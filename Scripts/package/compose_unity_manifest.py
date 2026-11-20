"""Compose one supported Unity package profile into a project manifest."""

from __future__ import annotations

import argparse
import codecs
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
from typing import Mapping


REPO_ROOT = Path(__file__).resolve().parents[2]
PACKAGE_ROOT = REPO_ROOT / "Packages"
LOCAL_PACKAGE_PREFIX = "dev.unity2foxglove."
RUNTIME_PREFIX = "dev.unity2foxglove.ros2forunity.runtime."
TYPESUPPORT_PREFIX = "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport."
STATIC_INTERFACE_PACKAGE_ID = "dev.unity2foxglove.foxrun.ros2.interfaces"
DISTROS = ("humble", "jazzy", "lyrical")
PROFILE_BASE_PACKAGES = {
    "core": ("dev.unity2foxglove.sdk",),
    "bridge": ("dev.unity2foxglove.sdk", "dev.unity2foxglove.ros2bridge"),
    "gateway": ("dev.unity2foxglove.sdk", "dev.unity2foxglove.remotegateway.win64"),
    "native": ("dev.unity2foxglove.sdk", "dev.unity2foxglove.ros2forunity"),
    "native-custom": ("dev.unity2foxglove.sdk", "dev.unity2foxglove.ros2forunity"),
}


class ManifestCompositionError(ValueError):
    """Describe a fail-closed manifest composition error."""


def _reference_text(value: object) -> str | None:
    """Return a Unity manifest reference from its supported JSON forms."""
    if isinstance(value, str):
        return value
    if isinstance(value, Mapping) and isinstance(value.get("version"), str):
        return value["version"]
    return None


def _is_relative_to(path: Path, root: Path) -> bool:
    """Return whether a resolved path is contained by a resolved root."""
    try:
        path.relative_to(root)
    except ValueError:
        return False
    return True


def _load_package(package_root: Path, package_id: str) -> dict:
    """Load and authenticate one repository package manifest."""
    package_dir = package_root / package_id
    manifest = package_dir / "package.json"
    if not package_dir.is_dir() or not manifest.is_file():
        raise ManifestCompositionError(f"required package is missing: {package_id}")
    try:
        data = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ManifestCompositionError(f"invalid package manifest: {package_id}") from exc
    if not isinstance(data, dict) or data.get("name") != package_id:
        raise ManifestCompositionError(f"package name does not match path: {package_id}")
    return data


def _selected_packages(profile: str, distro: str | None) -> tuple[str, ...]:
    """Return the exact package ids for one README profile."""
    if profile not in PROFILE_BASE_PACKAGES:
        raise ManifestCompositionError(f"unsupported profile: {profile}")
    native = profile in {"native", "native-custom"}
    if native != (distro is not None):
        raise ManifestCompositionError("native profiles require --distro and other profiles forbid it")
    if distro is not None and distro not in DISTROS:
        raise ManifestCompositionError(f"unsupported distro: {distro}")
    selected = list(PROFILE_BASE_PACKAGES[profile])
    if native and distro is not None:
        selected.append(f"{RUNTIME_PREFIX}{distro}.win64")
    if profile == "native-custom" and distro is not None:
        selected.extend(
            (
                "dev.unity2foxglove.foxrun.ros2.interfaces",
                f"{TYPESUPPORT_PREFIX}{distro}.win64",
            )
        )
    return tuple(selected)


