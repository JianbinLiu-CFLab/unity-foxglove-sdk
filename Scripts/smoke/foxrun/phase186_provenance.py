#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Fail-closed Phase186 reference provenance and SDK ROS inventory validation."""

from __future__ import annotations

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
    return hashlib.sha256(value).hexdigest()


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


def _reject_json_constant(value: str) -> object:
    raise ValueError(f"non-finite JSON number: {value}")


def _strict_json_loads(source: str | bytes) -> object:
    """Parse strict JSON: unique keys and finite RFC-compatible numbers."""

    return json.loads(
        source,
        object_pairs_hook=_strict_json_object,
        parse_constant=_reject_json_constant,
        parse_float=_strict_json_float,
    )


def _absolute_lexical_path(path: pathlib.Path) -> pathlib.Path:
    """Make a path absolute without following links or erasing ``..`` aliases."""

    return path if path.is_absolute() else pathlib.Path.cwd() / path


def _is_reparse_point(stat_result: os.stat_result) -> bool:
    """Return whether an lstat result denotes a symlink/junction/reparse point."""

    reparse_attribute = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return stat.S_ISLNK(stat_result.st_mode) or bool(
        getattr(stat_result, "st_file_attributes", 0) & reparse_attribute
    )


def _canonical_json_sha256(value: object) -> str:
    encoded = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return _sha256_bytes(encoded)


def path_inventory_digest(paths: Iterable[str]) -> str:
    """Hash one canonical sorted path inventory."""

    canonical = sorted({_normal_path(path) for path in paths if _normal_path(path)})
    payload = "".join(path + "\n" for path in canonical).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def _subprocess_lines(command: Sequence[str], cwd: pathlib.Path) -> list[str]:
    completed = subprocess.run(
        list(command),
        cwd=cwd,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
    )
    if completed.returncode != 0:
        detail = (completed.stderr or completed.stdout).strip()
        raise RuntimeError(detail or f"command failed with exit code {completed.returncode}")
    return [line.strip() for line in completed.stdout.splitlines() if line.strip()]


def _substantial_lines(source: str) -> list[str]:
    lines: list[str] = []
    for raw in source.splitlines():
        line = re.sub(r"\s+", " ", raw.strip())
        if (
            not line
            or line in {"{", "}", "};"}
            or line.startswith("using ")
            or line.startswith("namespace ")
            or "SPDX-License-Identifier:" in line
            or "Copyright (c)" in line
        ):
            continue
        lines.append(line)
    return lines


def _distinctive_windows(source: str) -> set[tuple[str, ...]]:
    lines = _substantial_lines(source)
    windows: set[tuple[str, ...]] = set()
    for index in range(0, len(lines) - _OVERLAP_LINE_COUNT + 1):
        window = tuple(lines[index : index + _OVERLAP_LINE_COUNT])
        if sum(len(line) for line in window) >= _OVERLAP_MIN_CHARS:
            windows.add(window)
    return windows


def _visible_markdown_source(source: str) -> str:
    """Remove HTML comments and fenced blocks from one Markdown document."""

    without_comments = re.sub(r"<!--.*?-->", "", source, flags=re.DOTALL)
    if "<!--" in without_comments or "-->" in without_comments:
        raise ValueError("visible authority Markdown has an unclosed HTML comment")

    visible_lines: list[str] = []
    fence_character: str | None = None
    fence_length = 0
    for line in without_comments.splitlines():
        if fence_character is not None:
            closing = re.fullmatch(
                rf" {{0,3}}{re.escape(fence_character)}"
                rf"{{{fence_length},}}[ \t]*",
                line,
            )
            if closing is not None:
                fence_character = None
                fence_length = 0
            continue
        opening = re.fullmatch(r" {0,3}(`{3,}|~{3,})(.*)", line)
        if opening is not None:
            marker = opening.group(1)
            info = opening.group(2)
            if marker[0] == "`" and "`" in info:
                visible_lines.append(line)
                continue
            fence_character = marker[0]
            fence_length = len(marker)
            continue
        visible_lines.append(line)
    if fence_character is not None:
        raise ValueError("visible authority Markdown has an unclosed fenced block")
    return "\n".join(visible_lines)


def _gfm_table_separator_cells(line: str) -> list[str] | None:
    """Recognize a GFM delimiter row with optional outer pipes."""

    candidate = line.strip()
    if "|" not in candidate:
        return None
    if candidate.startswith("|"):
        candidate = candidate[1:]
    if candidate.endswith("|"):
        candidate = candidate[:-1]
    cells = [cell.strip() for cell in candidate.split("|")]
    if len(cells) < 2 or any(
        re.fullmatch(r":?-{3,}:?", cell) is None for cell in cells
    ):
        return None
    return cells


def _visible_markdown_authority_section(source: str) -> tuple[str, str]:
    """Return visible Markdown and its one authoritative protocol section."""

    raw_lines = source.splitlines()
    heading = "## Frozen limits and implementation status"
    for line in raw_lines:
        if line.strip() and (line.startswith("    ") or line.startswith("\t")):
            raise ValueError(
                "visible authority document must not contain indented code"
            )
        if re.match(r"^ {0,3}(`{3,}|~{3,})", line):
            raise ValueError(
                "visible authority document must not contain fenced code"
            )
        if "<!--" in line or "-->" in line or re.search(
            r"</?[A-Za-z][^>]*>",
            line,
        ):
            raise ValueError(
                "visible authority document must not contain raw HTML"
            )
    raw_heading_indexes = [
        index for index, line in enumerate(raw_lines) if line == heading
    ]
    if len(raw_heading_indexes) != 1:
        raise ValueError(
            "visible authority must contain exactly one canonical column-0 "
            f"{heading!r} section, observed {len(raw_heading_indexes)}"
        )
    raw_start = raw_heading_indexes[0]
    raw_end = len(raw_lines)
    for index in range(raw_start + 1, len(raw_lines)):
        if raw_lines[index].startswith("## "):
            raw_end = index
            break
    raw_section_lines = raw_lines[raw_start:raw_end]
    for line in raw_section_lines:
        if line.strip() and (line.startswith("    ") or line.startswith("\t")):
            raise ValueError(
                "visible authority section must not contain indented code"
            )
        if re.match(r"^ {0,3}(`{3,}|~{3,})", line):
            raise ValueError(
                "visible authority section must not contain fenced code"
            )
        if "<!--" in line or "-->" in line or re.search(
            r"</?[A-Za-z][^>]*>",
            line,
        ):
            raise ValueError(
                "visible authority section must not contain raw HTML"
            )

    visible_source = _visible_markdown_source(source)
    visible_lines = visible_source.splitlines()
    heading_indexes = [
        index
        for index, line in enumerate(visible_lines)
        if line == heading
    ]
    if len(heading_indexes) != 1:
        raise ValueError(
            "visible authority must contain exactly one "
            f"{heading!r} section, observed {len(heading_indexes)}"
        )
    start = heading_indexes[0]
    end = len(visible_lines)
    for index in range(start + 1, len(visible_lines)):
        if visible_lines[index].startswith("## "):
            end = index
            break
    section_source = "\n".join(visible_lines[start:end])
    section_lines = section_source.splitlines()
    for line in visible_lines:
        if (
            (line.startswith("|") or line.endswith("|"))
            and (
                line != line.strip()
                or not line.startswith("|")
                or line.startswith("||")
                or not line.endswith("|")
                or line.endswith("||")
            )
        ):
            raise ValueError(
                "visible authority table lines must have exactly one leading "
                "and trailing pipe at canonical column 0"
            )
    expected_table_blocks = (
        (
            "| Offset | Width | Meaning |",
            "| --- | ---: | --- |",
        ),
        (
            "| Error code | Terminal | Allowed wire response |",
            "| --- | --- | --- |",
        ),
        (
            "| Limit | Value |",
            "| --- | ---: |",
        ),
    )
    observed_table_blocks: list[tuple[str, str]] = []
    observed_separator_indexes: list[int] = []
    for separator_index in range(1, len(visible_lines)):
        separator = visible_lines[separator_index]
        if _gfm_table_separator_cells(separator) is None:
            continue
        observed_table_blocks.append(
            (visible_lines[separator_index - 1], separator)
        )
        observed_separator_indexes.append(separator_index)
    expected_authority_membership = [False, True, True]
    observed_authority_membership = [
        start < index < end for index in observed_separator_indexes
    ]
    if (
        observed_table_blocks != list(expected_table_blocks)
        or observed_authority_membership != expected_authority_membership
    ):
        raise ValueError(
            "visible authority document must contain the one frozen envelope "
            "table plus exactly the two canonical tables inside the authority "
            "section, with no renamed or additional GFM table"
        )
    expected_table_headers = tuple(
        header for header, _ in expected_table_blocks[1:]
    )
    for table_header in expected_table_headers:
        global_count = sum(line == table_header for line in visible_lines)
        section_count = sum(line == table_header for line in section_lines)
        if global_count != 1 or section_count != 1:
            raise ValueError(
                "visible authority table must occur exactly once inside the "
                f"authority section: {table_header!r}"
            )
    return visible_source, section_source


