#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Detect, rebuild, sync, and validate only stale Phase181 custom ROS2 typesupport add-ons.

"""Refresh verified Phase181 custom ROS2 typesupport add-ons without operator-memory steps.

The command deliberately orchestrates the existing materialization boundaries:

* the candidate builder writes only below ``build/phase181/<distro>/candidate``;
* the sync command copies only a validated candidate inventory into ``Packages``;
* the final validator proves the tracked add-on matches its static interface and
  current base runtime.

Without ``--apply`` this is a read-only check.  With ``--apply`` it rebuilds
only add-ons whose static-interface or base-runtime manifest identity changed.
The caller must provide explicit external source and Unity paths; this script
never invents machine-specific toolchain locations.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import sys
from typing import Callable, Mapping, Sequence

try:
    from foxrun_custom_typesupport_common import (
        SUPPORTED_DISTROS,
        addon_package_id,
        base_runtime_package_id,
        normalized_json_sha256,
    )
except ModuleNotFoundError:  # pragma: no cover - direct script invocation
    from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
        SUPPORTED_DISTROS,
        addon_package_id,
        base_runtime_package_id,
        normalized_json_sha256,
    )


ERROR_CODE = "FOXRUN_TYPESUPPORT004"
RUNTIME_PACKAGE_PREFIX = "dev.unity2foxglove.ros2forunity.runtime."
STATIC_INTERFACE_PACKAGE_ID = "dev.unity2foxglove.foxrun.ros2.interfaces"
_ROS2CS_INSTALL_READY_FILES = (
    "share/rosidl_generator_cs/cmake/rosidl_generator_csConfig.cmake",
    "share/builtin_interfaces/cmake/builtin_interfacesConfig.cmake",
    "include/builtin_interfaces/builtin_interfaces/msg/detail/time__struct.hpp",
    "lib/dotnet/ros2cs_common.dll",
    "lib/dotnet/builtin_interfaces_assembly.dll",
)


class AddonRefreshError(RuntimeError):
    """A bounded failure for the Phase181 add-on refresh transaction."""

    def __init__(self, remediation: str):
        """Record the operator-facing remediation with the stable refresh error code."""

        self.code = ERROR_CODE
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


@dataclass(frozen=True)
class AddonRefreshState:
    """Describe whether one tracked add-on still matches its two source identities."""

    distro: str
    current: bool
    reasons: tuple[str, ...]


@dataclass(frozen=True)
class AddonRefreshRequest:
    """Represent one explicit bounded add-on refresh request."""

    root: Path
    distros: tuple[str, ...]
    apply: bool
    ros2cs_source: Path | None = None
    r2fu_source: Path | None = None
    unity: Path | None = None
    ros2_roots: tuple[tuple[str, Path], ...] = ()


def repository_root() -> Path:
    """Return this repository root without relying on the current directory."""

    return Path(__file__).resolve().parents[3]


def _read_object(path: Path, remediation: str) -> dict[str, object]:
    """Read one required JSON object using a bounded public failure label."""

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AddonRefreshError(remediation) from exc
    if not isinstance(value, dict):
        raise AddonRefreshError(remediation)
    return value


def _mapping_text(value: object, key: str) -> str | None:
    """Return one non-empty text field from a JSON object-like value."""

    if not isinstance(value, Mapping):
        return None
    candidate = value.get(key)
    return candidate if isinstance(candidate, str) and candidate else None


def _static_interface_digest(root: Path) -> str:
    """Read the canonical digest that every generated add-on must embed."""

    lock = _read_object(
        root / "Packages" / STATIC_INTERFACE_PACKAGE_ID / "RuntimeSupport" / "foxrun-ros2-interface-lock.json",
        "repair-static-interface-lock",
    )
    digest = _mapping_text(lock, "interfaceDigest")
    if digest is None:
        raise AddonRefreshError("repair-static-interface-lock")
    return digest


def _runtime_manifest_hash(root: Path, distro: str) -> str:
    """Return the normalized identity that binds an add-on to its base runtime."""

    manifest = _read_object(
        root / "Packages" / base_runtime_package_id(distro) / "RuntimeSupport" / "runtime-manifest.json",
        "repair-base-runtime-manifest",
    )
    return normalized_json_sha256(manifest)


def _inspect_typesupport_package(
    root: Path,
    distro: str,
    package_root: Path,
    *,
    missing_reason: str,
) -> AddonRefreshState:
    """Compare one tracked or candidate package against the two durable identities.

    This intentionally compares only durable identities.  Full payload and
    RMW-closure validation remains the final validator's responsibility.
    """

    if distro not in SUPPORTED_DISTROS:
        raise AddonRefreshError("select-supported-ros-distro")
    root = Path(root).resolve()
    expected_interface = _static_interface_digest(root)
    expected_runtime = _runtime_manifest_hash(root, distro)
    manifest_path = Path(package_root) / "RuntimeSupport" / "typesupport-manifest.json"
    if not manifest_path.is_file():
        return AddonRefreshState(distro, False, (missing_reason,))
    try:
        manifest = _read_object(manifest_path, "repair-typesupport-manifest")
    except AddonRefreshError:
        return AddonRefreshState(distro, False, ("typesupport-manifest",))

    reasons: list[str] = []
    source_digest = _mapping_text(manifest.get("source"), "interfaceDigest")
    runtime_digest = _mapping_text(manifest.get("baseRuntime"), "runtimeManifestSha256")
    if source_digest != expected_interface:
        reasons.append("static-interface")
    if runtime_digest != expected_runtime:
        reasons.append("runtime-manifest")
    return AddonRefreshState(distro, not reasons, tuple(reasons))


def inspect_addon_state(root: Path, distro: str) -> AddonRefreshState:
    """Check whether one tracked add-on requires a fresh candidate build."""

    root = Path(root).resolve()
    return _inspect_typesupport_package(
        root,
        distro,
        root / "Packages" / addon_package_id(distro),
        missing_reason="missing-addon",
    )


def _candidate_is_validated(root: Path, distro: str) -> bool:
    """Require the candidate builder's durable proof before a reuse-only sync."""

    evidence_path = root / "build" / "phase181" / distro / "candidate" / "e" / "candidate-validation.json"
    try:
        evidence = _read_object(evidence_path, "repair-candidate-validation")
    except AddonRefreshError:
        return False
    return (
        evidence.get("schemaVersion") == 1
        and evidence.get("distro") == distro
        and evidence.get("validated") is True
    )


