"""Shared validation primitives for Phase181 custom ROS2 typesupport add-ons.

The add-on package is intentionally a verified artifact layer.  This module
contains no build invocation, ROS environment mutation, or Unity API use; it
only compares declared package data against the selected static interface and
base runtime inputs.
"""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
from typing import Any, Iterable, Mapping, Sequence


STATIC_INTERFACE_PACKAGE_ID = "dev.unity2foxglove.foxrun.ros2.interfaces"
ROS_PACKAGE_NAME = "unity2foxglove_foxrun_interfaces_v1"
OPTIONAL_FACADE_PACKAGE_ID = "dev.unity2foxglove.ros2forunity"
SUPPORTED_DISTROS = ("humble", "jazzy", "lyrical")
REQUIRED_LICENSE_FILES = ("LICENSE", "README.md", "THIRD_PARTY_NOTICES.md")
EXPECTED_PLUGIN_PLATFORMS = ("Editor", "WindowsStandalone64")
_KNOWN_RMW_BASE_RUNTIME_LIBRARIES = {
    "rmw_fastrtps_cpp": ("rmw_fastrtps_cpp.dll",),
    "rmw_zenoh_cpp": ("rmw_zenoh_cpp.dll", "zenohc.dll"),
}
_SHA256 = re.compile(r"^[0-9a-f]{64}$")
_MVID = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
_ABSOLUTE_PATH = re.compile(r"^(?:[A-Za-z]:[\\\\/]|[\\\\/]{2}|/)")


class AddonValidationError(ValueError):
    """A bounded add-on validation failure with no machine-private path text."""

    code = "FOXRUN_TYPESUPPORT001"

    def __init__(self, remediation: str):
        """Initialize this object."""
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


@dataclass(frozen=True)
class AddonValidationRequest:
    """Explicit selected-package inputs for one add-on validation."""

    distro: str
    addon_package: Path
    static_interface_package: Path
    base_runtime_package: Path
    require_rmws: tuple[str, ...] = ()
    # Test fixtures use this injection point to keep the schema validator pure
    # when their tiny fake DLLs are deliberately not CLR assemblies. Production
    # callers leave it empty and inspect the selected base runtime read-only.
    base_ros2_message_identity: Mapping[str, str] | None = None


@dataclass(frozen=True)
class AddonValidationResult:
    """Publicly useful, machine-neutral result from a validated add-on."""

    distro: str
    package_id: str
    interface_digest: str
    supported_rmws: tuple[str, ...]


def addon_package_id(distro: str) -> str:
    """Return the sole supported custom typesupport add-on identity."""

    normalized = _normalize_distro(distro)
    return "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport." + normalized + ".win64"


def base_runtime_package_id(distro: str) -> str:
    """Return the exact R2FU base-runtime package identity for a distro."""

    normalized = _normalize_distro(distro)
    return "dev.unity2foxglove.ros2forunity.runtime." + normalized + ".win64"


def normalized_json_sha256(value: object) -> str:
    """Hash normalized JSON without retaining formatting or machine paths."""

    normalized = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def file_sha256(path: Path) -> str:
    """Return a payload SHA-256 using bounded streaming reads."""

    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def compute_static_interface_digest(static_interface_package: Path) -> str:
    """Compute the exact public length-framed static-interface source digest."""

    root = Path(static_interface_package)
    lock_path = "RuntimeSupport/foxrun-ros2-interface-lock.json"
    inputs: list[tuple[str, bytes]] = []
    try:
        candidates = list(root.rglob("*"))
    except OSError as exc:
        raise AddonValidationError("repair-static-interface-source") from exc
    for path in candidates:
        if not path.is_file():
            continue
        relative = path.relative_to(root).as_posix()
        if relative == lock_path or relative.endswith(".meta"):
            continue
        _safe_relative_path(relative)
        try:
            text = path.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError) as exc:
            raise AddonValidationError("repair-static-interface-source") from exc
        if text.startswith("\ufeff"):
            raise AddonValidationError("repair-static-interface-source")
        inputs.append((relative, text.replace("\r\n", "\n").replace("\r", "\n").encode("utf-8")))
    if not inputs:
        raise AddonValidationError("repair-static-interface-source")
    normalized = sorted(inputs, key=lambda item: item[0])
    if len({path.lower() for path, _ in normalized}) != len(normalized):
        raise AddonValidationError("repair-static-interface-source")
    digest = hashlib.sha256()
    _append_interface_digest_frame(digest, b"unity2foxglove:foxrun-ros2-interface-digest:v1")
    _append_interface_digest_frame(digest, b"1")
    for relative, content in normalized:
        _append_interface_digest_frame(digest, relative.encode("utf-8"))
        _append_interface_digest_frame(digest, content)
    return digest.hexdigest()