def _validate_existing_local_entries(
    dependencies: Mapping[str, object],
    manifest_parent: Path,
    package_root: Path,
) -> dict[str, dict]:
    """Reject forged local-looking entries before any manifest mutation."""
    package_root = package_root.resolve()
    existing: dict[str, dict] = {}
    for package_id, value in dependencies.items():
        if not isinstance(package_id, str) or not package_id.startswith(LOCAL_PACKAGE_PREFIX):
            continue
        reference = _reference_text(value)
        if reference is None or not reference.startswith("file:"):
            raise ManifestCompositionError(f"local package must use a file reference: {package_id}")
        raw_path = reference[5:]
        candidate = Path(raw_path)
        if candidate.is_absolute():
            raise ManifestCompositionError(f"local package reference is absolute: {package_id}")
        package_dir = (manifest_parent / candidate).resolve()
        if not _is_relative_to(package_dir, package_root):
            raise ManifestCompositionError(f"local package points outside Packages: {package_id}")
        manifest = package_dir / "package.json"
        if not manifest.is_file():
            raise ManifestCompositionError(f"local package manifest is missing: {package_id}")
        try:
            package_data = json.loads(manifest.read_text(encoding="utf-8"))
        except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise ManifestCompositionError(f"invalid local package manifest: {package_id}") from exc
        if not isinstance(package_data, dict) or package_data.get("name") != package_id:
            raise ManifestCompositionError(f"local package name does not match entry: {package_id}")
        existing[package_id] = package_data
    return existing


def _validate_selected_packages(
    selected: tuple[str, ...],
    package_data: Mapping[str, Mapping[str, object]],
) -> None:
    """Check selected package conflicts and local dependency closure."""
    selected_set = set(selected)
    for package_id in selected:
        data = package_data[package_id]
        conflicts = data.get("unity2foxgloveConflicts", ())
        if isinstance(conflicts, list) and selected_set.intersection(conflicts):
            raise ManifestCompositionError(f"selected packages violate conflicts: {package_id}")
        dependencies = data.get("dependencies", {})
        if not isinstance(dependencies, Mapping):
            raise ManifestCompositionError(f"invalid dependencies: {package_id}")
        for dependency, version in dependencies.items():
            if not isinstance(dependency, str) or not dependency.startswith(LOCAL_PACKAGE_PREFIX):
                continue
            if dependency not in selected_set:
                raise ManifestCompositionError(f"declared local dependency is absent: {package_id} -> {dependency}")
            if version != package_data[dependency].get("version"):
                raise ManifestCompositionError(f"declared local dependency version mismatch: {package_id} -> {dependency}")


def _should_remove_existing_package(
    package_id: str,
    package_data: Mapping[str, object],
    selected: set[str],
    selected_data: Mapping[str, Mapping[str, object]],
) -> bool:
    """Return whether an existing local package conflicts with the chosen profile."""
    if package_id.startswith(RUNTIME_PREFIX):
        return package_id not in {name for name in selected if name.startswith(RUNTIME_PREFIX)}
    if package_id.startswith(TYPESUPPORT_PREFIX):
        return package_id not in {name for name in selected if name.startswith(TYPESUPPORT_PREFIX)}
    if package_id == STATIC_INTERFACE_PACKAGE_ID:
        return package_id not in selected
    if package_id == "dev.unity2foxglove.ros2forunity":
        return package_id not in selected
    conflicts = package_data.get("unity2foxgloveConflicts", ())
    if isinstance(conflicts, list) and selected.intersection(conflicts):
        return True
    for data in selected_data.values():
        selected_conflicts = data.get("unity2foxgloveConflicts", ())
        if isinstance(selected_conflicts, list) and package_id in selected_conflicts:
            return True
    return False


def _line_ending(raw: bytes) -> bytes:
    """Return the manifest's existing line-ending bytes."""
    return b"\r\n" if b"\r\n" in raw else b"\n"


def _encode_manifest(document: Mapping[str, object], raw: bytes) -> bytes:
    """Encode a manifest while preserving its BOM, line endings, and final newline."""
    bom = codecs.BOM_UTF8 if raw.startswith(codecs.BOM_UTF8) else b""
    had_final_newline = raw.endswith((b"\n", b"\r"))
    text = json.dumps(document, ensure_ascii=False, indent=2)
    if _line_ending(raw) == b"\r\n":
        text = text.replace("\n", "\r\n")
    if had_final_newline:
        text += "\r\n" if _line_ending(raw) == b"\r\n" else "\n"
    return bom + text.encode("utf-8")


