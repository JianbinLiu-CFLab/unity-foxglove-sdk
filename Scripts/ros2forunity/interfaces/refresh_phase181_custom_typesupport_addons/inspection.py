#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from . import foundation as _foundation
from .foundation import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "refresh_phase181_custom_typesupport_addons.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def _inspect_typesupport_package(
    root: Path,
    distro: str,
    package_root: Path,
    *,
    missing_reason: str,
) -> _foundation.AddonRefreshState:
    """Compare one tracked or candidate package against the two durable identities.

    This intentionally compares only durable identities.  Full payload and
    RMW-closure validation remains the final validator's responsibility.
    """

    if distro not in _foundation.SUPPORTED_DISTROS:
        raise _foundation.AddonRefreshError("select-supported-ros-distro")
    root = Path(root).resolve()
    expected_interface = _static_interface_digest(root)
    expected_runtime = _runtime_manifest_hash(root, distro)
    manifest_path = Path(package_root) / "RuntimeSupport" / "typesupport-manifest.json"
    if not manifest_path.is_file():
        return _foundation.AddonRefreshState(distro, False, (missing_reason,))
    try:
        manifest = _read_object(manifest_path, "repair-typesupport-manifest")
    except _foundation.AddonRefreshError:
        return _foundation.AddonRefreshState(distro, False, ("typesupport-manifest",))

    reasons: list[str] = []
    source_digest = _mapping_text(manifest.get("source"), "interfaceDigest")
    runtime_digest = _mapping_text(manifest.get("baseRuntime"), "runtimeManifestSha256")
    if source_digest != expected_interface:
        reasons.append("static-interface")
    if runtime_digest != expected_runtime:
        reasons.append("runtime-manifest")
    return _foundation.AddonRefreshState(distro, not reasons, tuple(reasons))


def inspect_addon_state(root: Path, distro: str) -> _foundation.AddonRefreshState:
    """Check whether one tracked add-on requires a fresh candidate build."""

    root = Path(root).resolve()
    return _inspect_typesupport_package(
        root,
        distro,
        root / "Packages" / _foundation.addon_package_id(distro),
        missing_reason="missing-addon",
    )


def _candidate_is_validated(root: Path, distro: str) -> bool:
    """Require the candidate builder's durable proof before a reuse-only sync."""

    evidence_path = root / "build" / "phase181" / distro / "candidate" / "e" / "candidate-validation.json"
    try:
        evidence = _read_object(evidence_path, "repair-candidate-validation")
    except _foundation.AddonRefreshError:
        return False
    candidate_root = root / "build" / "phase181" / distro / "candidate" / "package"
    inventory_path = candidate_root / "RuntimeSupport" / "typesupport-inventory.json"
    try:
        inventory = _read_object(inventory_path, "repair-typesupport-inventory")
    except _foundation.AddonRefreshError:
        return False
    return (
        evidence.get("schemaVersion") == 1
        and evidence.get("distro") == distro
        and evidence.get("validated") is True
        and evidence.get("candidatePackageSha256") == _foundation.normalized_json_sha256(inventory)
    )


def _candidate_inventory_is_exact(candidate_root: Path) -> bool:
    """Accept candidate reuse only when every file is represented by its inventory."""

    candidate_root = Path(candidate_root)
    try:
        inventory = _read_object(
            candidate_root / "RuntimeSupport" / "typesupport-inventory.json",
            "repair-typesupport-inventory",
        )
    except _foundation.AddonRefreshError:
        return False
    entries = inventory.get("entries")
    if inventory.get("schemaVersion") != 1 or not isinstance(entries, list):
        return False
    expected: set[str] = {"RuntimeSupport/typesupport-inventory.json"}
    for entry in entries:
        if not isinstance(entry, Mapping):
            return False
        relative = _mapping_text(entry, "path")
        if relative is None or "\\" in relative:
            return False
        parsed = PurePosixPath(relative)
        if parsed.is_absolute() or any(part in {"", ".", ".."} for part in parsed.parts):
            return False
        normalized = parsed.as_posix()
        if normalized in expected:
            return False
        expected.add(normalized)
    actual = {
        path.relative_to(candidate_root).as_posix()
        for path in candidate_root.rglob("*")
        if path.is_file()
    }
    return actual == expected


def inspect_candidate_state(root: Path, distro: str) -> _foundation.AddonRefreshState:
    """Return a reusable state only for a current candidate with builder proof."""

    root = Path(root).resolve()
    candidate_root = root / "build" / "phase181" / distro / "candidate" / "package"
    state = _inspect_typesupport_package(
        root,
        distro,
        candidate_root,
        missing_reason="missing-candidate",
    )
    if not state.current:
        return state
    if not _candidate_is_validated(root, distro):
        return _foundation.AddonRefreshState(distro, False, ("candidate-validation",))
    if not _candidate_inventory_is_exact(candidate_root):
        return _foundation.AddonRefreshState(distro, False, ("candidate-inventory",))
    return state