def _append_interface_digest_frame(digest: "hashlib._Hash", content: bytes) -> None:
    """Implement the internal append interface digest frame step."""
    digest.update(len(content).to_bytes(8, byteorder="big", signed=False))
    digest.update(content)


def validate_addon(request: AddonValidationRequest) -> AddonValidationResult:
    """Validate one add-on against exact source and runtime package inputs."""

    distro = _normalize_distro(request.distro)
    addon_root = Path(request.addon_package)
    static_root = Path(request.static_interface_package)
    base_root = Path(request.base_runtime_package)
    if not addon_root.is_dir() or not static_root.is_dir() or not base_root.is_dir():
        raise AddonValidationError("provide-selected-package-inputs")

    package = _load_json(addon_root / "package.json", "repair-addon-package-json")
    manifest = _load_json(addon_root / "RuntimeSupport" / "typesupport-manifest.json", "repair-typesupport-manifest")
    inventory = _load_json(addon_root / "RuntimeSupport" / "typesupport-inventory.json", "repair-typesupport-inventory")
    static_lock = _load_json(
        static_root / "RuntimeSupport" / "foxrun-ros2-interface-lock.json",
        "repair-static-interface-lock",
    )
    runtime_manifest = _load_json(
        base_root / "RuntimeSupport" / "runtime-manifest.json",
        "repair-base-runtime-manifest",
    )

    _reject_absolute_values(package)
    _reject_absolute_values(manifest)
    _reject_absolute_values(inventory)
    _validate_static_source(static_root, static_lock)
    _validate_package_metadata(package, distro, runtime_manifest)
    _validate_manifest(manifest, distro, static_lock, runtime_manifest)
    _validate_notices(addon_root)
    base_ros2_identity = _base_ros2_message_identity(base_root, request.base_ros2_message_identity)
    _validate_managed_payload(addon_root, manifest, base_ros2_identity)
    _validate_native_payload(addon_root, manifest, base_root)
    _validate_inventory(addon_root, inventory)

    requested_rmws = tuple(request.require_rmws)
    supported_rmws = tuple(manifest["supportedRmwImplementations"])
    if any(rmw not in supported_rmws for rmw in requested_rmws):
        raise AddonValidationError("repair-required-rmw-closure")
    if distro == "lyrical" and not {"rmw_fastrtps_cpp", "rmw_zenoh_cpp"}.issubset(supported_rmws):
        raise AddonValidationError("repair-lyrical-rmw-closure")
    _validate_rmw_closures(manifest, supported_rmws, addon_root, base_root)

    return AddonValidationResult(
        distro=distro,
        package_id=package["name"],
        interface_digest=manifest["source"]["interfaceDigest"],
        supported_rmws=supported_rmws,
    )


def _validate_static_source(static_root: Path, static_lock: Mapping[str, Any]) -> None:
    """Implement the internal validate static source step."""
    if static_lock.get("unityPackageId") != STATIC_INTERFACE_PACKAGE_ID:
        raise AddonValidationError("repair-static-interface-source")
    if static_lock.get("rosPackageName") != ROS_PACKAGE_NAME:
        raise AddonValidationError("repair-static-interface-source")
    expected = static_lock.get("interfaceDigest")
    if not isinstance(expected, str) or _SHA256.fullmatch(expected) is None:
        raise AddonValidationError("repair-static-interface-source")
    if compute_static_interface_digest(static_root) != expected:
        raise AddonValidationError("repair-static-interface-source")