def _atomic_replace(path: Path, content: bytes) -> None:
    """Publish manifest bytes through a same-directory atomic replacement."""
    path.parent.mkdir(parents=True, exist_ok=True)
    original_mode = stat.S_IMODE(path.stat().st_mode) if path.exists() else None
    descriptor, temporary_name = tempfile.mkstemp(prefix=f".{path.name}.phase202-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(content)
            handle.flush()
            os.fsync(handle.fileno())
        if original_mode is not None:
            os.chmod(temporary_name, original_mode)
        os.replace(temporary_name, path)
    finally:
        try:
            os.unlink(temporary_name)
        except FileNotFoundError:
            pass


def compose_manifest(
    project: Path,
    profile: str,
    distro: str | None = None,
    *,
    dry_run: bool = False,
    package_root: Path = PACKAGE_ROOT,
) -> dict[str, object]:
    """Compose one profile into a target project manifest."""
    project = Path(project).expanduser().resolve()
    manifest_path = project / "Packages" / "manifest.json"
    if not manifest_path.is_file():
        raise ManifestCompositionError(f"manifest is missing: {manifest_path}")
    raw = manifest_path.read_bytes()
    try:
        document = json.loads(raw.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ManifestCompositionError("manifest must be strict JSON") from exc
    if not isinstance(document, dict) or not isinstance(document.get("dependencies"), dict):
        raise ManifestCompositionError("manifest dependencies must be an object")
    if not (stat.S_IMODE(manifest_path.stat().st_mode) & stat.S_IWUSR):
        raise ManifestCompositionError("manifest is read-only")

    package_root = Path(package_root).expanduser().resolve()
    dependencies = document["dependencies"]
    existing_local = _validate_existing_local_entries(dependencies, manifest_path.parent, package_root)
    selected = _selected_packages(profile, distro)
    selected_data = {package_id: _load_package(package_root, package_id) for package_id in selected}
    _validate_selected_packages(selected, selected_data)
    if any(package_id.startswith(RUNTIME_PREFIX) for package_id in selected):
        legacy_runtime = project / "Assets" / "Ros2ForUnity"
        if legacy_runtime.exists():
            raise ManifestCompositionError("legacy Assets/Ros2ForUnity runtime conflicts with packaged runtime")

    updated = dict(document)
    updated_dependencies = dict(dependencies)
    selected_set = set(selected)
    removed = [
        package_id
        for package_id, package_data in existing_local.items()
        if package_id not in selected_set
        and _should_remove_existing_package(package_id, package_data, selected_set, selected_data)
    ]
    for package_id in removed:
        del updated_dependencies[package_id]
    for package_id in selected:
        reference = os.path.relpath(package_root / package_id, manifest_path.parent).replace(os.sep, "/")
        updated_dependencies[package_id] = "file:" + reference
    updated["dependencies"] = updated_dependencies
    encoded = _encode_manifest(updated, raw)
    if not dry_run and encoded != raw:
        _atomic_replace(manifest_path, encoded)
    result = {
        "profile": profile,
        "distro": distro,
        "added": [package_id for package_id in selected if package_id not in dependencies],
        "removed": removed,
        "manifest": str(manifest_path),
        "changed": encoded != raw,
    }
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    print("Restart Unity before changing distro after native ROS2 DLLs have loaded.")
    return result


def main(argv: list[str] | None = None) -> int:
    """Parse command arguments and compose the requested Unity manifest."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--profile", choices=sorted(PROFILE_BASE_PACKAGES), required=True)
    parser.add_argument("--distro", choices=DISTROS)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)
    package_root = Path(os.environ.get("PHASE202_PACKAGE_ROOT", str(PACKAGE_ROOT)))
    try:
        compose_manifest(
            args.project,
            args.profile,
            args.distro,
            dry_run=args.dry_run,
            package_root=package_root,
        )
    except (ManifestCompositionError, OSError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