def _markdown_table_rows(
    source: str,
    *,
    header: str,
    column_count: int,
    table_name: str,
) -> list[list[str]]:
    lines = source.splitlines()
    header_indexes = [index for index, line in enumerate(lines) if line == header]
    if len(header_indexes) != 1:
        raise ValueError(
            f"{table_name} must contain exactly one header, "
            f"observed {len(header_indexes)}"
        )
    header_index = header_indexes[0]
    if header_index + 1 >= len(lines):
        raise ValueError(f"{table_name} has no separator row")
    separator_line = lines[header_index + 1]
    if (
        separator_line != separator_line.strip()
        or not separator_line.startswith("|")
        or separator_line.startswith("||")
        or not separator_line.endswith("|")
        or separator_line.endswith("||")
    ):
        raise ValueError(
            f"{table_name} separator must have exactly one leading and "
            "trailing pipe at canonical column 0"
        )
    separator_cells = [
        cell.strip() for cell in separator_line[1:-1].split("|")
    ]
    if len(separator_cells) != column_count or any(
        re.fullmatch(r":?-{3,}:?", cell) is None for cell in separator_cells
    ):
        raise ValueError(f"{table_name} has an invalid separator row")

    rows: list[list[str]] = []
    for line in lines[header_index + 2 :]:
        if not line.startswith("|"):
            break
        if (
            line != line.strip()
            or line.startswith("||")
            or not line.endswith("|")
            or line.endswith("||")
        ):
            raise ValueError(
                f"{table_name} row must have exactly one leading and "
                "trailing pipe at canonical column 0"
            )
        cells = [cell.strip() for cell in line[1:-1].split("|")]
        if len(cells) != column_count:
            raise ValueError(
                f"{table_name} row has {len(cells)} columns, "
                f"expected {column_count}: {line!r}"
            )
        rows.append(cells)
    return rows


def _validate_authority_table_row_uniqueness(
    section_source: str,
    *,
    identifiers: Sequence[str],
    table_name: str,
) -> list[str]:
    """Reject renamed/extra tables that repeat an authority row identity."""

    counts = {identifier: 0 for identifier in identifiers}
    for line in section_source.splitlines():
        if (
            line != line.strip()
            or not line.startswith("|")
            or line.startswith("||")
            or not line.endswith("|")
            or line.endswith("||")
        ):
            continue
        cells = [cell.strip() for cell in line[1:-1].split("|")]
        if not cells:
            continue
        match = re.fullmatch(r"`([^`]+)`", cells[0])
        if match is not None and match.group(1) in counts:
            counts[match.group(1)] += 1
    invalid = [
        f"{identifier}={count}"
        for identifier, count in counts.items()
        if count != 1
    ]
    if invalid:
        return [
            f"{table_name} has missing, extra, duplicate, or conflicting "
            "authority rows: " + ", ".join(invalid)
        ]
    return []


def _fixture_document_authority(
    implementation_sources: Mapping[str, str],
) -> tuple[list[tuple[str, str]], list[tuple[str, str, str]], list[str]]:
    """Read the exact limits/error documentation authority from the fixture."""

    errors: list[str] = []
    fixture_source = implementation_sources.get(_FIXTURE_RELATIVE)
    if fixture_source is None:
        return [], [], errors
    try:
        fixture = _strict_json_loads(fixture_source)
        limits = fixture["v2"]["commit2"]["limits"]
        error_codes = fixture["v2"]["errorCodes"]
        if not isinstance(limits, dict) or list(limits) != list(_LIMIT_NAMES):
            raise ValueError("fixture limit identities/order are not the frozen 27")
        if any(
            not isinstance(value, int) or isinstance(value, bool) or value <= 0
            for value in limits.values()
        ):
            raise ValueError("fixture limit values must be positive integers")
        limit_authority = [(name, str(value)) for name, value in limits.items()]
        if (
            not isinstance(error_codes, list)
            or len(error_codes) != len(_ERROR_CODES)
        ):
            raise ValueError("fixture errorCodes are not the frozen 23")
        error_authority: list[tuple[str, str, str]] = []
        for index, raw in enumerate(error_codes):
            if not isinstance(raw, dict):
                raise ValueError(f"fixture errorCodes[{index}] is not an object")
            code = raw.get("code")
            terminal = raw.get("terminal")
            response_ops = raw.get("responseOps")
            if (
                code != _ERROR_CODES[index]
                or not isinstance(terminal, bool)
                or not isinstance(response_ops, list)
                or any(not isinstance(operation, str) for operation in response_ops)
            ):
                raise ValueError(
                    f"fixture errorCodes[{index}] has invalid identity or mapping"
                )
            terminal_text = "yes" if terminal else "no"
            response_text = (
                ", ".join(f"`{operation}`" for operation in response_ops)
                if response_ops
                else "local only"
            )
            error_authority.append((f"`{code}`", terminal_text, response_text))
        return limit_authority, error_authority, errors
    except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
        errors.append(f"could not derive protocol document authority: {exc}")
        return [], [], errors