def validate_addon_set(requests: Sequence[AddonValidationRequest]) -> tuple[AddonValidationResult, ...]:
    """Reject duplicate active add-ons before validating their individual data."""

    results = tuple(validate_addon(request) for request in requests)
    package_ids = [result.package_id for result in results]
    if len(package_ids) != len(set(package_ids)):
        raise AddonValidationError("remove-duplicate-custom-typesupport-addons")
    return results


def _normalize_distro(value: str) -> str:
    """Implement the internal normalize distro step."""
    normalized = (value or "").strip().lower()
    if normalized not in SUPPORTED_DISTROS:
        raise AddonValidationError("select-supported-ros-distro")
    return normalized


def _load_json(path: Path, remediation: str) -> dict[str, Any]:
    """Implement the internal load json step."""
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AddonValidationError(remediation) from exc
    if not isinstance(payload, dict):
        raise AddonValidationError(remediation)
    return payload


def _require_text(mapping: Mapping[str, Any], key: str, remediation: str) -> str:
    """Implement the internal require text step."""
    value = mapping.get(key)
    if not isinstance(value, str) or not value:
        raise AddonValidationError(remediation)
    return value


def _require_sha256(mapping: Mapping[str, Any], key: str, remediation: str) -> str:
    """Implement the internal require sha256 step."""
    value = _require_text(mapping, key, remediation)
    if _SHA256.fullmatch(value) is None:
        raise AddonValidationError(remediation)
    return value


def _validate_package_metadata(package: Mapping[str, Any], distro: str, runtime_manifest: Mapping[str, Any]) -> None:
    """Implement the internal validate package metadata step."""
    if _require_text(package, "name", "repair-addon-package-name") != addon_package_id(distro):
        raise AddonValidationError("repair-addon-package-name")
    if package.get("unity2foxgloveFoxRunCustomTypesupportAddOn") is not True:
        raise AddonValidationError("repair-addon-marker")
    license_name = _require_text(package, "license", "repair-addon-license")
    if license_name not in {"Apache-2.0"}:
        raise AddonValidationError("repair-addon-license")
    conflicts = package.get("unity2foxgloveConflicts")
    if not isinstance(conflicts, list):
        raise AddonValidationError("repair-addon-conflicts")
    expected_conflicts = {addon_package_id(item) for item in SUPPORTED_DISTROS if item != distro}
    if not expected_conflicts.issubset(set(conflicts)):
        raise AddonValidationError("repair-addon-conflicts")
    dependencies = package.get("dependencies")
    if not isinstance(dependencies, dict):
        raise AddonValidationError("repair-addon-dependencies")
    if not isinstance(dependencies.get(OPTIONAL_FACADE_PACKAGE_ID), str):
        raise AddonValidationError("repair-addon-dependencies")
    expected_runtime = base_runtime_package_id(distro)
    if runtime_manifest.get("packageName") != expected_runtime:
        raise AddonValidationError("repair-base-runtime-package")
    if dependencies.get(expected_runtime) != runtime_manifest.get("packageVersion"):
        raise AddonValidationError("repair-addon-dependencies")