def _candidate_inventory_is_exact(candidate_root: Path) -> bool:
    """Accept candidate reuse only when every file is represented by its inventory."""

    candidate_root = Path(candidate_root)
    try:
        inventory = _read_object(
            candidate_root / "RuntimeSupport" / "typesupport-inventory.json",
            "repair-typesupport-inventory",
        )
    except AddonRefreshError:
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


def inspect_candidate_state(root: Path, distro: str) -> AddonRefreshState:
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
        return AddonRefreshState(distro, False, ("candidate-validation",))
    if not _candidate_inventory_is_exact(candidate_root):
        return AddonRefreshState(distro, False, ("candidate-inventory",))
    return state


def require_single_active_runtime(root: Path) -> str:
    """Fail before Unity work when manifest and lock select anything but one runtime."""

    manifest = _read_object(root / "Unity2Foxglove" / "Packages" / "manifest.json", "repair-unity-package-manifest")
    dependencies = manifest.get("dependencies")
    if not isinstance(dependencies, Mapping):
        raise AddonRefreshError("repair-unity-package-manifest")
    active = sorted(
        name
        for name in dependencies
        if isinstance(name, str) and name.startswith(RUNTIME_PACKAGE_PREFIX)
    )
    if len(active) != 1:
        raise AddonRefreshError("select-exactly-one-runtime-before-add-on-build")
    lock = _read_object(root / "Unity2Foxglove" / "Packages" / "packages-lock.json", "repair-unity-package-lock")
    locked_dependencies = lock.get("dependencies")
    if not isinstance(locked_dependencies, Mapping):
        raise AddonRefreshError("repair-unity-package-lock")
    locked = sorted(
        name
        for name in locked_dependencies
        if isinstance(name, str) and name.startswith(RUNTIME_PACKAGE_PREFIX)
    )
    if locked != active:
        raise AddonRefreshError("select-exactly-one-runtime-before-add-on-build")
    return active[0]


