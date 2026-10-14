#!/usr/bin/env python3
"""Shared validation primitives for Phase181 custom ROS2 typesupport add-ons.

The add-on package is intentionally a verified artifact layer.  This module
contains no build invocation, ROS environment mutation, or Unity API use; it
only compares declared package data against the selected static interface and
base runtime inputs.
"""


from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "foxrun_custom_typesupport_common.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import re
from pathlib import Path
from typing import Any, Mapping, Sequence

from .inventory import _require_sha256, _require_text, _safe_relative_path, _validate_payload_entry
from .models import (
    AddonValidationError,
    EXPECTED_PLUGIN_PLATFORMS,
    _KNOWN_RMW_BASE_RUNTIME_LIBRARIES,
)

def _has_restricted_windows_plugin_importer(text: str) -> bool:
    """Accept Unity's legacy and Unity 6000 PluginImporter serializations.

    Unity 2021-era packages serialize the player target as
    ``Standalone: Windows``.  Unity 6000 may instead serialize a separate
    ``Win64`` block after disabling the generic Standalone entry.  Both are
    genuine Unity-generated representations of the same restricted importer;
    require the safety-critical Any/Editor/Windows facts rather than one
    version-specific YAML layout.
    """

    if "PluginImporter:" not in text:
        return False

    legacy_any = _legacy_plugin_importer_platform_entry(text, "Any:")
    legacy_editor = _legacy_plugin_importer_platform_entry(text, "Editor: Editor")
    legacy_windows = _legacy_plugin_importer_platform_entry(text, "Standalone: Windows")
    if legacy_any is not None or legacy_editor is not None or legacy_windows is not None:
        return (
            legacy_any is not None
            and "enabled: 0" in legacy_any
            and legacy_editor is not None
            and "enabled: 1" in legacy_editor
            and "CPU: x86_64" in legacy_editor
            and "OS: Windows" in legacy_editor
            and legacy_windows is not None
            and "enabled: 1" in legacy_windows
            and "CPU: x86_64" in legacy_windows
        )

    any_block = _plugin_importer_platform_block(text, ("Any:",))
    editor_block = _plugin_importer_platform_block(text, ("Editor: Editor", "Editor:"))
    if (
        any_block is None
        or editor_block is None
        or "enabled: 0" not in any_block
        or "enabled: 1" not in editor_block
        or "CPU: x86_64" not in editor_block
        or "OS: Windows" not in editor_block
    ):
        return False

    unity6000_win64 = _plugin_importer_platform_block(text, ("Win64:",))
    return unity6000_win64 is not None and "enabled: 1" in unity6000_win64


def _legacy_plugin_importer_platform_entry(text: str, header: str) -> str | None:
    """Return the owning list-of-pairs entry for one legacy platform key."""

    lines = text.splitlines()
    for index, line in enumerate(lines):
        if line.strip() != header:
            continue
        item_index = index - 1
        while item_index >= 0 and lines[item_index].strip() != "- first:":
            item_index -= 1
        if item_index < 0:
            continue
        item_indentation = len(lines[item_index]) - len(lines[item_index].lstrip(" "))
        block = [lines[item_index]]
        for following in lines[item_index + 1 :]:
            following_indentation = len(following) - len(following.lstrip(" "))
            if following.strip() and following_indentation <= item_indentation:
                break
            block.append(following)
        candidate = "\n".join(block)
        if header in candidate:
            return candidate
    return None


def _plugin_importer_platform_block(text: str, headers: tuple[str, ...]) -> str | None:
    """Return one platform entry without needing a third-party YAML parser."""

    lines = text.splitlines()
    for index, line in enumerate(lines):
        stripped = line.strip()
        if stripped not in headers:
            continue
        indentation = len(line) - len(line.lstrip(" "))
        block = [line]
        for following in lines[index + 1 :]:
            following_indentation = len(following) - len(following.lstrip(" "))
            if following.strip() and following_indentation <= indentation:
                break
            block.append(following)
        return "\n".join(block)
    return None


