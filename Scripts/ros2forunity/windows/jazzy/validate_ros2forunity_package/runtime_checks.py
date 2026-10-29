from __future__ import annotations
from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "validate_ros2forunity_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
del _Phase192Path

from .text_boundaries import *

def check_runtime_inventory(results: list[CheckResult]) -> None:
    """Validate the generated Jazzy runtime artifact inventory."""
    if not RUNTIME_INVENTORY.exists():
        add(results, "runtime inventory exists", False, rel(RUNTIME_INVENTORY))
        return

    data = load_json(RUNTIME_INVENTORY, results, "runtime inventory parses")
    if data is None:
        return

    expected = {
        "schemaVersion": 1,
        "runtimeId": "r2fu-jazzy-win64",
        "artifactName": "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip",
        "rosDistro": "jazzy",
        "defaultRmwImplementation": EXPECTED_RMW_IMPLEMENTATION,
        "platform": "win64",
        "buildType": "standalone",
        "redistributionStatus": "candidate_not_published",
    }
    for key, value in expected.items():
        add(results, f"runtime inventory {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    supported_rmw = data.get("supportedRmwImplementations", [])
    add(
        results,
        "runtime inventory supported RMW implementations",
        isinstance(supported_rmw, list) and list(supported_rmw) == list(SUPPORTED_RMW_IMPLEMENTATIONS),
        f"supportedRmwImplementations={supported_rmw!r}",
    )

    manifest_data = load_json(MANIFEST, results, "runtime inventory manifest cross-check parses")
    current = manifest_data.get("currentRecommendedRuntime", {}) if isinstance(manifest_data, dict) else {}
    current = current if isinstance(current, dict) else {}
    cross_checks = {
        "artifactSize": current.get("artifactSize"),
        "sha256": current.get("artifactSha256"),
        "fileCount": current.get("inventoryFileCount"),
    }
    for key, value in cross_checks.items():
        add(
            results,
            f"runtime inventory {key}",
            data.get(key) == value,
            f"expected manifest value {value!r}, got {data.get(key)!r}",
        )

    counts = data.get("categoryCounts", {})
    add(
        results,
        "runtime inventory category counts",
        isinstance(counts, dict)
        and counts.get("native_libraries", 0) > 900
        and counts.get("managed_assemblies", 0) >= 2
        and counts.get("generated_message_assemblies", 0) >= 40,
        f"categoryCounts={counts!r}",
    )
    critical = data.get("knownCriticalFiles", [])
    critical_map = {
        item.get("name"): item for item in critical if isinstance(item, dict)
    } if isinstance(critical, list) else {}
    add(
        results,
        "runtime inventory critical DLLs present",
        all(critical_map.get(name, {}).get("present") for name in ("rcl.dll", "yaml.dll", "spdlog.dll", "fmt.dll")),
        f"knownCriticalFiles={critical!r}",
    )
    components = data.get("detectedComponents", [])
    component_text = json.dumps(components)
    add(
        results,
        "runtime inventory component families",
        "ros2cs" in component_text
        and "Fast DDS" in component_text
        and "RMW FastRTPS" in component_text
        and "Pixi runtime closure DLLs" in component_text,
        component_text[:MAX_REPORTED_OFFENDERS * 80],
    )
    files = data.get("files", [])
    add(
        results,
        "runtime inventory file entries",
        isinstance(files, list)
        and len(files) == data.get("fileCount")
        and all(isinstance(item, dict) and "path" in item and "sha256" in item for item in files),
        f"files={len(files) if isinstance(files, list) else type(files).__name__}",
    )


def check_no_runtime_artifacts(results: list[CheckResult]) -> None:
    """Reject runtime binaries and imported R2FU artifacts while the manifest says not_bundled."""
    offenders: list[str] = []
    for path in iter_files(PACKAGE):
        if (
            path.suffix.lower() in RUNTIME_BINARY_SUFFIXES
            or path.name in FORBIDDEN_RUNTIME_NAMES
        ) and not _is_editor_source_generator_artifact(path):
            offenders.append(rel(path))

    add(
        results,
        "optional package has no runtime binaries",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_OFFENDERS]) if offenders else "no runtime binaries found",
    )

    editor = PACKAGE / "Editor"
    invalid_editor_files = [
        rel(path)
        for path in iter_files(editor)
        if path.suffix.lower() not in ALLOWED_EDITOR_SUFFIXES
        and not _is_editor_source_generator_artifact(path)
    ] if editor.exists() else []
    editor_asmdefs = list(editor.glob("*.asmdef")) if editor.exists() else []
    editor_asmdefs_are_editor_only = True
    for asmdef in editor_asmdefs:
        asmdef_data = load_json(asmdef, results, f"editor asmdef parses: {asmdef.name}")
        editor_asmdefs_are_editor_only = editor_asmdefs_are_editor_only and bool(
            asmdef_data
            and asmdef_data.get("includePlatforms") == ["Editor"]
            and asmdef_data.get("autoReferenced") is True
        )
    installer = editor / "Ros2ForUnityRuntimeDefineInstaller.cs"
    selection = editor / "Ros2ForUnityRuntimeSelection.cs"
    installer_text = installer.read_text(encoding="utf-8", errors="replace") if installer.exists() else ""
    selection_text = selection.read_text(encoding="utf-8", errors="replace") if selection.exists() else ""
    compile_symbol_surface = installer_text + "\n" + selection_text
    add(
        results,
        "optional package editor surface only enables runtime compile symbol",
        not invalid_editor_files
        and editor_asmdefs_are_editor_only
        and "RuntimePackagePrefix" in selection_text
        and "DiscoverCandidateRuntimes" in selection_text
        and "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in compile_symbol_surface
        and "Ros2ForUnityRuntimeSelection.GetStatus()" in installer_text
        and "RuntimeCompileSymbols" not in installer_text
        and "NamedBuildTarget.Standalone" in installer_text
        and "using ROS2;" not in installer_text
        and "ROS2UnityComponent" not in installer_text,
        "; ".join(invalid_editor_files[:MAX_REPORTED_OFFENDERS]) if invalid_editor_files else rel(installer),
    )


__all__ = [name for name in globals() if not name.startswith("__")]