def _validate_manifest(
    manifest: Mapping[str, Any],
    distro: str,
    static_lock: Mapping[str, Any],
    runtime_manifest: Mapping[str, Any],
) -> None:
    """Implement the internal validate manifest step."""
    if manifest.get("schemaVersion") != 1:
        raise AddonValidationError("repair-typesupport-manifest")
    if _require_text(manifest, "distro", "repair-addon-distro") != distro:
        raise AddonValidationError("repair-addon-distro")
    if _require_text(manifest, "platform", "repair-addon-platform") != "win64":
        raise AddonValidationError("repair-addon-platform")
    if _require_text(manifest, "architecture", "repair-addon-platform") != "x86_64":
        raise AddonValidationError("repair-addon-platform")

    source = manifest.get("source")
    if not isinstance(source, dict):
        raise AddonValidationError("repair-typesupport-source-identity")
    if _require_text(source, "upmPackageId", "repair-typesupport-source-identity") != STATIC_INTERFACE_PACKAGE_ID:
        raise AddonValidationError("repair-typesupport-source-identity")
    if _require_text(source, "rosPackageName", "repair-typesupport-source-identity") != ROS_PACKAGE_NAME:
        raise AddonValidationError("repair-typesupport-source-identity")
    if source.get("interfaceRevision") != static_lock.get("interfaceRevision"):
        raise AddonValidationError("repair-interface-revision")
    if source.get("generatorSchemaVersion") != 1:
        raise AddonValidationError("repair-generator-schema-version")
    interface_digest = _require_sha256(source, "interfaceDigest", "repair-interface-digest")
    if (
        static_lock.get("unityPackageId") != STATIC_INTERFACE_PACKAGE_ID
        or static_lock.get("rosPackageName") != ROS_PACKAGE_NAME
        or static_lock.get("interfaceDigest") != interface_digest
    ):
        raise AddonValidationError("repair-interface-digest")

    base_runtime = manifest.get("baseRuntime")
    if not isinstance(base_runtime, dict):
        raise AddonValidationError("repair-base-runtime-identity")
    if _require_text(base_runtime, "packageId", "repair-base-runtime-identity") != base_runtime_package_id(distro):
        raise AddonValidationError("repair-base-runtime-identity")
    if base_runtime.get("runtimeManifestVersion") != runtime_manifest.get("schemaVersion"):
        raise AddonValidationError("repair-base-runtime-identity")
    if _require_sha256(base_runtime, "runtimeManifestSha256", "repair-base-runtime-identity") != normalized_json_sha256(runtime_manifest):
        raise AddonValidationError("repair-base-runtime-identity")

    rmws = manifest.get("supportedRmwImplementations")
    if not isinstance(rmws, list) or not rmws or any(not isinstance(item, str) or not item for item in rmws):
        raise AddonValidationError("repair-supported-rmw-list")
    if len(rmws) != len(set(rmws)):
        raise AddonValidationError("repair-supported-rmw-list")


def _validate_notices(addon_root: Path) -> None:
    """Implement the internal validate notices step."""
    for relative in REQUIRED_LICENSE_FILES:
        path = addon_root / relative
        if not path.is_file() or not path.read_text(encoding="utf-8", errors="replace").strip():
            raise AddonValidationError("repair-addon-license-notices")


def _base_ros2_message_identity(
    base_runtime_root: Path,
    supplied: Mapping[str, str] | None,
) -> Mapping[str, str]:
    """Implement the internal base ros2 message identity step."""
    if supplied is not None:
        return _validate_managed_identity(supplied, "repair-ros2cs-common-identity")

    assembly_path = base_runtime_root / "Runtime" / "Ros2ForUnity" / "Plugins" / "ros2cs_common.dll"
    if not assembly_path.is_file():
        raise AddonValidationError("repair-ros2cs-common-identity")
    try:
        identity = _read_windows_managed_identity(assembly_path)
    except (OSError, subprocess.SubprocessError, ValueError) as exc:
        raise AddonValidationError("repair-ros2cs-common-identity") from exc
    identity = dict(identity)
    identity["sha256"] = file_sha256(assembly_path)
    return _validate_managed_identity(identity, "repair-ros2cs-common-identity")


def _read_windows_managed_identity(assembly_path: Path) -> Mapping[str, str]:
    """Read public CLR identity without loading a ROS runtime or mutating PATH.

    The add-ons are Win64-only.  ``AssemblyName.GetAssemblyName`` and
    ``LoadFile`` expose PE metadata only here; they do not invoke ros2cs or any
    ROS static initializer.  The command returns only public identity facts,
    never a path or an environment value, so callers can surface bounded errors.
    """

    powershell = Path(os.environ.get("SystemRoot", r"C:\\Windows")) / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe"
    if not powershell.is_file():
        raise OSError("powershell-unavailable")
    script = (
        "$path=$env:UNITY2FOXGLOVE_MANAGED_IDENTITY_PATH;"
        "$assembly=[System.Reflection.Assembly]::LoadFile([System.IO.Path]::GetFullPath($path));"
        "$name=$assembly.GetName();"
        "$token=([System.BitConverter]::ToString($name.GetPublicKeyToken())).Replace('-','').ToLowerInvariant();"
        "[Console]::Out.Write(([PSCustomObject]@{"
        "assemblyName=$name.Name;version=$name.Version.ToString();publicKeyToken=$token;"
        "mvid=$assembly.ManifestModule.ModuleVersionId.ToString('D')"
        "}|ConvertTo-Json -Compress))"
    )
    environment = dict(os.environ)
    environment["UNITY2FOXGLOVE_MANAGED_IDENTITY_PATH"] = str(assembly_path.resolve())
    result = subprocess.run(
        (str(powershell), "-NoProfile", "-NonInteractive", "-Command", script),
        shell=False,
        capture_output=True,
        text=True,
        errors="replace",
        env=environment,
        check=False,
    )
    if result.returncode != 0:
        raise ValueError("managed-identity-probe-failed")
    payload = json.loads(result.stdout)
    if not isinstance(payload, dict):
        raise ValueError("managed-identity-probe-invalid")
    return {key: value for key, value in payload.items() if isinstance(value, str)}