def _validate_native_payload(
    addon_root: Path,
    manifest: Mapping[str, Any],
    base_runtime_root: Path,
) -> None:
    """Implement the internal validate native payload step."""
    libraries = manifest.get("nativeLibraries")
    if not isinstance(libraries, list) or not libraries:
        raise AddonValidationError("repair-native-typesupport-closure")
    add_on_native_root = addon_root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
    base_native_root = base_runtime_root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
    listed_paths: set[Path] = set()
    for item in libraries:
        if not isinstance(item, dict):
            raise AddonValidationError("repair-native-typesupport-closure")
        path = _validate_payload_entry(
            addon_root,
            item,
            expected_name=None,
            remediation="repair-native-typesupport-closure",
        )
        if path.suffix.lower() != ".dll":
            raise AddonValidationError("repair-native-typesupport-closure")
        if item.get("classification") not in {"direct", "transitive"}:
            raise AddonValidationError("repair-native-typesupport-closure")
        try:
            path.relative_to(add_on_native_root)
        except ValueError as exc:
            raise AddonValidationError("repair-native-typesupport-closure") from exc
        meta = path.with_name(path.name + ".meta")
        if not meta.is_file() or not _has_restricted_windows_plugin_importer(
            meta.read_text(encoding="utf-8", errors="replace")
        ):
            # Native typesupport is loaded by Unity's plugin loader, not by the
            # managed assembly resolver.  A complete DLL closure without this
            # importer contract reaches ros2cs but fails at the first native
            # symbol lookup.
            raise AddonValidationError("repair-native-plugin-importer")
        listed_paths.add(path.resolve())

    _validate_pe_dependency_closure(listed_paths, add_on_native_root, base_native_root)


def _validate_rmw_closures(
    manifest: Mapping[str, Any],
    supported_rmws: Sequence[str],
    addon_root: Path,
    base_runtime_root: Path,
) -> None:
    """Require explicit, path-free closure evidence for each declared RMW.

    FastDDS custom typesupport alone does not establish a working Lyrical/Zenoh
    path.  Each RMW declares both its base-runtime library closure and the
    complete generated custom-message closure observed in the candidate.
    """

    closures = manifest.get("rmwClosures")
    if not isinstance(closures, Mapping) or set(closures) != set(supported_rmws):
        raise AddonValidationError("repair-required-rmw-closure")
    native_libraries = manifest.get("nativeLibraries")
    if not isinstance(native_libraries, list):
        raise AddonValidationError("repair-required-rmw-closure")
    declared_addon_paths = {
        _safe_relative_path(_require_text(item, "path", "repair-required-rmw-closure")).as_posix()
        for item in native_libraries
        if isinstance(item, Mapping)
    }
    if len(declared_addon_paths) != len(native_libraries):
        raise AddonValidationError("repair-required-rmw-closure")

    base_native_root = base_runtime_root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
    if not base_native_root.is_dir():
        raise AddonValidationError("repair-required-rmw-closure")
    base_by_name = {path.name.lower() for path in base_native_root.glob("*.dll")}

    for rmw in supported_rmws:
        closure = closures.get(rmw)
        if not isinstance(closure, Mapping):
            raise AddonValidationError("repair-required-rmw-closure")
        base_libraries = _validated_closure_library_names(
            closure.get("baseRuntimeLibraries"), "repair-required-rmw-closure"
        )
        add_on_libraries = _validated_closure_addon_paths(
            closure.get("addOnLibraries"), "repair-required-rmw-closure"
        )
        expected = _KNOWN_RMW_BASE_RUNTIME_LIBRARIES.get(rmw, ())
        if not set(expected).issubset(set(base_libraries)):
            raise AddonValidationError("repair-required-rmw-closure")
        if any(name.lower() not in base_by_name for name in base_libraries):
            raise AddonValidationError("repair-required-rmw-closure")
        if set(add_on_libraries) != declared_addon_paths:
            raise AddonValidationError("repair-required-rmw-closure")


def _validated_closure_library_names(value: object, remediation: str) -> tuple[str, ...]:
    """Implement the internal validated closure library names step."""
    if not isinstance(value, list) or not value:
        raise AddonValidationError(remediation)
    names = tuple(value)
    if (
        any(
            not isinstance(name, str)
            or re.fullmatch(r"[A-Za-z0-9_.-]+\.dll", name) is None
            or "/" in name
            or "\\" in name
            for name in names
        )
        or len({name.lower() for name in names}) != len(names)
    ):
        raise AddonValidationError(remediation)
    return names


