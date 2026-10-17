"""Unity asset, metadata, and required-file checks."""

from __future__ import annotations

from .foundation import *
def check_required_files(results: list[CheckResult]) -> None:
    """Validate release-critical files that must be present in the package."""
    required = [
        PACKAGE / "README.md",
        PACKAGE / "LICENSE",
        PACKAGE / "Runtime" / "Unity.FoxgloveSDK.asmdef",
        PACKAGE / "Editor" / "Unity.FoxgloveSDK.Editor.asmdef",
        PACKAGE / "Editor" / "SourceGenerators" / "src" / "Unity.FoxgloveSDK.SourceGenerators.asmdef",
        PACKAGE / "Runtime" / "Schemas" / "Proto" / "Unity.FoxgloveSDK.Proto.asmdef",
        PACKAGE / "Runtime" / "link.xml",
        PACKAGE / "Plugins" / "Google.Protobuf" / "Google.Protobuf.dll",
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))
        meta = Path(str(path) + ".meta")
        add(
            results,
            f"required meta: {meta.name}",
            meta.exists(),
            rel(meta),
        )


def check_sample_meta(
    results: list[CheckResult],
    samples_files: list[Path] | None = None,
    additional_files: list[Path] | None = None,
) -> None:
    """Ensure ordinary Unity assets have valid, unique .meta identities."""
    samples_files = samples_files if samples_files is not None else list(iter_files(SAMPLES))
    files = list(samples_files) + list(additional_files or [])
    missing: list[str] = []
    malformed: list[str] = []
    duplicate: list[str] = []
    guids: dict[str, str] = {}
    for path in files:
        if path.suffix == ".meta" or path.name == "README.md":
            continue
        if path.suffix.lower() not in UNITY_META_EXTENSIONS:
            continue
        meta = Path(str(path) + ".meta")
        if not meta.is_file():
            missing.append(rel(path))
            continue
        try:
            text = meta.read_text(encoding="utf-8")
        except OSError:
            malformed.append(rel(meta))
            continue
        match = META_GUID_RE.search(text)
        if match is None:
            malformed.append(rel(meta))
            continue
        guid = match.group(1).lower()
        previous = guids.get(guid)
        if previous is not None:
            duplicate.append(f"{rel(meta)} duplicates {previous}")
        else:
            guids[guid] = rel(meta)
    add(
        results,
        "sample Unity asset .meta files",
        not missing and not malformed and not duplicate,
        "; ".join(
            missing[:MAX_REPORTED_MISSING_META]
            + malformed[:MAX_REPORTED_MISSING_META]
            + duplicate[:MAX_REPORTED_MISSING_META]
        )
        if missing or malformed or duplicate
        else f"{len(guids)} ordinary asset metas have unique valid GUIDs",
    )


def check_sdk_script_meta(results: list[CheckResult], sdk_files: list[Path] | None = None) -> None:
    """Ensure every C# source shipped in the SDK package has a Unity meta sidecar."""
    files = sdk_files if sdk_files is not None else list(PACKAGE.rglob("*.cs"))
    missing: list[str] = []
    malformed: list[str] = []
    guids: dict[str, str] = {}
    for path in files:
        if not path.is_file() or path.suffix.casefold() != ".cs":
            continue
        meta = Path(str(path) + ".meta")
        if not meta.is_file():
            missing.append(rel(path))
            continue
        try:
            text = meta.read_text(encoding="utf-8")
        except OSError:
            malformed.append(rel(meta))
            continue
        match = META_GUID_RE.search(text)
        if match is None:
            malformed.append(rel(meta))
            continue
        guid = match.group(1).lower()
        if guid in guids:
            malformed.append(f"{rel(meta)} duplicates {guids[guid]}")
        else:
            guids[guid] = rel(meta)

    offenders = missing[:MAX_REPORTED_MISSING_META] + malformed[:MAX_REPORTED_MISSING_META]
    add(
        results,
        "SDK C# source .meta files",
        not missing and not malformed,
        "; ".join(offenders) if offenders else f"{len(guids)} SDK C# sources have valid unique metas",
    )


def check_sample_boundaries(results: list[CheckResult]) -> None:
    """Verify Basic and FullDemo sample boundaries remain intentional."""
    basic = SAMPLES / "BasicVisualization"
    full = SAMPLES / "FullDemoVisualization"

    forbidden_basic = [
        basic / "Scripts",
        basic / "Settings",
        basic / "InputSystem_Actions.inputactions",
        basic / "FoxgloveFullLayout.json",
        basic / "Scenes" / "FullDemoVisualization.unity",
    ]
    leaks = [rel(p) for p in forbidden_basic if p.exists()]
    add(results, "Basic sample remains minimal", not leaks, "; ".join(leaks) if leaks else "no FullDemo-only files")

    required_full = [
        full / "FoxgloveFullLayout.json",
        full / "FoxgloveFullLayout.json.meta",
        full / "Scenes" / "FullDemoVisualization.unity",
        full / "Scripts" / "FoxgloveDemoSetup.cs",
        full / "Scripts" / "MouseDragCube.cs",
        full / "Scripts" / "TestLog.cs",
        full / "Settings" / "DefaultVolumeProfile.asset",
        full / "Settings" / "UniversalRenderPipelineGlobalSettings.asset",
    ]
    missing = [rel(p) for p in required_full if not p.exists()]
    add(results, "FullDemo required files", not missing, "; ".join(missing) if missing else "all required files present")

    forbidden_full = [
        full / "InputSystem_Actions.inputactions",
        full / "InputSystem_Actions.inputactions.meta",
    ]
    conflicts = [rel(p) for p in forbidden_full if p.exists()]
    add(results, "FullDemo avoids project-level input action assets", not conflicts, "; ".join(conflicts) if conflicts else "no InputSystem_Actions asset")



__all__ = sorted(name for name in globals() if not name.startswith("__"))
