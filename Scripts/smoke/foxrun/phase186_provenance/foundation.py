#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Fail-closed Phase186 reference provenance and SDK ROS inventory validation."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import argparse
import fnmatch
import hashlib
import json
import math
import os
import pathlib
import posixpath
import re
import stat
import subprocess
import sys
import unicodedata
from collections.abc import Iterable, Mapping, Sequence
_REFERENCE_REMOTE = "https://github.com/Unity-Technologies/ROS-TCP-Connector.git"
_REFERENCE_REVISION = "c27f00c6cf750d2d0564349b3039d19aa3925e7c"
_REFERENCE_TREE = "183ee0c7888b39e7278b24992e288a6c7555f39d"
_REFERENCE_DATE = "2022-02-02T09:25:06-08:00"
_REFERENCE_SUBJECT = "Release 0.7.0"
_REFERENCE_LICENSE_SHA256 = (
    "2e21a5b872a2cdfec6f89db4c93be2867e05b481e77b9c4a6af1caf813129fb0"
)
_REFERENCE_FILES = (
    "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/ROSConnection.cs",
    "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/OutgoingMessageSender.cs",
    "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/MessagePool.cs",
    "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/TopicMessageSender.cs",
    "com.unity.robotics.ros-tcp-connector/Editor/MessageGeneration/MessageParser.cs",
)
_REFERENCE_IDEAS = (
    "one component owns a connection lifecycle",
    "outbound work crosses a bounded sender boundary",
    "payload ownership can be pooled explicitly",
    "topic routing has a stable per-topic sender identity",
    "code generation separates parsing from emitted artifacts",
)
_CLASSIFICATIONS = {"original", "inspired", "materially_copied"}
_OVERLAP_LINE_COUNT = 4
_OVERLAP_MIN_CHARS = 120
_SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
_FULL_OBJECT_ID_PATTERN = re.compile(r"^[0-9a-f]{40}$")
_LEDGER_RELATIVE = (
    "Tools/ros2_bridge/unity2foxglove_ros2_bridge/PROVENANCE.json"
)
_INVENTORY_RELATIVE = (
    "Packages/dev.unity2foxglove.sdk/Tests/Unit/Phase186/Fixtures/"
    "pre_move_sdk_ros_inventory.json"
)
_FIXTURE_RELATIVE = (
    "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/"
    "u2r2_protocol_vectors.json"
)
_PROTOCOL_DOCS = (
    "Packages/dev.unity2foxglove.ros2bridge/Documentation~/en/U2R2_PROTOCOL.md",
    "Tools/ros2_bridge/unity2foxglove_ros2_bridge/U2R2_PROTOCOL.md",
)
_PROTOCOL_DOC_LEDGER_LINKS = {
    _PROTOCOL_DOCS[0]: (
        "../../../../Tools/ros2_bridge/unity2foxglove_ros2_bridge/"
        "PROVENANCE.json"
    ),
    _PROTOCOL_DOCS[1]: "PROVENANCE.json",
}
_REQUIRED_RECORDED_AUTHORITIES = (
    _FIXTURE_RELATIVE,
    "Scripts/smoke/foxrun/phase186_provenance.py",
    "Scripts/smoke/foxrun/regression_checks/test_phase186_provenance.py",
    _INVENTORY_RELATIVE,
    *_PROTOCOL_DOCS,
)
_REQUIRED_UNRECORDED_AUTHORITIES = (_LEDGER_RELATIVE,)
_PROTOCOL_SOURCE_ROOTS = (
    (
        "Packages/dev.unity2foxglove.ros2bridge/Runtime/Protocol",
        "*.cs",
    ),
    (
        "Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Protocol",
        "*.cs",
    ),
    (
        "Tools/ros2_bridge/unity2foxglove_ros2_bridge/include/"
        "unity2foxglove_ros2_bridge",
        "u2r2_protocol*.hpp",
    ),
    (
        "Tools/ros2_bridge/unity2foxglove_ros2_bridge/src",
        "u2r2_protocol*.cpp",
    ),
    (
        "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test",
        "test_u2r2_protocol*.cpp",
    ),
)
_PHASE186B_SOURCE_COMMITS = (
    (
        "c66a694a1e2a1a229598837a5d593d71e93b2c86",
        "test(186b): define cross-language U2R2 v2 authority",
        6,
    ),
    (
        "3f1b47e973713bf77fe149d04472f4a8ecbe8b71",
        "fix(186b): enforce U2R2 replay and ordering bounds",
        8,
    ),
)
_INVENTORY_CAPTURE_COMMIT = "b5388cb4051750939776d217208f467f37aa86c6"
_INVENTORY_CAPTURE_TREE = "4ef65eace86d0163c7ec0b75b21975ccfca95751"
_INVENTORY_PATH_COUNT = 156
_INVENTORY_PATH_DIGEST = (
    "72aa3286e017673725c8b62b25cf02acd6dc7f65466db13669623753da815517"
)
_INVENTORY_PURPOSE = (
    "Exact compact inventory of tracked SDK production assets that Phase186A "
    "must move, split, or delete."
)
_INVENTORY_TOP_LEVEL_KEYS = {
    "schemaVersion",
    "capturedFromHead",
    "capturedTree",
    "purpose",
    "scopes",
    "totalPathCount",
    "totalPathDigestSha256",
}
_INVENTORY_SCOPE_AUTHORITY = (
    {
        "id": "bridge_runtime_tree",
        "action": "move_to_bridge",
        "prefixes": [
            "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge",
        ],
        "exactPaths": [
            "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge.meta",
        ],
        "pathCount": 36,
        "pathDigestSha256": (
            "aeac5eb30d61304fea11edde4593a0220afcb67aeec4033309dfb15968986b0d"
        ),
    },
    {
        "id": "ros2msg_runtime_tree",
        "action": "move_to_bridge",
        "prefixes": [
            "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg",
        ],
        "exactPaths": [
            "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg.meta",
        ],
        "pathCount": 57,
        "pathDigestSha256": (
            "4a996525ec25e53810c561ca0817a1e8fbba17c304293a1cbbb59e545a6c6602"
        ),
    },
    {
        "id": "bridge_editor_tree",
        "action": "move_to_bridge",
        "prefixes": [
            "Packages/dev.unity2foxglove.sdk/Editor/Ros2Bridge",
        ],
        "exactPaths": [
            "Packages/dev.unity2foxglove.sdk/Editor/Ros2Bridge.meta",
            (
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/"
                "FoxgloveManagerEditor.Ros2Bridge.cs"
            ),
            (
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/"
                "FoxgloveManagerEditor.Ros2Bridge.cs.meta"
            ),
        ],
        "pathCount": 7,
        "pathDigestSha256": (
            "eec005ad89e40d64339923536ef78fae800a00549baacc8eecd67f94750f583e"
        ),
    },
    {
        "id": "bridge_manager_runtime",
        "action": "delete_from_sdk",
        "exactPaths": [
            (
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/"
                "FoxgloveManager.Publishing.Ros2Bridge.cs"
            ),
            (
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/"
                "FoxgloveManager.Publishing.Ros2Bridge.cs.meta"
            ),
        ],
        "pathCount": 2,
        "pathDigestSha256": (
            "071e81b2c33c4645602ff0ce22710b3d824dd91f68610ae7d780f86eb25b8b34"
        ),
    },
    {
        "id": "provider_generation_split",
        "action": "split_between_providers",
        "globs": [
            "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/*Ros2*.cs*",
            (
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/"
                "FoxgloveSourceEmitter/*Ros2*.cs*"
            ),
            (
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/"
                "FoxRunDescriptor/*Ros2*.cs*"
            ),
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/*Ros2*.cs*",
            (
                "Packages/dev.unity2foxglove.sdk/Editor/"
                "SourceGenerators/src/*Ros2*.cs*"
            ),
        ],
        "pathCount": 42,
        "pathDigestSha256": (
            "525303cf340ae2f37a24c7e7aa43f232073fe23ded5e28bb2b2f8f9310bccaac"
        ),
    },
    {
        "id": "r2fu_runtime_edge",
        "action": "move_to_r2fu",
        "globs": [
            (
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/"
                "FoxRun/FoxRunRos2*.cs*"
            ),
            (
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/"
                "Manager/Ros2NativeOutputPolicy.cs*"
            ),
        ],
        "pathCount": 10,
        "pathDigestSha256": (
            "8bd0cf74c43d7a45af54e85331a80c2709f3f5e8d41af82dc4743ed062422e9e"
        ),
    },
    {
        "id": "orphan_ros_proto_markers",
        "action": "delete_from_sdk",
        "exactPaths": [
            (
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/"
                "Ros2Bridge.meta"
            ),
            (
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/"
                "Ros2Msg.meta"
            ),
        ],
        "pathCount": 2,
        "pathDigestSha256": (
            "1655b4e819a5396194326d7ab7b61842b0f94242c85b279b228eab8a15ba8141"
        ),
    },
)
_V1_CAPTURE_COMMIT = "3fe4fc32460762963175b2dd9f9558ca964fd81d"
_V1_TOP_LEVEL_KEYS = (
    "fixtureVersion",
    "protocol",
    "limits",
    "health",
    "preparePublisher",
    "publish",
    "negativeVectors",
)
_V1_CANONICAL_SHA256 = (
    "de79b8f992261a80b838589ce8660ce6a6a49b824c5e98015d1e63e958b55586"
)
_ERROR_CODES = (
    "busy",
    "unsupported_protocol",
    "missing_capability",
    "invalid_frame",
    "invalid_contract",
    "contract_identity_mismatch",
    "publisher_unavailable",
    "invalid_request_id",
    "request_id_exhausted",
    "counter_exhausted",
    "request_id_conflict",
    "response_mismatch",
    "request_in_flight",
    "stale_request",
    "capacity_exceeded",
    "contract_not_ready",
    "unknown_contract",
    "contract_sequence_fault",
    "contract_sequence_exhausted",
    "invalid_configuration",
    "dialect_downgrade",
    "peer_closed",
    "timeout",
)
_LIMIT_NAMES = (
    "maxConnections",
    "maxDataSessions",
    "maxProbes",
    "maxContracts",
    "maxOutstandingRequests",
    "maxReplayEntries",
    "maxReplayBytes",
    "maxTombstones",
    "fixedFrameBytes",
    "maxHeaderBytes",
    "maxPayloadBytes",
    "maxTransientBytes",
    "maxInFlightBytes",
    "maxQueuedBytes",
    "maxTotalQueueDepth",
    "maxPerContractQueueDepth",
    "maxPerContractQueueBytes",
    "reservedControlQueueDepth",
    "reservedControlQueueBytes",
    "controlBurstLimit",
    "handshakeTimeoutMs",
    "partialFrameTimeoutMs",
    "readTimeoutMs",
    "writeTimeoutMs",
    "joinTimeoutMs",
    "shutdownTimeoutMs",
    "maxJsonDepth",
)
def _normal_path(value: str) -> str:
    """Normalize path separators for portable inventory comparisons."""

    return value.replace("\\", "/").strip("/")
def _canonical_relative_path(value: object, *, label: str) -> str:
    """Return one portable path identity or reject every alias/escape form."""

    if not isinstance(value, str) or not value:
        raise ValueError(f"{label} must be a non-empty string")
    if "\0" in value:
        raise ValueError(f"{label} contains a NUL byte")
    if (
        value.startswith(("/", "\\"))
        or re.match(r"^[A-Za-z]:", value)
        or value.startswith("//")
    ):
        raise ValueError(f"{label} must not be absolute: {value!r}")
    if "\\" in value:
        raise ValueError(f"{label} uses a non-canonical path separator: {value!r}")
    segments = value.split("/")
    if ".." in segments:
        raise ValueError(f"{label} contains parent traversal: {value!r}")
    if "." in segments or "" in segments:
        raise ValueError(f"{label} contains a canonical alias: {value!r}")
    normalized_unicode = unicodedata.normalize("NFC", value)
    normalized_path = posixpath.normpath(normalized_unicode)
    if normalized_unicode != value or normalized_path != value:
        raise ValueError(f"{label} contains a canonical alias: {value!r}")
    return value
def _canonical_path_list(
    values: object,
    *,
    label: str,
    require_nonempty: bool = False,
) -> tuple[list[str], list[str]]:
    """Canonicalize a JSON path list while collecting validation errors."""

    errors: list[str] = []
    if not isinstance(values, list):
        return [], [f"{label} must be an array"]
    if require_nonempty and not values:
        errors.append(f"{label} must be non-empty")
    paths: list[str] = []
    portable_identities: dict[str, str] = {}
    for index, value in enumerate(values):
        try:
            path = _canonical_relative_path(value, label=f"{label}[{index}]")
        except ValueError as exc:
            errors.append(str(exc))
            continue
        identity = unicodedata.normalize("NFC", path).casefold()
        previous = portable_identities.get(identity)
        if previous is not None:
            errors.append(
                f"{label} has a case-insensitive duplicate: {previous!r} and {path!r}"
            )
            continue
        portable_identities[identity] = path
        paths.append(path)
    return paths, errors
def _sha256_bytes(value: bytes) -> str:
    """Return the lowercase SHA-256 digest of a byte sequence."""

    return hashlib.sha256(value).hexdigest()
def _canonical_source_bytes(value: bytes) -> bytes:
    """Normalize checkout line endings before hashing tracked text sources."""

    return value.replace(b"\r\n", b"\n")
def _strict_json_equal(actual: object, expected: object) -> bool:
    """Compare JSON authority values without bool/int/float coercion."""

    if type(actual) is not type(expected):
        return False
    if isinstance(expected, dict):
        return (
            actual.keys() == expected.keys()
            and all(
                _strict_json_equal(actual[key], expected_value)
                for key, expected_value in expected.items()
            )
        )
    if isinstance(expected, list):
        return len(actual) == len(expected) and all(
            _strict_json_equal(actual_value, expected_value)
            for actual_value, expected_value in zip(actual, expected)
        )
    return actual == expected
def _strict_json_object(
    pairs: list[tuple[str, object]],
) -> dict[str, object]:
    """Build one JSON object while rejecting duplicate member names."""

    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key!r}")
        result[key] = value
    return result
def _strict_json_float(value: str) -> float:
    """Parse one finite JSON float without accepting overflow as infinity."""

    parsed = float(value)
    if not math.isfinite(parsed):
        raise ValueError(f"non-finite JSON number: {value}")
    return parsed


__all__ = [name for name in globals() if not name.startswith("__")]
