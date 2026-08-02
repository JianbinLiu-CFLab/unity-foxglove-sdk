#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Create and remove one owned Bridge-only Unity acceptance project."""

from __future__ import annotations

import dataclasses
import hashlib
import json
import os
import pathlib
import shutil
import sys
import tempfile
import time
from collections.abc import Mapping

try:
    from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol
except ImportError:  # Imported by the direct acceptance entry point.
    import phase186_bridge_acceptance_protocol as protocol


OWNERSHIP_MARKER = ".phase186-owned-project.json"
PROJECT_SCHEMA_VERSION = 1
MAX_WINDOWS_UNITY_LMDB_PATH = 240
_UNITY_LMDB_RELATIVE_PATH = pathlib.Path("Library") / "Search" / ("x" * 80)

_ASSET_PATHS = (
    "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186ManualInteractionState.cs",
    "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186ManualInteractionState.cs.meta",
    "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186Ros2BridgeAcceptance.cs",
    "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186Ros2BridgeAcceptance.cs.meta",
    "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase186BatchModeRos2BridgeProbe.cs",
    "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase186BatchModeRos2BridgeProbe.cs.meta",
    "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase186Ros2BridgeAcceptanceBuilder.cs",
    "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase186Ros2BridgeAcceptanceBuilder.cs.meta",
    "Unity2Foxglove/Assets/Scenes/ManualAcceptance/Phase186Ros2BridgeAcceptance.unity",
    "Unity2Foxglove/Assets/Scenes/ManualAcceptance/Phase186Ros2BridgeAcceptance.unity.meta",
)

_PHASE181_DTO_SOURCE = """// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
// TRANSIENT: exact Phase181 DTO identity for Bridge-only acceptance.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Tests.FoxRun.Fixtures
{
    [Serializable]
    public enum Phase181StateKind : ushort
    {
        Unknown = 0,
        Active = 1,
    }

    [Serializable]
    public sealed class Phase181NestedState
    {
        public bool Enabled { get; set; }
        public string Label { get; set; }
    }

    [Serializable]
    public sealed class Phase181State
    {
        public int Count { get; set; }
        public Phase181StateKind Kind { get; set; }
        public string Message { get; set; }
        public byte[] Bytes { get; set; }
        public List<long> Values { get; set; }
        public Phase181NestedState Nested { get; set; }
        public int? OptionalCount { get; set; }
        public string OptionalText { get; set; }
    }
}
"""


class BridgeOnlyProjectFailure(RuntimeError):
    """Stable owned-project staging or cleanup failure."""


@dataclasses.dataclass(frozen=True)
class OwnedBridgeOnlyProject:
    path: pathlib.Path
    marker: pathlib.Path
    owner_token: str
    manifest_sha256: str
    staged_asset_sha256: Mapping[str, str]


def _sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _write_atomic(path: pathlib.Path, value: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary: pathlib.Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=path.parent,
            prefix=path.name + ".",
            suffix=".tmp",
            delete=False,
        ) as stream:
            stream.write(value)
            temporary = pathlib.Path(stream.name)
        os.replace(temporary, path)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()


def _owned_project_path(
    repository: pathlib.Path,
    owner_token: str,
) -> pathlib.Path:
    root = pathlib.Path(repository).resolve()
    try:
        target = protocol.owned_unity_project_path(root, owner_token)
    except protocol.ProtocolFailure as exc:
        raise BridgeOnlyProjectFailure("owned Unity project token is malformed") from exc
    if (
        sys.platform == "win32"
        and len(str(target / _UNITY_LMDB_RELATIVE_PATH))
        > MAX_WINDOWS_UNITY_LMDB_PATH
    ):
        raise BridgeOnlyProjectFailure(
            "owned Unity project exceeds the Windows path budget"
        )
    return target