def _validated_closure_addon_paths(value: object, remediation: str) -> tuple[str, ...]:
    """Implement the internal validated closure addon paths step."""
    if not isinstance(value, list) or not value:
        raise AddonValidationError(remediation)
    paths = tuple(
        _safe_relative_path(item).as_posix() if isinstance(item, str) else ""
        for item in value
    )
    if any(not path.endswith(".dll") for path in paths) or len(set(paths)) != len(paths):
        raise AddonValidationError(remediation)
    return paths


def _validate_pe_dependency_closure(
    add_on_libraries: set[Path],
    add_on_native_root: Path,
    base_native_root: Path,
) -> None:
    """Resolve PE imports only through approved package roots or Windows DLLs."""

    try:
        from characterize_foxrun_custom_interface import pe_imports
    except ModuleNotFoundError:  # pragma: no cover - package import test path
        from Scripts.ros2forunity.interfaces.characterize_foxrun_custom_interface import pe_imports

    if not base_native_root.is_dir():
        raise AddonValidationError("repair-native-typesupport-closure")
    add_on_by_name = {path.name.lower(): path for path in add_on_libraries}
    base_by_name = {path.name.lower(): path for path in base_native_root.glob("*.dll")}
    windows_system = {
        "advapi32.dll", "api-ms-win-core-console-l1-1-0.dll", "api-ms-win-core-debug-l1-1-0.dll",
        "api-ms-win-core-errorhandling-l1-1-0.dll", "api-ms-win-core-file-l1-1-0.dll",
        "api-ms-win-core-file-l1-2-0.dll", "api-ms-win-core-handle-l1-1-0.dll",
        "api-ms-win-core-heap-l1-1-0.dll", "api-ms-win-core-libraryloader-l1-1-0.dll",
        "api-ms-win-core-localization-l1-2-0.dll", "api-ms-win-core-memory-l1-1-0.dll",
        "api-ms-win-core-processenvironment-l1-1-0.dll", "api-ms-win-core-processthreads-l1-1-0.dll",
        "api-ms-win-core-profile-l1-1-0.dll", "api-ms-win-core-rtlsupport-l1-1-0.dll",
        "api-ms-win-core-string-l1-1-0.dll", "api-ms-win-core-synch-l1-1-0.dll",
        "api-ms-win-core-sysinfo-l1-1-0.dll", "api-ms-win-core-timezone-l1-1-0.dll",
        "api-ms-win-core-util-l1-1-0.dll", "api-ms-win-crt-conio-l1-1-0.dll",
        "api-ms-win-crt-heap-l1-1-0.dll", "api-ms-win-crt-locale-l1-1-0.dll",
        "api-ms-win-crt-math-l1-1-0.dll", "api-ms-win-crt-runtime-l1-1-0.dll",
        "api-ms-win-crt-stdio-l1-1-0.dll", "api-ms-win-crt-string-l1-1-0.dll",
        "api-ms-win-crt-time-l1-1-0.dll", "bcrypt.dll", "combase.dll", "gdi32.dll",
        "kernel32.dll", "kernelbase.dll", "msvcp140.dll", "ntdll.dll", "ole32.dll",
        "shell32.dll", "user32.dll", "vcruntime140.dll", "vcruntime140_1.dll", "ws2_32.dll",
    }
    pending = list(add_on_libraries)
    visited: set[str] = set()
    while pending:
        current = pending.pop()
        key = current.name.lower()
        if key in visited:
            continue
        visited.add(key)
        for imported in pe_imports(current):
            import_name = imported.lower()
            if import_name in windows_system:
                continue
            candidate = add_on_by_name.get(import_name) or base_by_name.get(import_name)
            if candidate is None:
                raise AddonValidationError("repair-native-typesupport-closure")
            if candidate.name.lower() in add_on_by_name:
                pending.append(candidate)

__all__ = [name for name in globals() if not name.startswith("__")]
