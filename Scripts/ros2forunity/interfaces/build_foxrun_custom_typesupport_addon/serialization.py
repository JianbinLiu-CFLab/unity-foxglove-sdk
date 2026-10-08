#!/usr/bin/env python3
"""Build one Phase181 custom ROS2 typesupport add-on below ``build/phase181``.

The script deliberately has two materialization boundaries.  rosidl output is
first characterized in an out-of-tree candidate workspace.  A Unity batch
importer then generates PluginImporter metadata for every managed and native
candidate DLL.  Only the sibling ``sync_...`` command may copy a fully
validated candidate to ``Packages/``.
"""


from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "build_foxrun_custom_typesupport_addon.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import json
import shutil
from pathlib import Path
from typing import Any, Mapping, Sequence

from .models import *

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
    target.write_bytes(_canonical_utf8_lf_bytes(content))


def _canonical_utf8_lf_bytes(content: str) -> bytes:
    """Return UTF-8 text with every host line ending normalized to LF."""

    return content.replace("\r\n", "\n").replace("\r", "\n").encode("utf-8")


def _read_canonical_license_bytes(license_source: Path) -> bytes:
    """Read repository legal text as the exact bytes permitted by an inventory."""

    try:
        return _canonical_utf8_lf_bytes(license_source.read_text(encoding="utf-8"))
    except (OSError, UnicodeError) as exc:
        raise CandidateBuildError("provide-repository-license-notices") from exc


def _write_canonical_license(license_source: Path, target: Path) -> None:
    """Copy repository legal text as the LF-canonical inventory payload."""

    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(_read_canonical_license_bytes(license_source))


def _write_candidate_texts(package_root: Path, repo_root: Path, distro: str) -> None:
    """Write plain legal/readme files into the private candidate only."""

    license_source = repo_root / "LICENSE"
    notice_source = repo_root / "THIRD_PARTY_NOTICES.md"
    if not license_source.is_file() or not notice_source.is_file():
        raise CandidateBuildError("provide-repository-license-notices")
    _write_canonical_license(license_source, package_root / "LICENSE")
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
    supported_rmws: Sequence[str],
) -> str:
    """Render the catalog from the validated runtime capability set."""
    base_runtime = base_runtime_package_id(distro)
    supported_rmws_source = ", ".join('"' + item + '"' for item in supported_rmws)
    if not supported_rmws_source:
        raise CandidateBuildError("repair-empty-rmw-capability")
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
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
using System.Collections.Generic;
using UnityEngine;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity2Foxglove.FoxRun.CustomRos2Typesupport
{
""" + metadata_constants + """    internal sealed class FoxRunCustomTypesupportCatalog : IFoxRunRos2CustomTypesupportCatalog
    {
        private static readonly string[] s_rmws = { """ + supported_rmws_source + """ };
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
            FoxRunRos2CustomTypesupportNativePluginBootstrap.Register(typeof(FoxRunCustomTypesupportCatalog).Assembly);
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
        "defineConstraints": ["UNITY2FOXGLOVE_ROS2_FOR_UNITY", "UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN"],
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
    managed_path = _managed_package_assembly_path(package_root)
    if not managed_path.is_file():
        raise CandidateBuildError("repair-generated-managed-assembly")
    try:
        from foxrun_custom_typesupport_common import _base_ros2_message_identity
    except ModuleNotFoundError:  # pragma: no cover - direct script invocation
        from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import _base_ros2_message_identity
    ros2_message_identity = _base_ros2_message_identity(Path(request.base_runtime_package), None)
    toolchain_provenance = _load_toolchain_provenance(request)
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
            "toolchainProvenanceSha256": file_sha256(toolchain_provenance),
            "toolchainInputIdentity": json.loads(
                toolchain_provenance.read_text(encoding="utf-8")
            )["inputIdentity"],
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
        _catalog_source(
            distro=request.distro,
            interface_digest=interface_digest,
            type_map=type_map,
            supported_rmws=_runtime_rmws(runtime_manifest, request.distro),
        ),
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
        # Unity-generated PluginImporter metadata is part of the native or
        # managed DLL contract.  Other asset metadata is Editor-local and
        # deliberately remains outside the portable payload inventory.
        if relative.endswith(".meta") and not relative.endswith(".dll.meta"):
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

__all__ = [name for name in globals() if not name.startswith("__")]
