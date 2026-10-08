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

import json
import os
import re
import subprocess
from pathlib import Path
from typing import Any, Mapping

from .identity import file_sha256
from .inventory import _require_text, _safe_relative_path, _validate_payload_entry
from .models import (
    AddonValidationError,
    EXPECTED_PLUGIN_PLATFORMS,
    ROS_PACKAGE_NAME,
    _MVID,
    _SHA256,
)
from .native import _has_restricted_windows_plugin_importer

def _base_ros2_message_identity(
    base_runtime_root: Path,
    supplied: Mapping[str, str] | None,
    *,
    allow_hash_only: bool = False,
) -> Mapping[str, str]:
    """Implement the internal base ros2 message identity step."""
    if supplied is not None:
        return _validate_managed_identity(supplied, "repair-ros2cs-common-identity")

    assembly_path = base_runtime_root / "Runtime" / "Ros2ForUnity" / "Plugins" / "ros2cs_common.dll"
    if not assembly_path.is_file():
        raise AddonValidationError("repair-ros2cs-common-identity")
    try:
        assembly_sha256 = file_sha256(assembly_path)
    except OSError as exc:
        raise AddonValidationError("repair-ros2cs-common-identity") from exc
    if not _is_windows_host():
        if allow_hash_only:
            # The artifact is Win64-only, while the package-check workflow is
            # intentionally Linux-hosted. The SHA-256 still proves that the
            # declared identity refers to the exact selected base DLL. The
            # Windows analyzer workflow runs this same validator with the CLR
            # metadata probe enabled before an add-on can merge.
            return {"sha256": assembly_sha256}
        raise AddonValidationError("repair-ros2cs-common-identity")
    try:
        identity = _read_windows_managed_identity(assembly_path)
    except (OSError, subprocess.SubprocessError, ValueError) as exc:
        raise AddonValidationError("repair-ros2cs-common-identity") from exc
    identity = dict(identity)
    identity["sha256"] = assembly_sha256
    return _validate_managed_identity(identity, "repair-ros2cs-common-identity")


def _is_windows_host() -> bool:
    """Return whether the host can perform the Win64 CLR metadata probe."""

    return os.name == "nt"


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
    expected_assembly_path = (
        addon_root
        / "Runtime"
        / "Ros2ForUnity"
        / "Plugins"
        / "unity2foxglove_foxrun_interfaces_v1_assembly.dll"
    )
    if assembly_path != expected_assembly_path:
        raise AddonValidationError("repair-managed-typesupport-payload")
    if not _is_clr_pe_payload(assembly_path):
        raise AddonValidationError("repair-managed-typesupport-payload")
    if not _contains_expected_managed_type(assembly_path, "Phase181State48D288ED82F1Envelope"):
        raise AddonValidationError("repair-managed-type-map")
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
    if set(base_ros2_identity) == {"sha256"}:
        if declared_identity["sha256"] != base_ros2_identity["sha256"]:
            raise AddonValidationError("repair-ros2cs-common-identity")
    elif declared_identity != dict(base_ros2_identity):
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


def _is_clr_pe_payload(path: Path) -> bool:
    """Require the managed add-on payload to identify as a Windows CLR PE."""
    try:
        data = path.read_bytes()
        if len(data) < 0x40 or data[:2] != b"MZ":
            return False
        pe_offset = int.from_bytes(data[0x3C:0x40], "little")
        if pe_offset < 0x40 or pe_offset + 8 > len(data) or data[pe_offset:pe_offset + 4] != b"PE\0\0":
            return False
        # Managed Unity plug-ins may be AnyCPU (I386) or AMD64; reject other
        # machine types before trusting the declared CLR identity.
        if data[pe_offset + 4:pe_offset + 6] not in {b"L\x01", b"d\x86"}:
            return False
        return b"BSJB" in data
    except OSError:
        return False


def _contains_expected_managed_type(path: Path, type_name: str) -> bool:
    """Require the declared envelope type name to be present in CLR metadata."""
    try:
        data = path.read_bytes()
        return type_name.encode("ascii") in data or type_name.encode("utf-16le") in data
    except (OSError, UnicodeEncodeError):
        return False

__all__ = [name for name in globals() if not name.startswith("__")]