def validate_ledger_payload(
    payload: Mapping[str, object],
    *,
    actual_revision: str,
    implementation_sources: Mapping[str, str],
    reference_sources: Mapping[str, str],
    implementation_bytes: Mapping[str, bytes] | None = None,
) -> list[str]:
    """Validate one already-loaded ledger and its bounded source corpus."""

    errors: list[str] = []
    schema_version = payload.get("schemaVersion")
    if type(schema_version) is not int or schema_version != 1:
        errors.append(
            "provenance schemaVersion must be exactly the JSON integer 1"
        )

    reference = payload.get("reference")
    if not isinstance(reference, Mapping):
        return errors + ["provenance reference must be an object"]
    expected_reference_metadata = {
        "repository": _REFERENCE_REMOTE,
        "origin": _REFERENCE_REMOTE,
        "revision": _REFERENCE_REVISION,
        "tree": _REFERENCE_TREE,
        "commitDate": _REFERENCE_DATE,
        "subject": _REFERENCE_SUBJECT,
        "license": "Apache-2.0",
        "licenseFile": "LICENSE",
        "licenseSha256": _REFERENCE_LICENSE_SHA256,
    }
    for field, expected in expected_reference_metadata.items():
        if reference.get(field) != expected:
            errors.append(
                f"reference {field} must be exactly {expected!r}; "
                f"observed {reference.get(field)!r}"
            )
    if reference.get("revision") != actual_revision:
        errors.append(
            "reference revision mismatch: "
            f"expected {reference.get('revision')!r}, observed {actual_revision!r}"
        )

    inspected, inspected_errors = _canonical_path_list(
        reference.get("inspectedFiles"),
        label="reference inspectedFiles",
        require_nonempty=True,
    )
    errors.extend(inspected_errors)
    if inspected != list(_REFERENCE_FILES):
        errors.append(
            "reference inspectedFiles must match the locked upstream file list"
        )
    ideas = reference.get("ideasReviewed")
    if ideas != list(_REFERENCE_IDEAS):
        errors.append("reference ideasReviewed must match the locked clean-room ideas")
    if not isinstance(reference.get("materialCopied"), bool):
        errors.append("reference materialCopied must be a Boolean")

    expected_reference_paths = set(inspected)
    observed_reference_paths, reference_path_errors = _canonical_path_list(
        list(reference_sources.keys()),
        label="reference source paths",
    )
    errors.extend(reference_path_errors)
    missing_reference = sorted(
        expected_reference_paths - set(observed_reference_paths)
    )
    if missing_reference:
        errors.append(
            "missing inspected reference files: " + ", ".join(missing_reference)
        )

    implementations = payload.get("implementations")
    if not isinstance(implementations, list) or not implementations:
        return errors + ["provenance implementations must be a non-empty list"]

    records: dict[str, Mapping[str, object]] = {}
    portable_record_paths: dict[str, str] = {}
    has_material_copy = False
    for index, raw_record in enumerate(implementations):
        if not isinstance(raw_record, Mapping):
            errors.append(f"implementation record {index} must be an object")
            continue
        path_value = raw_record.get("path")
        try:
            path = _canonical_relative_path(
                path_value,
                label=f"implementation record {index} path",
            )
        except ValueError as exc:
            errors.append(str(exc))
            continue
        portable_identity = unicodedata.normalize("NFC", path).casefold()
        previous = portable_record_paths.get(portable_identity)
        if previous is not None:
            errors.append(
                "case-insensitive duplicate implementation provenance path: "
                f"{previous!r} and {path!r}"
            )
            continue
        portable_record_paths[portable_identity] = path
        if path in records:
            errors.append(f"duplicate implementation provenance path: {path}")
            continue
        records[path] = raw_record
        digest = raw_record.get("sha256")
        if not isinstance(digest, str) or _SHA256_PATTERN.fullmatch(digest) is None:
            errors.append(f"{path}: sha256 must be exactly 64 lowercase hex digits")
        classification = raw_record.get("classification")
        if classification not in _CLASSIFICATIONS:
            errors.append(f"{path}: unknown classification {classification!r}")
        influence = raw_record.get("influence")
        if not isinstance(influence, str) or not influence.strip():
            errors.append(f"{path}: influence must be non-empty")
        if classification in {"inspired", "materially_copied"}:
            references = raw_record.get("referenceFiles")
            reference_paths, reference_errors = _canonical_path_list(
                references,
                label=f"{path} referenceFiles",
                require_nonempty=True,
            )
            errors.extend(reference_errors)
            if any(item not in expected_reference_paths for item in reference_paths):
                errors.append(f"{path}: referenceFiles must name inspected upstream files")
        if classification == "materially_copied":
            has_material_copy = True
            notice = raw_record.get("licenseNotice")
            if not isinstance(notice, str) or not notice.strip():
                errors.append(f"{path}: materially_copied content requires licenseNotice")
            elif "Apache-2.0" not in notice:
                errors.append(
                    f"{path}: materially_copied licenseNotice must name Apache-2.0"
                )
        elif "licenseNotice" in raw_record:
            errors.append(
                f"{path}: licenseNotice is only valid for materially_copied content"
            )

    if reference.get("materialCopied") is not has_material_copy:
        errors.append(
            "reference materialCopied is inconsistent with per-record classifications"
        )

    observed_implementation_paths, implementation_path_errors = _canonical_path_list(
        list(implementation_sources.keys()),
        label="implementation source paths",
    )
    errors.extend(implementation_path_errors)
    missing_implementation = sorted(
        set(records) - set(observed_implementation_paths)
    )
    if missing_implementation:
        errors.append(
            "missing implementation files: " + ", ".join(missing_implementation)
        )

    reference_windows: dict[tuple[str, ...], str] = {}
    for reference_path, source in reference_sources.items():
        for window in _distinctive_windows(source):
            reference_windows.setdefault(window, _normal_path(reference_path))

    for implementation_path, source in implementation_sources.items():
        try:
            path = _canonical_relative_path(
                implementation_path,
                label="implementation source path",
            )
        except ValueError:
            continue
        record = records.get(path)
        if record is None:
            errors.append(f"implementation file has no provenance record: {path}")
            continue
        source_bytes = (
            implementation_bytes.get(path)
            if implementation_bytes is not None
            else source.encode("utf-8")
        )
        if source_bytes is None:
            errors.append(f"{path}: exact implementation bytes were not supplied")
        else:
            observed_digest = _sha256_bytes(source_bytes)
            if record.get("sha256") != observed_digest:
                errors.append(
                    f"{path}: sha256 mismatch; observed {observed_digest}"
                )
        if record.get("classification") == "materially_copied":
            continue
        for window in _distinctive_windows(source):
            reference_path = reference_windows.get(window)
            if reference_path is not None:
                errors.append(
                    f"{path}: unexplained distinctive overlap with {reference_path}"
                )
                break

    limit_authority, error_authority, document_authority_errors = (
        _fixture_document_authority(implementation_sources)
    )
    errors.extend(document_authority_errors)
    for document_path in _PROTOCOL_DOCS:
        source = implementation_sources.get(document_path)
        if source is None:
            continue
        try:
            visible_source = _visible_markdown_source(source)
        except ValueError as exc:
            errors.append(f"{document_path}: {exc}")
            continue
        try:
            _, authority_section = _visible_markdown_authority_section(source)
        except ValueError as exc:
            errors.append(f"{document_path}: {exc}")
            authority_section = ""
        lower_source = visible_source.casefold()
        required_anchors = (
            (_REFERENCE_REMOTE, _REFERENCE_REMOTE),
            (_REFERENCE_REVISION, _REFERENCE_REVISION),
            (_REFERENCE_DATE, _REFERENCE_DATE),
            (_REFERENCE_SUBJECT, _REFERENCE_SUBJECT),
            ("Apache-2.0", "Apache-2.0"),
            ("original", "original"),
            ("no implementation code or comments were copied", "no implementation code"),
            ("PROVENANCE.json", "PROVENANCE.json"),
            (
                "not yet wired into the production runtime",
                "not yet wired into the production runtime",
            ),
        )
        for anchor, diagnostic in required_anchors:
            if anchor.casefold() not in lower_source:
                errors.append(
                    f"{document_path}: protocol document must contain {diagnostic!r}"
                )
        for code in _ERROR_CODES:
            if f"`{code}`" not in visible_source:
                errors.append(
                    f"{document_path}: protocol document is missing error code {code!r}"
                )
        for limit in _LIMIT_NAMES:
            if f"`{limit}`" not in visible_source:
                errors.append(
                    f"{document_path}: protocol document is missing limit {limit!r}"
                )
        expected_link = _PROTOCOL_DOC_LEDGER_LINKS[document_path]
        if f"]({expected_link})" not in visible_source:
            errors.append(
                f"{document_path}: protocol document must link {expected_link!r}"
            )
        if limit_authority:
            try:
                observed_limit_rows = _markdown_table_rows(
                    authority_section,
                    header="| Limit | Value |",
                    column_count=2,
                    table_name="visible authority limit table",
                )
                expected_limit_rows = [
                    [f"`{name}`", value] for name, value in limit_authority
                ]
                if observed_limit_rows != expected_limit_rows:
                    errors.append(
                        f"{document_path}: limit table mismatch; expected the "
                        "exact ordered 27-row fixture authority with no extras "
                        "or duplicates"
                    )
            except ValueError as exc:
                errors.append(f"{document_path}: {exc}")
            errors.extend(
                f"{document_path}: {error}"
                for error in _validate_authority_table_row_uniqueness(
                    authority_section,
                    identifiers=_LIMIT_NAMES,
                    table_name="visible authority limit table",
                )
            )
        if error_authority:
            try:
                observed_error_rows = _markdown_table_rows(
                    authority_section,
                    header=(
                        "| Error code | Terminal | Allowed wire response |"
                    ),
                    column_count=3,
                    table_name="visible authority error table",
                )
                expected_error_rows = [list(row) for row in error_authority]
                if observed_error_rows != expected_error_rows:
                    errors.append(
                        f"{document_path}: error table mismatch; expected the "
                        "exact ordered 23-row fixture mapping with no extras "
                        "or duplicates"
                    )
            except ValueError as exc:
                errors.append(f"{document_path}: {exc}")
            errors.extend(
                f"{document_path}: {error}"
                for error in _validate_authority_table_row_uniqueness(
                    authority_section,
                    identifiers=_ERROR_CODES,
                    table_name="visible authority error table",
                )
            )

    compatibility = payload.get("v1Compatibility")
    if compatibility is not None:
        if not isinstance(compatibility, Mapping):
            errors.append("v1Compatibility must be an object")
        else:
            fixture_path = compatibility.get("fixturePath")
            source = (
                implementation_sources.get(fixture_path)
                if isinstance(fixture_path, str)
                else None
            )
            keys = compatibility.get("topLevelKeys")
            if not isinstance(keys, list) or any(
                not isinstance(key, str) for key in keys
            ):
                errors.append("v1Compatibility topLevelKeys must be a string list")
            elif source is None:
                errors.append("v1Compatibility fixture is missing from implementations")
            else:
                try:
                    current_fixture = _strict_json_loads(source)
                    subset = {key: current_fixture[key] for key in keys}
                    observed_digest = _canonical_json_sha256(subset)
                    if observed_digest != compatibility.get("canonicalSha256"):
                        errors.append(
                            "frozen v1 canonical state mismatch; "
                            f"observed {observed_digest}"
                        )
                except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
                    errors.append(f"could not validate frozen v1 fixture: {exc}")

    return errors