def _validate_managed_identity(
    identity: Mapping[str, Any],
    remediation: str,
) -> Mapping[str, str]:
    """Implement the internal validate managed identity step."""
    assembly_name = _require_text(identity, "assemblyName", remediation)
    version = _require_text(identity, "version", remediation)
    public_key_token = identity.get("publicKeyToken")
    mvid = _require_text(identity, "mvid", remediation)
    sha256 = _require_text(identity, "sha256", remediation)
    if (
        assembly_name != "ros2cs_common"
        or re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version) is None
        or not isinstance(public_key_token, str)
        or (public_key_token and re.fullmatch(r"[0-9a-f]{16}", public_key_token) is None)
        or _MVID.fullmatch(mvid) is None
        or _SHA256.fullmatch(sha256) is None
    ):
        raise AddonValidationError(remediation)
    return {
        "assemblyName": assembly_name,
        "version": version,
        "publicKeyToken": public_key_token,
        "mvid": mvid,
        "sha256": sha256,
    }


def _validate_managed_payload(
    addon_root: Path,
    manifest: Mapping[str, Any],
    base_ros2_identity: Mapping[str, str],
) -> None:
    """Implement the internal validate managed payload step."""
    managed = manifest.get("managed")
    if not isinstance(managed, dict):
        raise AddonValidationError("repair-managed-typesupport-payload")
    assembly = managed.get("assembly")
    if not isinstance(assembly, dict):
        raise AddonValidationError("repair-managed-typesupport-payload")
    assembly_path = _validate_payload_entry(
        addon_root,
        assembly,
        expected_name="unity2foxglove_foxrun_interfaces_v1_assembly",
        remediation="repair-managed-typesupport-payload",
    )
    if not assembly_path.name.endswith(".dll"):
        raise AddonValidationError("repair-managed-typesupport-payload")
    type_map = managed.get("typeMap")
    if not isinstance(type_map, list) or not type_map:
        raise AddonValidationError("repair-managed-type-map")
    expected_envelope = ROS_PACKAGE_NAME + ".msg.Phase181State48D288ED82F1Envelope"
    if not any(
        isinstance(item, dict)
        and item.get("canonicalRosType") == ROS_PACKAGE_NAME + "/msg/Phase181State48D288ED82F1Envelope"
        and item.get("managedType") == expected_envelope
        for item in type_map
    ):
        raise AddonValidationError("repair-managed-type-map")
    ros2_message = managed.get("ros2Message")
    if not isinstance(ros2_message, dict):
        raise AddonValidationError("repair-ros2cs-common-identity")
    declared_identity = _validate_managed_identity(ros2_message, "repair-ros2cs-common-identity")
    if declared_identity != dict(base_ros2_identity):
        raise AddonValidationError("repair-ros2cs-common-identity")
    importer = managed.get("pluginImporter")
    if not isinstance(importer, dict):
        raise AddonValidationError("repair-managed-plugin-importer")
    if tuple(importer.get("includePlatforms", ())) != EXPECTED_PLUGIN_PLATFORMS:
        raise AddonValidationError("repair-managed-plugin-importer")
    meta_path = _safe_relative_path(_require_text(importer, "metaPath", "repair-managed-plugin-importer"))
    meta = addon_root / meta_path
    if not meta.is_file():
        raise AddonValidationError("repair-managed-plugin-importer")
    text = meta.read_text(encoding="utf-8", errors="replace")
    if not _has_restricted_windows_plugin_importer(text):
        raise AddonValidationError("repair-managed-plugin-importer")


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

    # Unity's older list-of-pairs serialization keeps ``first`` and
    # ``second`` at the same indentation as the platform key.  Retain its
    # explicit checks rather than pretending it has the Unity 6000 mapping
    # structure parsed below.
    if (
        "Any:" in text
        and "enabled: 0" in text
        and "Editor: Editor" in text
        and "Standalone: Windows" in text
        and "enabled: 1" in text
        and "CPU: x86_64" in text
    ):
        return True

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