def _bridge_only_manifest(repository: pathlib.Path) -> bytes:
    root = pathlib.Path(repository).resolve()
    source = root / "Unity2Foxglove" / "Packages" / "manifest.json"
    try:
        value = json.loads(source.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise BridgeOnlyProjectFailure("repository Unity manifest is unavailable") from exc
    dependencies = value.get("dependencies")
    if not isinstance(dependencies, dict):
        raise BridgeOnlyProjectFailure("repository Unity dependencies are malformed")
    filtered = {
        key: item
        for key, item in dependencies.items()
        if key.startswith("com.unity.modules.")
    }
    for package_id, relative in (
        ("dev.unity2foxglove.sdk", "Packages/dev.unity2foxglove.sdk"),
        (
            "dev.unity2foxglove.ros2bridge",
            "Packages/dev.unity2foxglove.ros2bridge",
        ),
    ):
        package = (root / relative).resolve(strict=True)
        filtered[package_id] = "file:" + package.as_posix()
    value["dependencies"] = dict(sorted(filtered.items()))
    return (
        json.dumps(value, indent=2, sort_keys=True, ensure_ascii=True) + "\n"
    ).encode("utf-8")


def validate_bridge_only_manifest(path: pathlib.Path) -> dict[str, object]:
    manifest = pathlib.Path(path) / "Packages" / "manifest.json"
    try:
        value = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise BridgeOnlyProjectFailure("Bridge-only manifest is unavailable") from exc
    dependencies = value.get("dependencies")
    if not isinstance(dependencies, dict):
        raise BridgeOnlyProjectFailure("Bridge-only dependencies are malformed")
    product = sorted(
        key for key in dependencies if key.startswith("dev.unity2foxglove.")
    )
    expected = [
        "dev.unity2foxglove.ros2bridge",
        "dev.unity2foxglove.sdk",
    ]
    if product != expected:
        raise BridgeOnlyProjectFailure(
            "Bridge-only Unity project contains unexpected product packages"
        )
    unexpected = sorted(
        key
        for key in dependencies
        if key not in expected and not key.startswith("com.unity.modules.")
    )
    if unexpected:
        raise BridgeOnlyProjectFailure(
            "Bridge-only Unity project contains unrelated feature packages"
        )
    for key in expected:
        value_text = dependencies.get(key)
        if not isinstance(value_text, str) or not value_text.startswith("file:"):
            raise BridgeOnlyProjectFailure(
                "Bridge-only product package is not an exact local source"
            )
    return {
        "composition": "sdk-bridge",
        "productPackages": product,
        "manifest": str(manifest.resolve()),
        "manifestSha256": _sha256_bytes(manifest.read_bytes()),
    }


def create_bridge_only_project(
    repository: pathlib.Path,
    owner_token: str,
) -> OwnedBridgeOnlyProject:
    root = pathlib.Path(repository).resolve()
    target = _owned_project_path(root, owner_token)
    if target.exists():
        raise BridgeOnlyProjectFailure("owned Unity project already exists")
    target.mkdir(parents=True)
    try:
        shutil.copytree(
            root / "Unity2Foxglove" / "ProjectSettings",
            target / "ProjectSettings",
        )
        manifest = _bridge_only_manifest(root)
        _write_atomic(target / "Packages" / "manifest.json", manifest)
        staged: dict[str, str] = {}
        for relative_text in _ASSET_PATHS:
            source = root / relative_text
            relative = source.relative_to(root / "Unity2Foxglove")
            destination = target / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
            staged[relative.as_posix()] = _sha256_bytes(destination.read_bytes())
        dto = target / "Assets" / "Scripts" / "ManualAcceptance" / "Phase181AcceptanceDto.cs"
        dto_bytes = _PHASE181_DTO_SOURCE.encode("utf-8")
        _write_atomic(dto, dto_bytes)
        dto_meta = pathlib.Path(str(dto) + ".meta")
        guid = _sha256_bytes(dto_bytes)[:32]
        _write_atomic(
            dto_meta,
            ("fileFormatVersion: 2\nguid: " + guid + "\n").encode("ascii"),
        )
        staged[dto.relative_to(target).as_posix()] = _sha256_bytes(dto_bytes)
        staged[dto_meta.relative_to(target).as_posix()] = _sha256_bytes(
            dto_meta.read_bytes()
        )
        evidence = validate_bridge_only_manifest(target)
        marker = target / OWNERSHIP_MARKER
        marker_value = {
            "schemaVersion": PROJECT_SCHEMA_VERSION,
            "ownerToken": owner_token,
            "projectPath": str(target.resolve()),
            "manifestSha256": evidence["manifestSha256"],
            "stagedAssetSha256": dict(sorted(staged.items())),
        }
        _write_atomic(
            marker,
            (
                json.dumps(marker_value, indent=2, sort_keys=True) + "\n"
            ).encode("utf-8"),
        )
        return OwnedBridgeOnlyProject(
            target,
            marker,
            owner_token,
            str(evidence["manifestSha256"]),
            dict(sorted(staged.items())),
        )
    except BaseException:
        if target.exists():
            shutil.rmtree(target, ignore_errors=True)
        raise


def cleanup_bridge_only_project(project: OwnedBridgeOnlyProject) -> None:
    target = pathlib.Path(project.path).resolve()
    marker = pathlib.Path(project.marker).resolve()
    if marker != target / OWNERSHIP_MARKER or not marker.is_file():
        raise BridgeOnlyProjectFailure("owned Unity project marker is absent")
    try:
        value = json.loads(marker.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise BridgeOnlyProjectFailure("owned Unity project marker is invalid") from exc
    expected = {
        "schemaVersion": PROJECT_SCHEMA_VERSION,
        "ownerToken": project.owner_token,
        "projectPath": str(target),
        "manifestSha256": project.manifest_sha256,
        "stagedAssetSha256": dict(project.staged_asset_sha256),
    }
    if value != expected:
        raise BridgeOnlyProjectFailure("owned Unity project marker differs")
    deadline = time.monotonic() + 20.0
    while True:
        try:
            shutil.rmtree(target)
            return
        except OSError as exc:
            if time.monotonic() >= deadline:
                raise BridgeOnlyProjectFailure(
                    "owned Unity project could not be removed"
                ) from exc
            time.sleep(0.1)