def _read_json(path: pathlib.Path) -> Mapping[str, object]:
    payload = _strict_json_loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError(f"{path} must contain one JSON object")
    return payload


def _subprocess_bytes(command: Sequence[str], cwd: pathlib.Path) -> bytes:
    completed = subprocess.run(
        list(command),
        cwd=cwd,
        check=False,
        capture_output=True,
    )
    if completed.returncode != 0:
        detail = completed.stderr.decode("utf-8", errors="replace").strip()
        raise RuntimeError(
            detail or f"command failed with exit code {completed.returncode}"
        )
    return completed.stdout


def _git_blob(repository: pathlib.Path, revision: str, path: str) -> bytes:
    path = _canonical_relative_path(path, label="Git blob path")
    return _subprocess_bytes(
        ["git", "cat-file", "blob", f"{revision}:{path}"],
        repository,
    )


def _resolve_contained(
    root: pathlib.Path,
    relative: str,
    *,
    label: str,
) -> pathlib.Path:
    canonical = _canonical_relative_path(relative, label=label)
    candidate = root.joinpath(*pathlib.PurePosixPath(canonical).parts)
    resolved = candidate.resolve(strict=True)
    if not resolved.is_relative_to(root):
        raise ValueError(f"{label} resolves outside repository: {relative!r}")
    return resolved


def _resolve_regular_file_contained(
    root: pathlib.Path,
    relative: str,
    *,
    label: str,
) -> pathlib.Path:
    """Resolve one lexical regular file without traversing reparse aliases."""

    canonical = _canonical_relative_path(relative, label=label)
    current = root
    parts = pathlib.PurePosixPath(canonical).parts
    for index, part in enumerate(parts):
        current = current / part
        result = current.lstat()
        if _is_reparse_point(result):
            raise ValueError(
                f"{label} must be a regular non-symlink file and must not "
                f"traverse reparse points: {relative!r}"
            )
        if index < len(parts) - 1 and not stat.S_ISDIR(result.st_mode):
            raise ValueError(
                f"{label} parent component is not a directory: {relative!r}"
            )
        if index == len(parts) - 1 and not stat.S_ISREG(result.st_mode):
            raise ValueError(
                f"{label} must be a regular non-symlink file: {relative!r}"
            )
    resolved = current.resolve(strict=True)
    if not resolved.is_relative_to(root):
        raise ValueError(f"{label} resolves outside repository: {relative!r}")
    return resolved


def _is_protocol_source_path(path: str) -> bool:
    pure = pathlib.PurePosixPath(path)
    for root, pattern in _PROTOCOL_SOURCE_ROOTS:
        if pure.parent.as_posix() == root and fnmatch.fnmatchcase(pure.name, pattern):
            return True
    return False