def _validate_payload_entry(
    addon_root: Path,
    entry: Mapping[str, Any],
    *,
    expected_name: str | None,
    remediation: str,
) -> Path:
    """Implement the internal validate payload entry step."""
    relative = _safe_relative_path(_require_text(entry, "path", remediation))
    path = addon_root / relative
    if not path.is_file() or file_sha256(path) != _require_sha256(entry, "sha256", remediation):
        raise AddonValidationError(remediation)
    if expected_name is not None and entry.get("name") != expected_name:
        raise AddonValidationError(remediation)
    return path


def _validate_inventory(addon_root: Path, inventory: Mapping[str, Any]) -> None:
    """Implement the internal validate inventory step."""
    if inventory.get("schemaVersion") != 1:
        raise AddonValidationError("repair-typesupport-inventory")
    entries = inventory.get("entries")
    if not isinstance(entries, list):
        raise AddonValidationError("repair-typesupport-inventory")
    seen: set[str] = set()
    inventory_paths: list[str] = []
    allowed_roles = {"managed", "native", "catalog", "importer", "metadata", "notice"}
    allowed_classifications = {"direct", "transitive", "metadata"}
    for entry in entries:
        if not isinstance(entry, dict):
            raise AddonValidationError("repair-typesupport-inventory")
        relative = _safe_relative_path(_require_text(entry, "path", "repair-typesupport-inventory")).as_posix()
        if relative in seen:
            raise AddonValidationError("repair-typesupport-inventory")
        seen.add(relative)
        inventory_paths.append(relative)
        path = addon_root / relative
        if not path.is_file() or path.stat().st_size != entry.get("byteLength"):
            raise AddonValidationError("repair-typesupport-inventory")
        if file_sha256(path) != _require_sha256(entry, "sha256", "repair-typesupport-inventory"):
            raise AddonValidationError("repair-typesupport-inventory")
        if entry.get("role") not in allowed_roles or entry.get("classification") not in allowed_classifications:
            raise AddonValidationError("repair-typesupport-inventory")
    if inventory_paths != sorted(inventory_paths, key=str.lower):
        raise AddonValidationError("repair-typesupport-inventory")

    excluded = {"RuntimeSupport/typesupport-inventory.json"}
    recorded_importers = {
        _safe_relative_path(_require_text(entry, "path", "repair-typesupport-inventory")).as_posix()
        for entry in entries
        if isinstance(entry, dict) and entry.get("role") == "importer"
    }
    actual = {
        path.relative_to(addon_root).as_posix()
        for path in addon_root.rglob("*")
        if path.is_file()
        and path.relative_to(addon_root).as_posix() not in excluded
        and (
            not path.name.endswith(".meta")
            or path.relative_to(addon_root).as_posix() in recorded_importers
        )
    }
    if actual != seen:
        raise AddonValidationError("repair-typesupport-inventory")


def _safe_relative_path(value: str) -> PurePosixPath:
    """Implement the internal safe relative path step."""
    if _ABSOLUTE_PATH.match(value) or "\\" in value:
        raise AddonValidationError("remove-machine-private-path")
    path = PurePosixPath(value)
    if not value or path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts):
        raise AddonValidationError("remove-machine-private-path")
    return path


def _reject_absolute_values(value: object) -> None:
    """Implement the internal reject absolute values step."""
    if isinstance(value, str):
        if _ABSOLUTE_PATH.match(value):
            raise AddonValidationError("remove-machine-private-path")
        return
    if isinstance(value, Mapping):
        for item in value.values():
            _reject_absolute_values(item)
        return
    if isinstance(value, Iterable) and not isinstance(value, (bytes, bytearray)):
        for item in value:
            _reject_absolute_values(item)