def _require_apply_toolchain(request: AddonRefreshRequest, distro: str) -> tuple[Path, Path, Path, Path]:
    """Validate the explicit external inputs required for one real candidate build."""

    if request.ros2cs_source is None:
        raise AddonRefreshError("provide-ros2cs-source")
    if request.r2fu_source is None:
        raise AddonRefreshError("provide-r2fu-source")
    if request.unity is None:
        raise AddonRefreshError("provide-unity-editor")
    ros2cs_source = Path(request.ros2cs_source).resolve()
    r2fu_source = Path(request.r2fu_source).resolve()
    unity = Path(request.unity).resolve()
    ros2cs_install = ros2cs_source / ("install-" + distro)
    if not ros2cs_source.is_dir() or not ros2cs_install.is_dir():
        raise AddonRefreshError("repair-ros2cs-install-for-selected-distro")
    _ros2cs_install_snapshot(ros2cs_install)
    if _ros2cs_install_build_is_active(ros2cs_install):
        raise AddonRefreshError("wait-for-active-ros2cs-colcon-build")
    if not r2fu_source.is_dir():
        raise AddonRefreshError("repair-r2fu-source")
    if not unity.is_file():
        raise AddonRefreshError("repair-unity-editor")
    return ros2cs_source, ros2cs_install, r2fu_source, unity


def _ros2cs_install_snapshot(ros2cs_install: Path) -> tuple[tuple[str, int, int], ...]:
    """Require the managed ros2cs closure and return a stable-build fingerprint."""

    snapshot: list[tuple[str, int, int]] = []
    for relative in _ROS2CS_INSTALL_READY_FILES:
        path = Path(ros2cs_install) / relative
        if not path.is_file():
            raise AddonRefreshError("wait-for-complete-ros2cs-install")
        try:
            stat = path.stat()
        except OSError as exc:
            raise AddonRefreshError("wait-for-complete-ros2cs-install") from exc
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


def _script(root: Path, name: str) -> Path:
    """Resolve one sibling Phase181 script beneath this repository only."""

    path = root / "Scripts" / "ros2forunity" / "interfaces" / name
    if not path.is_file():
        raise AddonRefreshError("repair-phase181-add-on-tooling")
    return path


def _explicit_ros2_root(request: AddonRefreshRequest, distro: str) -> Path | None:
    """Return the sole explicit ROS root for one distro, when supplied."""

    matches = [Path(path).resolve() for candidate, path in request.ros2_roots if candidate == distro]
    if len(matches) > 1:
        raise AddonRefreshError("provide-one-ros2-root-per-distro")
    return matches[0] if matches else None


def build_command(
    request: AddonRefreshRequest,
    distro: str,
    *,
    toolchain: tuple[Path, Path, Path, Path] | None = None,
) -> list[str]:
    """Build the exact argument vector for one candidate-only materialization."""

    root = Path(request.root).resolve()
    ros2cs_source, ros2cs_install, r2fu_source, unity = (
        toolchain
        if toolchain is not None
        else _require_apply_toolchain(request, distro)
    )
    command = [
        sys.executable,
        str(_script(root, "build_foxrun_custom_typesupport_addon.py")),
        "--distro",
        distro,
        "--ros2cs-source",
        str(ros2cs_source),
        "--ros2cs-install",
        str(ros2cs_install),
        "--r2fu-source",
        str(r2fu_source),
        "--build-root",
        str(root / "build"),
        "--unity",
        str(unity),
    ]
    ros2_root = _explicit_ros2_root(request, distro)
    if ros2_root is not None:
        command.extend(("--ros2-root", str(ros2_root)))
    return command


def sync_command(root: Path, distro: str) -> list[str]:
    """Build the exact argument vector for the validated-candidate sync boundary."""

    return [
        sys.executable,
        str(_script(root, "sync_foxrun_custom_typesupport_addon.py")),
        "--distro",
        distro,
    ]


def validation_command(root: Path, distro: str) -> list[str]:
    """Build the final validation command for one distro's advertised RMW closure."""

    command = [
        sys.executable,
        str(_script(root, "validate_foxrun_custom_typesupport_addon.py")),
        "--distro",
        distro,
    ]
    required_rmws = ("rmw_fastrtps_cpp", "rmw_zenoh_cpp") if distro == "lyrical" else ("rmw_fastrtps_cpp",)
    for rmw in required_rmws:
        command.extend(("--require-rmw", rmw))
    return command


def _run_child(command: Sequence[str], *, root: Path, runner: Callable[..., subprocess.CompletedProcess] | None) -> None:
    """Run one child by argv only and surface a bounded orchestration failure."""

    execute = runner or subprocess.run
    result = execute(list(command), cwd=str(root), check=False)
    if getattr(result, "returncode", 1) != 0:
        raise AddonRefreshError("repair-phase181-add-on-command")