def _discover_protocol_sources(
    repository: pathlib.Path,
) -> tuple[set[str], list[str]]:
    discovered: set[str] = set()
    errors: list[str] = []
    for root_relative, pattern in _PROTOCOL_SOURCE_ROOTS:
        root_candidate = repository.joinpath(
            *pathlib.PurePosixPath(root_relative).parts
        )
        try:
            root_stat = root_candidate.lstat()
            if _is_reparse_point(root_stat):
                errors.append(
                    "protocol source directory must not be a symlink or "
                    f"reparse point: {root_relative}"
                )
                continue
            if not stat.S_ISDIR(root_stat.st_mode):
                errors.append(
                    f"protocol source root is not a directory: {root_relative}"
                )
                continue
            root = _resolve_contained(
                repository,
                root_relative,
                label="protocol source root",
            )
        except (OSError, ValueError) as exc:
            errors.append(str(exc))
            continue

        pending = [root]
        while pending:
            current = pending.pop()
            try:
                entries = list(os.scandir(current))
            except OSError as exc:
                errors.append(str(exc))
                continue
            for entry in entries:
                candidate = pathlib.Path(entry.path)
                try:
                    entry_stat = candidate.lstat()
                except OSError as exc:
                    errors.append(str(exc))
                    continue
                is_reparse = _is_reparse_point(entry_stat)
                if is_reparse:
                    relative = candidate.relative_to(repository).as_posix()
                    if entry.is_dir(follow_symlinks=True):
                        errors.append(
                            "protocol source directory must not be a symlink or "
                            f"reparse point: {relative}"
                        )
                    elif fnmatch.fnmatchcase(
                        entry.name.casefold(),
                        pattern.casefold(),
                    ):
                        try:
                            _resolve_contained(
                                repository,
                                relative,
                                label="protocol implementation path",
                            )
                        except (OSError, ValueError) as exc:
                            errors.append(str(exc))
                        else:
                            errors.append(
                                "protocol source entry must not be a symlink or "
                                f"reparse point: {relative}"
                            )
                    else:
                        errors.append(
                            "protocol source entry must not be a symlink or "
                            f"reparse point: {relative}"
                        )
                    continue
                if stat.S_ISDIR(entry_stat.st_mode):
                    pending.append(candidate)
                    continue
                if not fnmatch.fnmatchcase(
                    entry.name.casefold(),
                    pattern.casefold(),
                ):
                    continue
                relative = candidate.relative_to(repository).as_posix()
                try:
                    _resolve_contained(
                        repository,
                        relative,
                        label="protocol implementation path",
                    )
                except (OSError, ValueError) as exc:
                    errors.append(str(exc))
                    continue
                if is_reparse or not stat.S_ISREG(entry_stat.st_mode):
                    errors.append(
                        "protocol implementation file must be a regular "
                        f"non-symlink file: {relative}"
                    )
                    continue
                discovered.add(relative)
    return discovered, errors


def _phase186b_introduced_sources(
    repository: pathlib.Path,
) -> tuple[dict[str, str], list[str]]:
    introduced: dict[str, str] = {}
    errors: list[str] = []
    for revision, subject, expected_count in _PHASE186B_SOURCE_COMMITS:
        try:
            object_type = _subprocess_lines(
                ["git", "cat-file", "-t", revision],
                repository,
            )[0]
            if object_type != "commit":
                errors.append(f"Phase186B source revision {revision} is not a commit")
                continue
            actual_subject = _subprocess_lines(
                ["git", "show", "-s", "--format=%s", revision],
                repository,
            )[0]
            if actual_subject != subject:
                errors.append(
                    f"Phase186B source revision {revision} subject mismatch: "
                    f"{actual_subject!r}"
                )
            ancestor = subprocess.run(
                ["git", "merge-base", "--is-ancestor", revision, "HEAD"],
                cwd=repository,
                check=False,
                capture_output=True,
            )
            if ancestor.returncode != 0:
                errors.append(
                    f"Phase186B source revision {revision} is not an ancestor of HEAD"
                )
            added_raw = _subprocess_bytes(
                [
                    "git",
                    "diff-tree",
                    "--no-commit-id",
                    "--name-only",
                    "-r",
                    "--diff-filter=A",
                    "-z",
                    revision,
                ],
                repository,
            )
            added = [
                item.decode("utf-8", errors="strict")
                for item in added_raw.split(b"\0")
                if item
            ]
            sources = sorted(path for path in added if _is_protocol_source_path(path))
            if len(sources) != expected_count:
                errors.append(
                    f"Phase186B source revision {revision} introduced "
                    f"{len(sources)} protocol sources, expected {expected_count}"
                )
            for path in sources:
                previous = introduced.get(path)
                if previous is not None:
                    errors.append(
                        f"Phase186B protocol source {path} was introduced twice: "
                        f"{previous} and {revision}"
                    )
                introduced[path] = revision
        except (IndexError, OSError, RuntimeError, UnicodeDecodeError) as exc:
            errors.append(
                f"could not inspect Phase186B source revision {revision}: {exc}"
            )
    if len(introduced) != 14:
        errors.append(
            f"Phase186B fixed introduced source set has {len(introduced)} paths, "
            "expected 14"
        )
    return introduced, errors


def _validate_v1_repository_authority(
    repository: pathlib.Path,
    payload: Mapping[str, object],
    implementation_sources: Mapping[str, str],
) -> list[str]:
    errors: list[str] = []
    compatibility = payload.get("v1Compatibility")
    if not isinstance(compatibility, Mapping):
        return ["provenance v1Compatibility must be an object"]
    expected = {
        "capturedFromHead": _V1_CAPTURE_COMMIT,
        "fixturePath": _FIXTURE_RELATIVE,
        "topLevelKeys": list(_V1_TOP_LEVEL_KEYS),
        "canonicalSha256": _V1_CANONICAL_SHA256,
    }
    for field, value in expected.items():
        if compatibility.get(field) != value:
            errors.append(
                f"v1Compatibility {field} must be exactly {value!r}; "
                f"observed {compatibility.get(field)!r}"
            )
    try:
        object_type = _subprocess_lines(
            ["git", "cat-file", "-t", _V1_CAPTURE_COMMIT],
            repository,
        )[0]
        if object_type != "commit":
            errors.append("frozen v1 capture object is not a commit")
            return errors
        frozen = _strict_json_loads(
            _git_blob(repository, _V1_CAPTURE_COMMIT, _FIXTURE_RELATIVE)
        )
        if list(frozen.keys()) != list(_V1_TOP_LEVEL_KEYS):
            errors.append("frozen v1 Git object has an unexpected top-level shape")
        frozen_subset = {key: frozen[key] for key in _V1_TOP_LEVEL_KEYS}
        frozen_digest = _canonical_json_sha256(frozen_subset)
        if frozen_digest != _V1_CANONICAL_SHA256:
            errors.append(
                "frozen v1 Git object canonical digest mismatch; "
                f"observed {frozen_digest}"
            )
        current = _strict_json_loads(
            implementation_sources[_FIXTURE_RELATIVE]
        )
        current_subset = {key: current[key] for key in _V1_TOP_LEVEL_KEYS}
        if current_subset != frozen_subset:
            errors.append("current fixture changed frozen v1 bytes or state")
        current_extra = set(current) - set(_V1_TOP_LEVEL_KEYS)
        if current_extra != {"v2"}:
            errors.append(
                "current fixture may add only the v2 top-level authority beside frozen v1"
            )
    except (
        IndexError,
        KeyError,
        OSError,
        RuntimeError,
        TypeError,
        ValueError,
        json.JSONDecodeError,
    ) as exc:
        errors.append(f"could not validate frozen v1 Git authority: {exc}")
    return errors


