#!/usr/bin/env python3
"""Build one Phase181 custom ROS2 typesupport add-on below ``build/phase181``.

The script deliberately has two materialization boundaries.  rosidl output is
first characterized in an out-of-tree candidate workspace.  A Unity batch
importer then generates the managed-DLL PluginImporter metadata in that
candidate.  Only the sibling ``sync_...`` command may copy a fully validated
candidate to ``Packages/``.
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from dataclasses import dataclass
from typing import Any, Iterable, Mapping, Sequence

try:
    from characterize_foxrun_custom_interface import (
        CharacterizationError,
        CharacterizationRequest,
        characterize,
    )
    from foxrun_custom_typesupport_common import (
        AddonValidationRequest,
        OPTIONAL_FACADE_PACKAGE_ID,
        ROS_PACKAGE_NAME,
        STATIC_INTERFACE_PACKAGE_ID,
        addon_package_id,
        base_runtime_package_id,
        compute_static_interface_digest,
        file_sha256,
        normalized_json_sha256,
        validate_addon,
    )
except ModuleNotFoundError:  # pragma: no cover - direct script invocation
    from Scripts.ros2forunity.interfaces.characterize_foxrun_custom_interface import (
        CharacterizationError,
        CharacterizationRequest,
        characterize,
    )
    from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
        AddonValidationRequest,
        OPTIONAL_FACADE_PACKAGE_ID,
        ROS_PACKAGE_NAME,
        STATIC_INTERFACE_PACKAGE_ID,
        addon_package_id,
        base_runtime_package_id,
        compute_static_interface_digest,
        file_sha256,
        normalized_json_sha256,
        validate_addon,
    )


ERROR_CODE = "FOXRUN_TYPESUPPORT002"
MANAGED_ASSEMBLY_NAME = ROS_PACKAGE_NAME + "_assembly"
MANAGED_ASSEMBLY_FILE = MANAGED_ASSEMBLY_NAME + ".dll"
GENERATED_CATALOG_FILE = "FoxRunCustomTypesupportCatalog.g.cs"
GENERATED_CATALOG_ASMDEF = "Unity2Foxglove.FoxRun.CustomRos2Typesupport.asmdef"
_RMW_BASE_RUNTIME_LIBRARIES = {
    "rmw_fastrtps_cpp": ("rmw_fastrtps_cpp.dll",),
    "rmw_zenoh_cpp": ("rmw_zenoh_cpp.dll", "zenohc.dll"),
}


class CandidateBuildError(RuntimeError):
    """A bounded candidate-build failure that exposes no machine-private data."""

    def __init__(self, remediation: str):
        """Initializes the stable public code and operator remediation."""
        self.code = ERROR_CODE
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


@dataclass(frozen=True)
class CandidateBuildRequest:
    """Immutable operator input for one disposable custom-typesupport candidate build."""

    distro: str
    static_interface_package: Path
    base_runtime_package: Path
    ros2_root: Path
    ros2cs_source: Path | None
    ros2cs_install: Path | None
    r2fu_source: Path | None
    build_root: Path
    generator: str = "Ninja"
    unity_executable: Path | None = None
    repo_root: Path | None = None


@dataclass(frozen=True)
class CandidateBuildResult:
    """Successful candidate package identity and the static interface digest it embeds."""

    distro: str
    candidate_package: Path
    interface_digest: str


def _phase181_distro_root(request: CandidateBuildRequest) -> Path:
    """Return the bounded per-distribution candidate root below the repository build directory."""
    build_root = Path(request.build_root)
    if build_root.name.lower() != "build":
        raise CandidateBuildError("use-repository-build-root")
    root = build_root / "phase181" / request.distro
    try:
        root.resolve().relative_to(build_root.resolve())
    except ValueError as exc:
        raise CandidateBuildError("use-repository-build-root") from exc
    return root


def candidate_package_root(request: CandidateBuildRequest) -> Path:
    """Return the sole private candidate package location for one distro."""

    return _phase181_distro_root(request) / "candidate" / "package"


def verify_candidate_source_lock(request: CandidateBuildRequest) -> str:
    """Validate the tracked static lock before invoking any native build tool."""

    lock_path = Path(request.static_interface_package) / "RuntimeSupport" / "foxrun-ros2-interface-lock.json"
    try:
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise CandidateBuildError("repair-static-interface-lock") from exc
    if not isinstance(lock, dict):
        raise CandidateBuildError("repair-static-interface-lock")
    digest = lock.get("interfaceDigest")
    if (
        lock.get("unityPackageId") != STATIC_INTERFACE_PACKAGE_ID
        or lock.get("rosPackageName") != ROS_PACKAGE_NAME
        or lock.get("interfaceRevision") != 1
        or not isinstance(digest, str)
        or len(digest) != 64
        or compute_static_interface_digest(Path(request.static_interface_package)) != digest
    ):
        raise CandidateBuildError("repair-static-interface-lock")
    return digest


def select_candidate_native_libraries(paths: Iterable[Path]) -> tuple[Path, ...]:
    """Keep the exact native custom-message closure and reject Python tooling.

    rosidl emits a Python generator DLL that depends on the ROS build Python
    runtime.  It is a build-time artifact, not a Unity Player dependency.  All
    remaining custom package DLLs are retained and their PE imports are checked
    by the shared add-on validator before any sync.
    """

    prefix = ROS_PACKAGE_NAME.lower()
    selected = []
    for path in paths:
        name = path.name.lower()
        if not name.endswith(".dll") or not name.startswith(prefix):
            continue
        if name.endswith("__rosidl_generator_py.dll"):
            continue
        selected.append(path)
    return tuple(selected)


def _repo_root(request: CandidateBuildRequest) -> Path:
    """Resolve the repository root supplied by a test or the checked-in script location."""
    if request.repo_root is not None:
        return Path(request.repo_root)
    return Path(__file__).resolve().parents[3]


def _load_json(path: Path, remediation: str) -> Mapping[str, Any]:
    """Load one object-shaped JSON document or raise a bounded remediation error."""
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise CandidateBuildError(remediation) from exc
    if not isinstance(payload, dict):
        raise CandidateBuildError(remediation)
    return payload


def _runtime_rmws(runtime_manifest: Mapping[str, Any], distro: str) -> tuple[str, ...]:
    """Read RMW support through the same capability precedence as the runtime.

    Older Humble/Jazzy runtime manifests expose one legacy ``rmwImplementation``
    field; newer manifests may provide an explicit union or communication-mode
    records.  The build tool accepts all three representations but never
    invents an RMW from the distro name.
    """

    explicit = runtime_manifest.get("supportedRmwImplementations")
    if isinstance(explicit, list) and explicit and all(isinstance(item, str) and item for item in explicit):
        rmws = explicit
    else:
        modes = runtime_manifest.get("communicationModes")
        if isinstance(modes, list):
            from_modes = [
                item.get("rmwImplementation")
                for item in modes
                if isinstance(item, dict) and isinstance(item.get("rmwImplementation"), str) and item["rmwImplementation"]
            ]
            rmws = from_modes if from_modes else None
        else:
            rmws = None
        if not rmws:
            legacy = runtime_manifest.get("rmwImplementation")
            rmws = [legacy] if isinstance(legacy, str) and legacy else None

    if not rmws:
        raise CandidateBuildError("repair-base-runtime-rmw-policy")
    normalized = tuple(sorted(set(rmws)))
    if distro == "lyrical" and not {"rmw_fastrtps_cpp", "rmw_zenoh_cpp"}.issubset(normalized):
        raise CandidateBuildError("repair-lyrical-rmw-policy")
    return normalized


def _rmw_closures(
    runtime_manifest: Mapping[str, Any],
    distro: str,
    native_entries: Sequence[Mapping[str, str]],
) -> Mapping[str, Mapping[str, list[str]]]:
    """Record the generated closure that must be available for each RMW."""

    add_on_libraries = [entry["path"] for entry in native_entries]
    closures: dict[str, Mapping[str, list[str]]] = {}
    for rmw in _runtime_rmws(runtime_manifest, distro):
        base_libraries = _RMW_BASE_RUNTIME_LIBRARIES.get(rmw)
        if base_libraries is None:
            raise CandidateBuildError("repair-required-rmw-closure")
        closures[rmw] = {
            "baseRuntimeLibraries": list(base_libraries),
            "addOnLibraries": add_on_libraries,
        }
    return closures


def _candidate_assembly_path(request: CandidateBuildRequest) -> Path:
    """Return the managed assembly emitted by the controlled candidate characterization."""
    return _phase181_distro_root(request) / "candidate" / "i" / "lib" / "dotnet" / MANAGED_ASSEMBLY_FILE


def _candidate_native_paths(request: CandidateBuildRequest) -> tuple[Path, ...]:
    """Return the deterministic native custom-message closure from the candidate output."""
    native_root = _phase181_distro_root(request) / "candidate" / "i" / "bin"
    return select_candidate_native_libraries(sorted(native_root.glob("*.dll"), key=lambda item: item.name.lower()))


def _copy_file(source: Path, target: Path) -> None:
    """Copy one candidate artifact after creating only its target parent directory."""
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)


def _write_utf8_lf(target: Path, content: str) -> None:
    """Write generated package text as exact LF bytes on every host.

    The typesupport inventory is byte-exact and the repository checkout policy
    is LF.  ``Path.write_text`` delegates newline conversion to the host text
    mode, which would make an inventory generated on Windows disagree with the
    same package after Git's LF checkout normalization.  Generated text must
    therefore bypass text-mode newline translation.
    """

    target.parent.mkdir(parents=True, exist_ok=True)
    normalized = content.replace("\r\n", "\n").replace("\r", "\n")
    target.write_bytes(normalized.encode("utf-8"))


def _write_candidate_texts(package_root: Path, repo_root: Path, distro: str) -> None:
    """Write plain legal/readme files into the private candidate only."""

    license_source = repo_root / "LICENSE"
    notice_source = repo_root / "THIRD_PARTY_NOTICES.md"
    if not license_source.is_file() or not notice_source.is_file():
        raise CandidateBuildError("provide-repository-license-notices")
    _copy_file(license_source, package_root / "LICENSE")
    _write_utf8_lf(
        package_root / "README.md",
        "# Unity2Foxglove FoxRun Custom ROS2 Typesupport - " + distro.title() + " Win64\n\n"
        "This generated add-on carries the validated custom-message closure for the locked "
        "`dev.unity2foxglove.foxrun.ros2.interfaces` source package. It must be selected "
        "with its exact matching ROS2 For Unity runtime; it is not a standalone ROS runtime.\n",
    )
    _write_utf8_lf(
        package_root / "THIRD_PARTY_NOTICES.md",
        "# Third-Party Notices\n\n"
        "This add-on redistributes generated ROSIDL C#/native custom-message support built "
        "from the locked Unity2Foxglove FoxRun interface package through ros2cs and ROS2 For Unity. "
        "The selected base runtime package owns the ROS2/RMW runtime closure and its full notices.\n\n"
        "The repository-wide third-party notice is included below for license continuity.\n\n"
        + notice_source.read_text(encoding="utf-8"),
    )


def _package_metadata(request: CandidateBuildRequest, package_root: Path) -> None:
    """Copy normalized add-on package metadata into the disposable candidate package."""
    source = _repo_root(request) / "Packages" / addon_package_id(request.distro) / "package.json"
    payload = _load_json(source, "repair-addon-package-json")
    package_root.mkdir(parents=True, exist_ok=True)
    _write_utf8_lf(package_root / "package.json", json.dumps(payload, sort_keys=True, indent=2) + "\n")


def _typesupport_type_map(managed_evidence: Mapping[str, Any]) -> list[dict[str, str]]:
    """Extract the generated ROS2.Message map from characterized managed evidence."""
    messages = managed_evidence.get("messages")
    if not isinstance(messages, list):
        raise CandidateBuildError("repair-managed-characterization-evidence")
    result: list[dict[str, str]] = []
    prefix = ROS_PACKAGE_NAME + ".msg."
    for item in messages:
        if not isinstance(item, dict):
            continue
        full_name = item.get("fullName")
        interfaces = item.get("interfaces")
        if (
            not isinstance(full_name, str)
            or not full_name.startswith(prefix)
            or "+" in full_name
            or not isinstance(interfaces, list)
            or "ROS2.Message" not in interfaces
        ):
            continue
        result.append(
            {
                "canonicalRosType": ROS_PACKAGE_NAME + "/msg/" + full_name[len(prefix):],
                "managedType": full_name,
            }
        )
    result.sort(key=lambda item: item["canonicalRosType"])
    if not result:
        raise CandidateBuildError("repair-generated-managed-type-map")
    return result


def _catalog_source(
    *,
    distro: str,
    interface_digest: str,
    type_map: Sequence[Mapping[str, str]],
) -> str:
    """Render the deterministic compile-time catalog for one distro add-on."""
    base_runtime = base_runtime_package_id(distro)
    supported_rmws = ", ".join(
        '"' + item + '"'
        for item in ("rmw_fastrtps_cpp", "rmw_zenoh_cpp")
        if distro == "lyrical"
    )
    if not supported_rmws:
        supported_rmws = '"rmw_fastrtps_cpp"'
    type_entries = ",\n                ".join(
        "new FoxRunRos2CustomTypesupportTypeMapEntry(\""
        + item["canonicalRosType"]
        + "\", typeof("
        + item["managedType"]
        + ").FullName)"
        for item in type_map
    )
    metadata_properties = (
        '        public string SourcePackageId { get { return "'
        + STATIC_INTERFACE_PACKAGE_ID
        + '"; } }\n'
        + '        public string RosPackageName { get { return "'
        + ROS_PACKAGE_NAME
        + '"; } }\n'
        + '        public int InterfaceRevision { get { return 1; } }\n'
        + '        public string InterfaceDigest { get { return "'
        + interface_digest
        + '"; } }\n'
        + '        public string BaseRuntimePackageId { get { return "'
        + base_runtime
        + '"; } }\n'
    )
    metadata_constants = (
        "    // This public, compile-time-only seam is consumed by generated user\n"
        "    // code. It deliberately carries only the immutable source lock, not\n"
        "    // mutable runtime selection or credential data.\n"
        "    public static class FoxRunRos2CustomTypesupportMetadata\n"
        "    {\n"
        '        public const string SourcePackageId = "'
        + STATIC_INTERFACE_PACKAGE_ID
        + '";\n'
        '        public const string RosPackageName = "'
        + ROS_PACKAGE_NAME
        + '";\n'
        "        public const int InterfaceRevision = 1;\n"
        '        public const string InterfaceDigest = "'
        + interface_digest
        + '";\n'
        '        public const string BaseRuntimePackageId = "'
        + base_runtime
        + '";\n'
        "    }\n\n"
    )
    return """// <auto-generated />