def run_refresh(
    request: AddonRefreshRequest,
    *,
    runner: Callable[..., subprocess.CompletedProcess] | None = None,
) -> tuple[AddonRefreshState, ...]:
    """Run the bounded refresh lifecycle, rebuilding only stale packages."""

    root = Path(request.root).resolve()
    if not root.is_dir() or not (root / "Packages").is_dir() or not (root / "Scripts").is_dir():
        raise AddonRefreshError("provide-unity2foxglove-repository-root")
    distros = tuple(dict.fromkeys(request.distros))
    if not distros or any(distro not in SUPPORTED_DISTROS for distro in distros):
        raise AddonRefreshError("select-supported-ros-distro")
    selected_runtime = require_single_active_runtime(root)
    print("[phase181-addons] active runtime: " + selected_runtime, flush=True)

    states: list[AddonRefreshState] = []
    for distro in distros:
        state = inspect_addon_state(root, distro)
        states.append(state)
        if state.current:
            print("[phase181-addons] " + distro + ": current; validating.", flush=True)
            _run_child(validation_command(root, distro), root=root, runner=runner)
            continue
        reason_text = ", ".join(state.reasons)
        if not request.apply:
            print("[phase181-addons] " + distro + ": stale (" + reason_text + "); rerun with --apply.", flush=True)
            continue
        candidate = inspect_candidate_state(root, distro)
        if candidate.current:
            print(
                "[phase181-addons] " + distro + ": reusing matching validated candidate; synchronizing.",
                flush=True,
            )
        else:
            print(
                "[phase181-addons] " + distro + ": stale (" + reason_text
                + "); checking that the matching ros2cs install is idle and complete.",
                flush=True,
            )
            toolchain = _require_apply_toolchain(request, distro)
            _, ros2cs_install, _, _ = toolchain
            toolchain_snapshot = _ros2cs_install_snapshot(ros2cs_install)
            print(
                "[phase181-addons] " + distro
                + ": ros2cs install is complete; building isolated candidate. This can take several minutes.",
                flush=True,
            )
            _run_child(
                build_command(request, distro, toolchain=toolchain),
                root=root,
                runner=runner,
            )
            if _ros2cs_install_snapshot(ros2cs_install) != toolchain_snapshot:
                raise AddonRefreshError("wait-for-stable-ros2cs-install-and-rebuild-candidate")
            print("[phase181-addons] " + distro + ": candidate validated; synchronizing.", flush=True)
        _run_child(sync_command(root, distro), root=root, runner=runner)
        print("[phase181-addons] " + distro + ": synchronized; validating tracked package.", flush=True)
        _run_child(validation_command(root, distro), root=root, runner=runner)
    return tuple(states)


def parse_args(argv: Sequence[str] | None = None) -> AddonRefreshRequest:
    """Parse a safe read-only check or explicit real refresh request."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", action="append", choices=SUPPORTED_DISTROS, help="Limit work to one distro; repeatable.")
    parser.add_argument("--apply", action="store_true", help="Build and sync only stale add-ons; the default is read-only.")
    parser.add_argument("--ros2cs-source", type=Path)
    parser.add_argument("--r2fu-source", type=Path)
    parser.add_argument("--unity", type=Path)
    parser.add_argument(
        "--ros2-root",
        action="append",
        default=[],
        metavar="DISTRO=PATH",
        help="Explicit ROS root for an isolated worktree; repeat once per selected distro.",
    )
    args = parser.parse_args(argv)
    ros2_roots: list[tuple[str, Path]] = []
    for assignment in args.ros2_root:
        distro, separator, path = assignment.partition("=")
        if not separator or distro not in SUPPORTED_DISTROS or not path:
            parser.error("--ros2-root must use DISTRO=PATH with a supported distro")
        ros2_roots.append((distro, Path(path)))
    return AddonRefreshRequest(
        root=repository_root(),
        distros=tuple(args.distro or SUPPORTED_DISTROS),
        apply=args.apply,
        ros2cs_source=args.ros2cs_source,
        r2fu_source=args.r2fu_source,
        unity=args.unity,
        ros2_roots=tuple(ros2_roots),
    )


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command-line entry point."""

    request = parse_args(argv)
    try:
        states = run_refresh(request)
    except AddonRefreshError as exc:
        print(str(exc), file=sys.stderr)
        return 1
    stale = [state.distro for state in states if not state.current]
    if stale and not request.apply:
        print("NEEDS_REBUILD: " + ", ".join(stale), file=sys.stderr)
        return 2
    print("PASS: Phase181 custom typesupport add-ons are current and validated.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