def _validate_canonical_ledger_schema(
    payload: Mapping[str, object],
    introduced_sources: Mapping[str, str],
) -> list[str]:
    """Lock every object/key boundary in the canonical release ledger."""

    errors: list[str] = []
    top_level_keys = {
        "schemaVersion",
        "ledgerPath",
        "reference",
        "introducedSourceCommits",
        "v1Compatibility",
        "implementations",
    }
    if set(payload) != top_level_keys:
        errors.append(
            "canonical ledger top-level schema must contain exactly "
            + ", ".join(sorted(top_level_keys))
        )

    reference = payload.get("reference")
    reference_keys = {
        "repository",
        "origin",
        "revision",
        "tree",
        "commitDate",
        "subject",
        "license",
        "licenseFile",
        "licenseSha256",
        "inspectedFiles",
        "ideasReviewed",
        "materialCopied",
    }
    if not isinstance(reference, Mapping) or set(reference) != reference_keys:
        errors.append(
            "canonical ledger reference schema must contain exactly the "
            "frozen reference metadata fields"
        )

    compatibility = payload.get("v1Compatibility")
    compatibility_keys = {
        "capturedFromHead",
        "fixturePath",
        "topLevelKeys",
        "canonicalSha256",
    }
    if (
        not isinstance(compatibility, Mapping)
        or set(compatibility) != compatibility_keys
    ):
        errors.append(
            "canonical ledger v1Compatibility schema must contain exactly "
            "capturedFromHead, fixturePath, topLevelKeys, and canonicalSha256"
        )

    declared_commits = payload.get("introducedSourceCommits")
    commit_keys = {"revision", "subject", "sourceCount"}
    if isinstance(declared_commits, list):
        for index, record in enumerate(declared_commits):
            if (
                not isinstance(record, Mapping)
                or set(record) != commit_keys
            ):
                errors.append(
                    "canonical ledger introducedSourceCommits"
                    f"[{index}] schema must contain exactly revision, subject, "
                    "and sourceCount"
                )
    else:
        errors.append(
            "canonical ledger introducedSourceCommits schema must be an array"
        )

    implementations = payload.get("implementations")
    implementation_paths: list[str] = []
    if not isinstance(implementations, list):
        return errors + [
            "canonical ledger implementation schema must be an array"
        ]
    base_keys = {"path", "sha256", "classification", "influence"}
    for index, record in enumerate(implementations):
        if not isinstance(record, Mapping):
            errors.append(
                f"canonical ledger implementation schema at index {index} "
                "must be an object"
            )
            continue
        path = record.get("path")
        classification = record.get("classification")
        if isinstance(path, str):
            implementation_paths.append(path)
        expected_keys = set(base_keys)
        if isinstance(path, str) and path in introduced_sources:
            expected_keys.add("introducedIn")
        if classification in {"inspired", "materially_copied"}:
            expected_keys.add("referenceFiles")
        if classification == "materially_copied":
            expected_keys.add("licenseNotice")
        if set(record) != expected_keys:
            errors.append(
                "canonical ledger implementation schema mismatch at "
                f"index {index}: expected exactly "
                + ", ".join(sorted(expected_keys))
            )
    if implementation_paths != sorted(implementation_paths):
        errors.append(
            "canonical ledger implementations must be sorted by ordinal path"
        )
    return errors


def validate_repository_provenance(
    repository: pathlib.Path,
    reference_root: pathlib.Path,
    ledger_path: pathlib.Path,
) -> list[str]:
    """Validate the checked-out official reference and every ledgered implementation."""

    repository = repository.resolve()
    reference_root = reference_root.resolve()
    requested_ledger = _absolute_lexical_path(ledger_path)
    canonical_ledger = repository.joinpath(
        *pathlib.PurePosixPath(_LEDGER_RELATIVE).parts
    )
    if os.path.normcase(str(requested_ledger)) != os.path.normcase(
        str(canonical_ledger)
    ):
        return [
            "ledger path must be the canonical release authority: "
            f"{_LEDGER_RELATIVE}"
        ]
    try:
        ledger_path = _resolve_regular_file_contained(
            repository,
            _LEDGER_RELATIVE,
            label="canonical ledger",
        )
    except (OSError, ValueError) as exc:
        return [f"could not read provenance ledger: {exc}"]
    errors: list[str] = []
    if not ledger_path.is_relative_to(repository):
        return ["provenance ledger resolves outside repository"]
    try:
        payload = _read_json(ledger_path)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        return [f"could not read provenance ledger: {exc}"]

    try:
        git_top_level = pathlib.Path(
            _subprocess_lines(
                ["git", "rev-parse", "--show-toplevel"],
                reference_root,
            )[0]
        ).resolve(strict=True)
        if os.path.normcase(str(git_top_level)) != os.path.normcase(
            str(reference_root)
        ):
            return ["reference_root must be the exact Git top-level"]
    except (IndexError, OSError, RuntimeError) as exc:
        return [f"could not resolve reference Git top-level: {exc}"]

    try:
        revision_lines = _subprocess_lines(
            ["git", "rev-parse", "HEAD"],
            reference_root,
        )
        actual_revision = revision_lines[0]
        status = _subprocess_lines(
            ["git", "status", "--porcelain=v1", "--untracked-files=all"],
            reference_root,
        )
        if any(not line.startswith("?? ") for line in status):
            errors.append("reference checkout has tracked modifications")
        if any(line.startswith("?? ") for line in status):
            errors.append("reference checkout has untracked files")
        remotes = _subprocess_lines(
            ["git", "remote", "get-url", "origin"],
            reference_root,
        )
        if not remotes or remotes[0] != _REFERENCE_REMOTE:
            errors.append("reference origin is not the official ROS-TCP-Connector URL")
        actual_tree = _subprocess_lines(
            ["git", "show", "-s", "--format=%T", actual_revision],
            reference_root,
        )[0]
        actual_date = _subprocess_lines(
            ["git", "show", "-s", "--format=%cI", actual_revision],
            reference_root,
        )[0]
        actual_subject = _subprocess_lines(
            ["git", "show", "-s", "--format=%s", actual_revision],
            reference_root,
        )[0]
        if actual_tree != _REFERENCE_TREE:
            errors.append(
                f"reference tree mismatch: expected {_REFERENCE_TREE}, "
                f"observed {actual_tree}"
            )
        if actual_date != _REFERENCE_DATE:
            errors.append(
                f"reference commitDate mismatch: expected {_REFERENCE_DATE!r}, "
                f"observed {actual_date!r}"
            )
        if actual_subject != _REFERENCE_SUBJECT:
            errors.append(
                f"reference subject mismatch: expected {_REFERENCE_SUBJECT!r}, "
                f"observed {actual_subject!r}"
            )
    except (OSError, RuntimeError, IndexError) as exc:
        return errors + [f"could not inspect reference checkout: {exc}"]

    try:
        license_bytes = _git_blob(reference_root, actual_revision, "LICENSE")
        license_digest = _sha256_bytes(license_bytes)
        if license_digest != _REFERENCE_LICENSE_SHA256:
            errors.append(
                "reference LICENSE blob SHA-256 mismatch: "
                f"expected {_REFERENCE_LICENSE_SHA256}, observed {license_digest}"
            )
    except (OSError, RuntimeError, ValueError) as exc:
        errors.append(f"could not read pinned reference LICENSE blob: {exc}")

    reference = payload.get("reference")
    inspected = reference.get("inspectedFiles", []) if isinstance(reference, Mapping) else []
    reference_sources: dict[str, str] = {}
    for relative in inspected if isinstance(inspected, list) else []:
        if not isinstance(relative, str):
            continue
        try:
            canonical = _canonical_relative_path(
                relative,
                label="inspected reference file",
            )
            reference_sources[canonical] = _git_blob(
                reference_root,
                actual_revision,
                canonical,
            ).decode("utf-8", errors="strict")
        except (OSError, RuntimeError, UnicodeDecodeError, ValueError) as exc:
            errors.append(
                f"could not read pinned inspected reference file {relative}: {exc}"
            )

    discovered_sources, discovery_errors = _discover_protocol_sources(repository)
    errors.extend(discovery_errors)
    introduced_sources, introduced_errors = _phase186b_introduced_sources(repository)
    errors.extend(introduced_errors)
    errors.extend(
        _validate_canonical_ledger_schema(payload, introduced_sources)
    )
    if discovered_sources != set(introduced_sources):
        missing = sorted(set(introduced_sources) - discovered_sources)
        extra = sorted(discovered_sources - set(introduced_sources))
        if missing:
            errors.append(
                "fixed Phase186B protocol source files are missing: "
                + ", ".join(missing)
            )
        if extra:
            errors.append(
                "protocol source files are outside the fixed Phase186B introduced set: "
                + ", ".join(extra)
            )

    expected_implementation_paths = (
        discovered_sources | set(_REQUIRED_RECORDED_AUTHORITIES)
    )
    for relative in (*_REQUIRED_RECORDED_AUTHORITIES, *_REQUIRED_UNRECORDED_AUTHORITIES):
        try:
            _resolve_regular_file_contained(
                repository,
                relative,
                label="required Phase186B authority",
            )
        except (OSError, ValueError) as exc:
            errors.append(str(exc))

    implementation_sources: dict[str, str] = {}
    implementation_source_bytes: dict[str, bytes] = {}
    for relative in sorted(expected_implementation_paths):
        try:
            path = _resolve_regular_file_contained(
                repository,
                relative,
                label="implementation file",
            )
            raw = path.read_bytes()
            implementation_source_bytes[relative] = raw
            implementation_sources[relative] = raw.decode("utf-8", errors="strict")
        except (OSError, UnicodeDecodeError, ValueError) as exc:
            errors.append(f"could not read implementation file {relative}: {exc}")

    errors.extend(
        validate_ledger_payload(
            payload,
            actual_revision=actual_revision,
            implementation_sources=implementation_sources,
            reference_sources=reference_sources,
            implementation_bytes=implementation_source_bytes,
        )
    )
    records = payload.get("implementations")
    record_by_path = {
        str(record.get("path")): record
        for record in records
        if isinstance(record, Mapping) and isinstance(record.get("path"), str)
    } if isinstance(records, list) else {}
    for path, revision in introduced_sources.items():
        record = record_by_path.get(path)
        if record is not None and record.get("introducedIn") != revision:
            errors.append(
                f"{path}: introducedIn must be {revision} because diff-tree "
                "records it as added there"
            )
    for path, record in record_by_path.items():
        if path not in introduced_sources and "introducedIn" in record:
            errors.append(
                f"{path}: introducedIn is present but the fixed Phase186B "
                "diff-tree source set does not contain this path"
            )
    declared_commits = payload.get("introducedSourceCommits")
    expected_commits = [
        {
            "revision": revision,
            "subject": subject,
            "sourceCount": count,
        }
        for revision, subject, count in _PHASE186B_SOURCE_COMMITS
    ]
    if not _strict_json_equal(declared_commits, expected_commits):
        errors.append(
            "introducedSourceCommits must match the two fixed Phase186B Git commits"
        )
    if payload.get("ledgerPath") != _LEDGER_RELATIVE:
        errors.append(f"ledgerPath must be exactly {_LEDGER_RELATIVE!r}")
    errors.extend(
        _validate_v1_repository_authority(
            repository,
            payload,
            implementation_sources,
        )
    )
    return errors