// Phase181 validated custom ROS2 typesupport catalog. Do not hand-edit.
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System.Collections.Generic;
using UnityEngine;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity2Foxglove.FoxRun.CustomRos2Typesupport
{
""" + metadata_constants + """    internal sealed class FoxRunCustomTypesupportCatalog : IFoxRunRos2CustomTypesupportCatalog
    {
        private static readonly string[] s_rmws = { """ + supported_rmws + """ };
        private static readonly FoxRunRos2CustomTypesupportTypeMapEntry[] s_typeMap =
        {
                """ + type_entries + """
        };

""" + metadata_properties + """        public string Platform { get { return \"win64\"; } }
        public IReadOnlyList<string> SupportedRmwImplementations { get { return s_rmws; } }
        public IReadOnlyList<FoxRunRos2CustomTypesupportTypeMapEntry> TypeMap { get { return s_typeMap; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            FoxRunRos2CustomTypesupportCatalogRegistry.Register(new FoxRunCustomTypesupportCatalog());
        }
    }
}
#endif
"""


def _catalog_asmdef() -> Mapping[str, Any]:
    """Return the narrow Unity assembly definition for a generated typesupport catalog."""
    return {
        "name": "Unity2Foxglove.FoxRun.CustomRos2Typesupport",
        "rootNamespace": "Unity2Foxglove.FoxRun.CustomRos2Typesupport",
        "references": [
            "Unity2Foxglove.Ros2ForUnity.Native",
            MANAGED_ASSEMBLY_NAME,
        ],
        "includePlatforms": ["Editor", "WindowsStandalone64"],
        "excludePlatforms": [],
        "allowUnsafeCode": False,
        "overrideReferences": False,
        "precompiledReferences": [],
        "autoReferenced": True,
        "defineConstraints": ["UNITY2FOXGLOVE_ROS2_FOR_UNITY"],
        "versionDefines": [],
        "noEngineReferences": False,
    }


def _write_candidate_manifest(
    request: CandidateBuildRequest,
    package_root: Path,
    interface_digest: str,
    managed_evidence: Mapping[str, Any],
    native_libraries: Sequence[Path],
) -> None:
    """Write the candidate provenance, managed map, and native/RMW closure manifest."""
    runtime_manifest = _load_json(
        Path(request.base_runtime_package) / "RuntimeSupport" / "runtime-manifest.json",
        "repair-base-runtime-manifest",
    )
    type_map = _typesupport_type_map(managed_evidence)
    managed_path = package_root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64" / MANAGED_ASSEMBLY_FILE
    if not managed_path.is_file():
        raise CandidateBuildError("repair-generated-managed-assembly")
    try:
        from foxrun_custom_typesupport_common import _base_ros2_message_identity
    except ModuleNotFoundError:  # pragma: no cover - direct script invocation
        from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import _base_ros2_message_identity
    ros2_message_identity = _base_ros2_message_identity(Path(request.base_runtime_package), None)
    plugin_meta = managed_path.with_name(managed_path.name + ".meta")
    if not plugin_meta.is_file():
        raise CandidateBuildError("generate-unity-plugin-importer-metadata")
    native_root = package_root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
    native_entries = []
    for source in native_libraries:
        target = native_root / source.name
        native_entries.append(
            {
                "path": target.relative_to(package_root).as_posix(),
                "sha256": file_sha256(target),
                "classification": "direct" if "_native.dll" in source.name else "transitive",
            }
        )
    native_entries.sort(key=lambda item: item["path"].lower())
    manifest = {
        "schemaVersion": 1,
        "source": {
            "upmPackageId": STATIC_INTERFACE_PACKAGE_ID,
            "rosPackageName": ROS_PACKAGE_NAME,
            "interfaceRevision": 1,
            "interfaceDigest": interface_digest,
            "generatorSchemaVersion": 1,
        },
        "distro": request.distro,
        "platform": "win64",
        "architecture": "x86_64",
        "baseRuntime": {
            "packageId": base_runtime_package_id(request.distro),
            "runtimeManifestSha256": normalized_json_sha256(runtime_manifest),
            "runtimeManifestVersion": runtime_manifest.get("schemaVersion"),
        },
        "supportedRmwImplementations": list(_runtime_rmws(runtime_manifest, request.distro)),
        "managed": {
            "assembly": {
                "path": managed_path.relative_to(package_root).as_posix(),
                "name": MANAGED_ASSEMBLY_NAME,
                "sha256": file_sha256(managed_path),
            },
            "typeMap": type_map,
            "ros2Message": dict(ros2_message_identity),
            "pluginImporter": {
                "metaPath": plugin_meta.relative_to(package_root).as_posix(),
                "includePlatforms": ["Editor", "WindowsStandalone64"],
            },
        },
        "nativeLibraries": native_entries,
        "rmwClosures": _rmw_closures(runtime_manifest, request.distro, native_entries),
        "provenance": {
            "source": "controlled-out-of-tree-build",
            "managedEvidenceSha256": file_sha256(
                _phase181_distro_root(request) / "candidate" / "e" / "managed.json"
            ),
        },
    }
    support = package_root / "RuntimeSupport"
    support.mkdir(parents=True, exist_ok=True)
    _write_utf8_lf(
        support / "typesupport-manifest.json",
        json.dumps(manifest, sort_keys=True, indent=2) + "\n",
    )
    generated = package_root / "Runtime" / "FoxRun" / "Generated"
    generated.mkdir(parents=True, exist_ok=True)
    _write_utf8_lf(
        generated / GENERATED_CATALOG_FILE,
        _catalog_source(distro=request.distro, interface_digest=interface_digest, type_map=type_map),
    )
    _write_utf8_lf(
        generated / GENERATED_CATALOG_ASMDEF,
        json.dumps(_catalog_asmdef(), sort_keys=True, indent=2) + "\n",
    )


def _inventory_role(relative: str) -> str:
    """Classify one candidate file by its public typesupport role."""
    if relative.endswith(".dll"):
        return "managed" if relative.endswith(MANAGED_ASSEMBLY_FILE) else "native"
    if relative.endswith(".cs"):
        return "catalog"
    if relative.endswith(".meta"):
        return "importer"
    if relative.startswith("RuntimeSupport/") or relative.endswith(".asmdef") or relative == "package.json":
        return "metadata"
    return "notice"


def _inventory_classification(relative: str) -> str:
    """Classify native libraries as direct generated support or transitive closure."""
    if relative.endswith("_native.dll"):
        return "direct"
    if relative.endswith(".dll"):
        return "transitive"
    return "metadata"


def _write_inventory(package_root: Path) -> None:
    """Write a sorted byte-exact inventory for the disposable add-on package."""
    excluded = {"RuntimeSupport/typesupport-inventory.json"}
    entries = []
    for path in sorted(package_root.rglob("*"), key=lambda item: item.as_posix().lower()):
        if not path.is_file():
            continue
        relative = path.relative_to(package_root).as_posix()
        if relative in excluded:
            continue
        entries.append(
            {
                "path": relative,
                "byteLength": path.stat().st_size,
                "sha256": file_sha256(path),
                "role": _inventory_role(relative),
                "classification": _inventory_classification(relative),
            }
        )
    support = package_root / "RuntimeSupport"
    support.mkdir(parents=True, exist_ok=True)
    _write_utf8_lf(
        support / "typesupport-inventory.json",
        json.dumps({"schemaVersion": 1, "entries": entries}, sort_keys=True, indent=2) + "\n",
    )


def _repair_tracked_addon_catalog(request: CandidateBuildRequest) -> Path:
    """Regenerate only a tracked catalog when a prior catalog template was invalid.

    This is intentionally narrower than ``build_candidate``: it never touches a
    native or managed payload, never creates a package, and derives the catalog
    exclusively from the validated static lock plus the tracked manifest.
    """

    interface_digest = verify_candidate_source_lock(request)
    repository_root = _repo_root(request).resolve()
    package_root = repository_root / "Packages" / addon_package_id(request.distro)
    if not package_root.is_dir():
        raise CandidateBuildError("repair-tracked-typesupport-package")

    manifest = _load_json(
        package_root / "RuntimeSupport" / "typesupport-manifest.json",
        "repair-typesupport-manifest",
    )
    source = manifest.get("source")
    base_runtime = manifest.get("baseRuntime")
    managed = manifest.get("managed")
    if (
        not isinstance(source, Mapping)
        or source.get("upmPackageId") != STATIC_INTERFACE_PACKAGE_ID
        or source.get("rosPackageName") != ROS_PACKAGE_NAME
        or source.get("interfaceRevision") != 1
        or source.get("interfaceDigest") != interface_digest
        or not isinstance(base_runtime, Mapping)
        or base_runtime.get("packageId") != base_runtime_package_id(request.distro)
        or not isinstance(managed, Mapping)
        or not isinstance(managed.get("typeMap"), list)
    ):
        raise CandidateBuildError("repair-typesupport-catalog-source")

    type_map: list[dict[str, str]] = []
    managed_prefix = ROS_PACKAGE_NAME + ".msg."
    for item in managed["typeMap"]:
        if not isinstance(item, Mapping):
            raise CandidateBuildError("repair-typesupport-catalog-source")
        canonical = item.get("canonicalRosType")
        managed_type = item.get("managedType")
        if not isinstance(canonical, str) or not isinstance(managed_type, str):
            raise CandidateBuildError("repair-typesupport-catalog-source")
        if not managed_type.startswith(managed_prefix):
            raise CandidateBuildError("repair-typesupport-catalog-source")
        message_name = managed_type[len(managed_prefix):]
        if (
            not message_name
            or not all(character.isalnum() or character == "_" for character in message_name)
            or canonical != ROS_PACKAGE_NAME + "/msg/" + message_name
        ):
            raise CandidateBuildError("repair-typesupport-catalog-source")
        type_map.append({"canonicalRosType": canonical, "managedType": managed_type})
    if not type_map:
        raise CandidateBuildError("repair-typesupport-catalog-source")

    generated_root = package_root / "Runtime" / "FoxRun" / "Generated"
    catalog_path = generated_root / GENERATED_CATALOG_FILE
    if not generated_root.is_dir() or not catalog_path.is_file():
        raise CandidateBuildError("repair-typesupport-catalog-source")

    _write_utf8_lf(
        catalog_path,
        _catalog_source(
            distro=request.distro,
            interface_digest=interface_digest,
            type_map=tuple(type_map),
        ),
    )
    _write_inventory(package_root)
    return catalog_path


def _unity_executable(request: CandidateBuildRequest) -> Path:
    """Return the explicitly supplied Unity executable or the supported Unity 6000 location."""
    if request.unity_executable is not None:
        return Path(request.unity_executable)
    return Path(os.environ.get("ProgramFiles", r"C:\\Program Files")) / "Unity" / "Hub" / "Editor" / "6000.3.14f1" / "Editor" / "Unity.exe"


def _unity_editor_process_is_running() -> bool:
    """Return whether any Unity Editor process is active without using a shell."""

    if os.name != "nt":
        return False
    result = subprocess.run(
        ("tasklist", "/FI", "IMAGENAME eq Unity.exe", "/FO", "CSV", "/NH"),
        shell=False,
        capture_output=True,
        text=True,
        errors="replace",
        check=False,
    )
    return result.returncode == 0 and "Unity.exe" in result.stdout


def _run_unity_plugin_importer(request: CandidateBuildRequest, managed_path: Path) -> None:
    """Ask Unity to generate the only allowed managed-DLL importer metadata."""

    unity = _unity_executable(request)
    project = _repo_root(request) / "Unity2Foxglove"
    output_meta = managed_path.with_name(managed_path.name + ".meta")
    if not unity.is_file() or not project.is_dir():
        raise CandidateBuildError("provide-unity-6000-plugin-importer")
    # A stale lockfile can remain after a prior Editor crash or controlled exit.
    # Only an actually running Editor blocks our batch import; Unity itself owns
    # stale-lock recovery when it opens the project.
    if (project / "Temp" / "UnityLockfile").exists() and _unity_editor_process_is_running():
        raise CandidateBuildError("close-unity-editor-before-importing-plugin-metadata")
    command = (
        str(unity),
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath",
        str(project),
        "-executeMethod",
        "Phase181TypesupportPluginImporterBuilder.Run",
        "-phase181TypesupportManagedInput",
        str(managed_path),
        "-phase181TypesupportManagedMetaOutput",
        str(output_meta),
        "-logFile",
        str(_phase181_distro_root(request) / "candidate" / "e" / "unity-plugin-importer.log"),
    )
    result = subprocess.run(command, shell=False, capture_output=True, text=True, errors="replace", check=False)
    if result.returncode != 0 or not output_meta.is_file():
        raise CandidateBuildError("repair-unity-plugin-importer-metadata")


def build_candidate(request: CandidateBuildRequest, *, check_source_only: bool = False) -> CandidateBuildResult:
    """Build, Unity-import, and validate one private candidate add-on."""

    interface_digest = verify_candidate_source_lock(request)
    if check_source_only:
        return CandidateBuildResult(request.distro, candidate_package_root(request), interface_digest)

    ros2cs_source, ros2cs_install, r2fu_source = _require_explicit_toolchain_sources(request)

    try:
        result = characterize(
            CharacterizationRequest(
                distro=request.distro,
                static_package=Path(request.static_interface_package),
                ros2_root=Path(request.ros2_root),
                ros2cs_source=ros2cs_source,
                ros2cs_install=ros2cs_install,
                r2fu_source=r2fu_source,
                build_root=Path(request.build_root),
                generator=request.generator,
                workspace_name="candidate",
            ),
            replace_existing=True,
        )
    except CharacterizationError as exc:
        raise CandidateBuildError("repair-controlled-typesupport-build") from exc
    candidate_root = candidate_package_root(request)
    if candidate_root.exists():
        shutil.rmtree(candidate_root)
    candidate_root.mkdir(parents=True, exist_ok=True)
    _package_metadata(request, candidate_root)
    _write_candidate_texts(candidate_root, _repo_root(request), request.distro)

    native_root = candidate_root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
    managed_target = native_root / MANAGED_ASSEMBLY_FILE
    _copy_file(result.managed_assembly, managed_target)
    native_libraries = _candidate_native_paths(request)
    if not native_libraries:
        raise CandidateBuildError("repair-generated-native-typesupport-closure")
    for native in native_libraries:
        _copy_file(native, native_root / native.name)

    _run_unity_plugin_importer(request, managed_target)
    managed_evidence = _load_json(
        _phase181_distro_root(request) / "candidate" / "e" / "managed.json",
        "repair-managed-characterization-evidence",
    )
    _write_candidate_manifest(request, candidate_root, interface_digest, managed_evidence, native_libraries)
    _write_inventory(candidate_root)

    try:
        validate_addon(
            AddonValidationRequest(
                distro=request.distro,
                addon_package=candidate_root,
                static_interface_package=Path(request.static_interface_package),
                base_runtime_package=Path(request.base_runtime_package),
                require_rmws=("rmw_fastrtps_cpp", "rmw_zenoh_cpp") if request.distro == "lyrical" else (),
            )
        )
    except Exception as exc:
        raise CandidateBuildError("repair-candidate-typesupport-validation") from exc
    evidence = _phase181_distro_root(request) / "candidate" / "e" / "candidate-validation.json"
    _write_utf8_lf(
        evidence,
        json.dumps(
            {
                "schemaVersion": 1,
                "distro": request.distro,
                "interfaceDigest": interface_digest,
                "candidatePackageSha256": normalized_json_sha256(
                    json.loads((candidate_root / "RuntimeSupport" / "typesupport-inventory.json").read_text(encoding="utf-8"))
                ),
                "validated": True,
            },
            sort_keys=True,
            indent=2,
        ) + "\n",
    )
    return CandidateBuildResult(request.distro, candidate_root, interface_digest)


def _default_repo_root() -> Path:
    """Return the repository root inferred from this checked-in interface tool."""
    return Path(__file__).resolve().parents[3]


def _require_explicit_toolchain_sources(request: CandidateBuildRequest) -> tuple[Path, Path, Path]:
    """Returns explicit build-toolchain paths without inventing machine-local defaults."""
    if request.ros2cs_source is None:
        raise CandidateBuildError("provide-ros2cs-source")
    if request.r2fu_source is None:
        raise CandidateBuildError("provide-r2fu-source")
    ros2cs_install = request.ros2cs_install or request.ros2cs_source / ("install-" + request.distro)
    return request.ros2cs_source, ros2cs_install, request.r2fu_source


def parse_args(argv: Sequence[str] | None = None) -> tuple[CandidateBuildRequest, bool, bool]:
    """Parse a candidate command without inventing external operator toolchain paths."""
    root = _default_repo_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", required=True, choices=("humble", "jazzy", "lyrical"))
    parser.add_argument("--static-interface-package", type=Path, default=root / "Packages" / STATIC_INTERFACE_PACKAGE_ID)
    parser.add_argument("--base-runtime-package", type=Path)
    parser.add_argument("--ros2-root", type=Path)
    parser.add_argument("--ros2cs-source", type=Path)
    parser.add_argument("--ros2cs-install", type=Path)
    parser.add_argument("--r2fu-source", type=Path)
    parser.add_argument("--build-root", type=Path, default=root / "build")
    parser.add_argument("--generator", default="Ninja")
    parser.add_argument("--unity", type=Path)
    parser.add_argument("--check-source", action="store_true")
    parser.add_argument("--repair-tracked-catalog", action="store_true")
    args = parser.parse_args(argv)
    if args.check_source and args.repair_tracked_catalog:
        parser.error("--check-source and --repair-tracked-catalog are mutually exclusive")
    ros2_root = args.ros2_root or root / "ros2-windows" / ("ros2_" + args.distro)
    ros2cs_install = args.ros2cs_install
    if ros2cs_install is None and args.ros2cs_source is not None:
        ros2cs_install = args.ros2cs_source / ("install-" + args.distro)
    return (
        CandidateBuildRequest(
            distro=args.distro,
            static_interface_package=args.static_interface_package,
            base_runtime_package=args.base_runtime_package or root / "Packages" / base_runtime_package_id(args.distro),
            ros2_root=ros2_root,
            ros2cs_source=args.ros2cs_source,
            ros2cs_install=ros2cs_install,
            r2fu_source=args.r2fu_source,
            build_root=args.build_root,
            generator=args.generator,
            unity_executable=args.unity,
            repo_root=root,
        ),
        args.check_source,
        args.repair_tracked_catalog,
    )


def main(argv: Sequence[str] | None = None) -> int:
    """Run source-only validation, catalog repair, or a controlled candidate build."""
    request, check_source_only, repair_tracked_catalog = parse_args(argv)
    try:
        if repair_tracked_catalog:
            catalog = _repair_tracked_addon_catalog(request)
            print("PASS:", request.distro, catalog)
            return 0
        result = build_candidate(request, check_source_only=check_source_only)
    except CandidateBuildError as exc:
        print(str(exc), file=sys.stderr)
        return 1
    print("PASS:", result.distro, result.candidate_package, result.interface_digest)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