def require_single_active_runtime(root: Path) -> str:
    """Fail before Unity work when manifest and lock select anything but one runtime."""

    manifest = _read_object(root / "Unity2Foxglove" / "Packages" / "manifest.json", "repair-unity-package-manifest")
    dependencies = manifest.get("dependencies")
    if not isinstance(dependencies, Mapping):
        raise _foundation.AddonRefreshError("repair-unity-package-manifest")
    active = sorted(
        name
        for name in dependencies
        if isinstance(name, str) and name.startswith(_foundation.RUNTIME_PACKAGE_PREFIX)
    )
    if len(active) != 1:
        raise _foundation.AddonRefreshError("select-exactly-one-runtime-before-add-on-build")
    lock = _read_object(root / "Unity2Foxglove" / "Packages" / "packages-lock.json", "repair-unity-package-lock")
    locked_dependencies = lock.get("dependencies")
    if not isinstance(locked_dependencies, Mapping):
        raise _foundation.AddonRefreshError("repair-unity-package-lock")
    locked = sorted(
        name
        for name in locked_dependencies
        if isinstance(name, str) and name.startswith(_foundation.RUNTIME_PACKAGE_PREFIX)
    )
    if locked != active:
        raise _foundation.AddonRefreshError("select-exactly-one-runtime-before-add-on-build")
    return active[0]


def _require_apply_toolchain(request: AddonRefreshRequest, distro: str) -> tuple[Path, Path, Path, Path]:
    """Validate the explicit external inputs required for one real candidate build."""

    if request.ros2cs_source is None:
        raise _foundation.AddonRefreshError("provide-ros2cs-source")
    if request.r2fu_source is None:
        raise _foundation.AddonRefreshError("provide-r2fu-source")
    if request.unity is None:
        raise _foundation.AddonRefreshError("provide-unity-editor")
    ros2cs_source = Path(request.ros2cs_source).resolve()
    r2fu_source = Path(request.r2fu_source).resolve()
    unity = Path(request.unity).resolve()
    ros2cs_install = ros2cs_source / ("install-" + distro)
    if not ros2cs_source.is_dir() or not ros2cs_install.is_dir():
        raise _foundation.AddonRefreshError("repair-ros2cs-install-for-selected-distro")
    _ros2cs_install_snapshot(ros2cs_install)
    if _ros2cs_install_build_is_active(ros2cs_install):
        raise _foundation.AddonRefreshError("wait-for-active-ros2cs-colcon-build")
    if not r2fu_source.is_dir():
        raise _foundation.AddonRefreshError("repair-r2fu-source")
    if not unity.is_file():
        raise _foundation.AddonRefreshError("repair-unity-editor")
    return ros2cs_source, ros2cs_install, r2fu_source, unity


def _ros2cs_install_snapshot(ros2cs_install: Path) -> tuple[tuple[str, int, int], ...]:
    """Require the managed ros2cs closure and return a stable-build fingerprint."""

    snapshot: list[tuple[str, int, int]] = []
    for relative in _foundation._ROS2CS_INSTALL_READY_FILES:
        path = Path(ros2cs_install) / relative
        if not path.is_file():
            raise _foundation.AddonRefreshError("wait-for-complete-ros2cs-install")
        try:
            stat = path.stat()
        except OSError as exc:
            raise _foundation.AddonRefreshError("wait-for-complete-ros2cs-install") from exc
        snapshot.append((relative, stat.st_size, stat.st_mtime_ns))
    return tuple(snapshot)


def _ros2cs_install_build_is_active(ros2cs_install: Path) -> bool:
    """Return whether a Windows colcon process is currently materializing this install."""

    if os.name != "nt":
        return False
    powershell = Path(os.environ.get("SystemRoot", r"C:\\Windows")) / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe"
    if not powershell.is_file():
        return False
    environment = dict(os.environ)
    environment["FOXRUN_TYPESUPPORT_ROS2CS_INSTALL"] = str(Path(ros2cs_install).resolve())
    query = (
        "$target = [IO.Path]::GetFullPath($env:FOXRUN_TYPESUPPORT_ROS2CS_INSTALL); "
        "$filter = \"Name = 'colcon.exe'\"; "
        "Get-CimInstance -ClassName Win32_Process -Filter $filter | "
        "Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($target, [StringComparison]::OrdinalIgnoreCase) -ge 0 } | "
        "Select-Object -First 1 -ExpandProperty ProcessId"
    )
    try:
        result = subprocess.run(
            (str(powershell), "-NoProfile", "-NonInteractive", "-Command", query),
            shell=False,
            capture_output=True,
            text=True,
            errors="replace",
            env=environment,
            check=False,
        )
    except OSError:
        return False
    return result.returncode == 0 and bool(result.stdout.strip())

__all__ = ["inspect_addon_state", "inspect_candidate_state", "require_single_active_runtime"]