def _git_tree_paths(repository: pathlib.Path, revision: str) -> list[str]:
    output = _subprocess_bytes(
        ["git", "ls-tree", "-r", "--name-only", "-z", revision],
        repository,
    )
    return [
        raw.decode("utf-8", errors="strict")
        for raw in output.split(b"\0")
        if raw
    ]


def _scope_paths(all_paths: Sequence[str], scope: Mapping[str, object]) -> list[str]:
    prefixes = scope.get("prefixes", [])
    exact_paths = scope.get("exactPaths", [])
    globs = scope.get("globs", [])
    if not all(isinstance(value, list) for value in (prefixes, exact_paths, globs)):
        raise ValueError("inventory selectors must be arrays")
    normalized_prefixes, prefix_errors = _canonical_path_list(
        prefixes,
        label="inventory prefixes",
    )
    normalized_exact_list, exact_errors = _canonical_path_list(
        exact_paths,
        label="inventory exactPaths",
    )
    normalized_globs, glob_errors = _canonical_path_list(
        globs,
        label="inventory globs",
    )
    selector_errors = prefix_errors + exact_errors + glob_errors
    portable_selectors: dict[str, str] = {}
    for selector in normalized_prefixes + normalized_exact_list + normalized_globs:
        identity = unicodedata.normalize("NFC", selector).casefold()
        previous = portable_selectors.get(identity)
        if previous is not None:
            selector_errors.append(
                "inventory selectors have a case-insensitive duplicate: "
                f"{previous!r} and {selector!r}"
            )
        portable_selectors[identity] = selector
    if selector_errors:
        raise ValueError("; ".join(selector_errors))
    prefix_identities = tuple(prefix + "/" for prefix in normalized_prefixes)
    normalized_exact = set(normalized_exact_list)
    return sorted(
        path
        for path in all_paths
        if path in normalized_exact
        or any(path.startswith(prefix) for prefix in prefix_identities)
        or any(fnmatch.fnmatchcase(path, pattern) for pattern in normalized_globs)
    )


def validate_pre_move_inventory(
    repository: pathlib.Path,
    inventory_path: pathlib.Path,
) -> list[str]:
    """Validate compact exact path counts and hashes for every pre-move ROS scope."""

    repository = repository.resolve()
    requested_inventory = _absolute_lexical_path(inventory_path)
    canonical_inventory = repository.joinpath(
        *pathlib.PurePosixPath(_INVENTORY_RELATIVE).parts
    )
    is_canonical_inventory = os.path.normcase(
        str(requested_inventory)
    ) == os.path.normcase(str(canonical_inventory))
    try:
        resolved_inventory = (
            _resolve_regular_file_contained(
                repository,
                _INVENTORY_RELATIVE,
                label="canonical inventory",
            )
            if is_canonical_inventory
            else requested_inventory.resolve(strict=True)
        )
        if not resolved_inventory.is_relative_to(repository):
            return ["pre-move inventory resolves outside repository"]
        payload = _read_json(resolved_inventory)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        return [f"could not read pre-move inventory: {exc}"]
    errors: list[str] = []
    schema_version = payload.get("schemaVersion")
    if type(schema_version) is not int or schema_version != 1:
        errors.append(
            "pre-move inventory schemaVersion must be exactly the JSON integer 1"
        )
    captured = payload.get("capturedFromHead")
    if (
        not isinstance(captured, str)
        or _FULL_OBJECT_ID_PATTERN.fullmatch(captured) is None
    ):
        return errors + [
            "capturedFromHead must be one full lowercase 40-hex commit object ID"
        ]
    try:
        object_type = _subprocess_lines(
            ["git", "cat-file", "-t", captured],
            repository,
        )[0]
        if object_type != "commit":
            return errors + [
                f"capturedFromHead must name a commit, observed {object_type!r}"
            ]
        resolved_commit = _subprocess_lines(
            ["git", "rev-parse", f"{captured}^{{commit}}"],
            repository,
        )[0]
        if resolved_commit != captured:
            errors.append(
                "capturedFromHead must be the exact commit ID, not an alias"
            )
        captured_tree = _subprocess_lines(
            ["git", "show", "-s", "--format=%T", captured],
            repository,
        )[0]
        if payload.get("capturedTree") != captured_tree:
            errors.append(
                "capturedTree mismatch: "
                f"expected {payload.get('capturedTree')!r}, observed {captured_tree!r}"
            )
        ancestor = subprocess.run(
            ["git", "merge-base", "--is-ancestor", captured, "HEAD"],
            cwd=repository,
            check=False,
            capture_output=True,
        )
        if ancestor.returncode != 0:
            errors.append("capturedFromHead is not an ancestor of current HEAD")
        all_paths = _git_tree_paths(repository, captured)
    except (IndexError, OSError, RuntimeError, UnicodeDecodeError) as exc:
        return errors + [f"could not inspect captured pre-move commit: {exc}"]

    inventory_relative = (
        _INVENTORY_RELATIVE
        if is_canonical_inventory
        else resolved_inventory.relative_to(repository).as_posix()
    )
    if inventory_relative == _INVENTORY_RELATIVE:
        if (
            set(payload) != _INVENTORY_TOP_LEVEL_KEYS
            or payload.get("purpose") != _INVENTORY_PURPOSE
        ):
            errors.append(
                "fixed inventory top-level authority mismatch: expected the "
                "exact keys and purpose"
            )
        if captured != _INVENTORY_CAPTURE_COMMIT:
            errors.append(
                f"capturedFromHead must be the fixed pre-move commit "
                f"{_INVENTORY_CAPTURE_COMMIT}"
            )
        if payload.get("capturedTree") != _INVENTORY_CAPTURE_TREE:
            errors.append(
                f"capturedTree must be the fixed pre-move tree "
                f"{_INVENTORY_CAPTURE_TREE}"
            )

    scopes = payload.get("scopes")
    if not isinstance(scopes, list) or not scopes:
        return errors + ["pre-move inventory scopes must be a non-empty list"]
    if (
        inventory_relative == _INVENTORY_RELATIVE
        and not _strict_json_equal(
            scopes,
            list(_INVENTORY_SCOPE_AUTHORITY),
        )
    ):
        errors.append(
            "fixed inventory scope authority mismatch: expected the exact "
            "ordered seven-scope identity/action/selector/count/digest ledger"
        )

    union: set[str] = set()
    scope_identities: dict[str, str] = {}
    for index, raw_scope in enumerate(scopes):
        if not isinstance(raw_scope, Mapping):
            errors.append(f"inventory scope {index} must be an object")
            continue
        scope_id = raw_scope.get("id")
        if not isinstance(scope_id, str) or not scope_id:
            errors.append(f"inventory scope {index} has no id")
            scope_id = str(index)
        else:
            identity = unicodedata.normalize("NFC", scope_id).casefold()
            previous = scope_identities.get(identity)
            if previous is not None:
                errors.append(
                    "duplicate inventory scope id: "
                    f"{previous!r} and {scope_id!r}"
                )
            else:
                scope_identities[identity] = scope_id
        try:
            paths = _scope_paths(all_paths, raw_scope)
        except ValueError as exc:
            errors.append(f"{scope_id}: {exc}")
            continue
        overlap = sorted(union.intersection(paths))
        if overlap:
            errors.append(
                f"{scope_id}: cross-scope overlap: " + ", ".join(overlap)
            )
        union.update(paths)
        actual_digest = path_inventory_digest(paths)
        actual_count = len(paths)
        declared_count = raw_scope.get("pathCount")
        if type(declared_count) is not int or declared_count != actual_count:
            errors.append(
                f"{scope_id}: pathCount mismatch; "
                f"expected {declared_count!r}, observed {actual_count}"
            )
        if raw_scope.get("pathDigestSha256") != actual_digest:
            errors.append(
                f"{scope_id}: path digest mismatch; observed {actual_digest}"
            )
        if raw_scope.get("action") not in {
            "move_to_bridge",
            "move_to_r2fu",
            "split_between_providers",
            "delete_from_sdk",
        }:
            errors.append(f"{scope_id}: action is not a recognized extraction action")

    actual_union_digest = path_inventory_digest(union)
    declared_total_count = payload.get("totalPathCount")
    if (
        type(declared_total_count) is not int
        or declared_total_count != len(union)
    ):
        errors.append(
            "totalPathCount mismatch; "
            f"expected {declared_total_count!r}, observed {len(union)}"
        )
    if payload.get("totalPathDigestSha256") != actual_union_digest:
        errors.append(
            "total path digest mismatch; " f"observed {actual_union_digest}"
        )
    if inventory_relative == _INVENTORY_RELATIVE:
        if (
            type(declared_total_count) is not int
            or declared_total_count != _INVENTORY_PATH_COUNT
        ):
            errors.append(
                "totalPathCount must remain exactly the JSON integer "
                f"{_INVENTORY_PATH_COUNT}"
            )
        if payload.get("totalPathDigestSha256") != _INVENTORY_PATH_DIGEST:
            errors.append(
                "totalPathDigestSha256 must remain the frozen pre-move digest"
            )
    return errors


def _default_paths(repository: pathlib.Path) -> tuple[pathlib.Path, pathlib.Path, pathlib.Path]:
    return (
        repository / "third-party" / "ROS-TCP-Connector",
        repository
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "PROVENANCE.json",
        repository
        / "Packages"
        / "dev.unity2foxglove.sdk"
        / "Tests"
        / "Unit"
        / "Phase186"
        / "Fixtures"
        / "pre_move_sdk_ros_inventory.json",
    )


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=pathlib.Path, default=pathlib.Path.cwd())
    parser.add_argument("--reference-root", type=pathlib.Path)
    parser.add_argument("--ledger", type=pathlib.Path)
    parser.add_argument("--inventory", type=pathlib.Path)
    arguments = parser.parse_args(argv)
    repository = arguments.repository.resolve()
    default_reference, default_ledger, default_inventory = _default_paths(repository)
    errors = validate_repository_provenance(
        repository,
        arguments.reference_root or default_reference,
        arguments.ledger or default_ledger,
    )
    selected_inventory = arguments.inventory or default_inventory
    if arguments.inventory is not None and os.path.normcase(
        str(_absolute_lexical_path(selected_inventory))
    ) != os.path.normcase(str(default_inventory)):
        errors.append(
            "release inventory path must be the canonical authority: "
            f"{_INVENTORY_RELATIVE}"
        )
    errors.extend(
        validate_pre_move_inventory(
            repository,
            selected_inventory,
        )
    )
    if errors:
        for error in errors:
            print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print("PASS: Phase186 provenance and pre-move SDK ROS inventory are exact.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
