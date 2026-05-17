#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Generate the Unity runtime catalog for official Foxglove ROS 2 .msg schemas.
# Usage: python Scripts/schema/generate_ros2_msg_schema_catalog.py
# Inputs: third-party/foxglove-sdk/schemas/ros2/*.msg by default.
# Outputs: Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/FoxgloveRos2MsgSchemaCatalog.cs.
"""Generate the Unity runtime catalog for official Foxglove ROS 2 .msg schemas."""

from __future__ import annotations

import argparse
import base64
import hashlib
import re
import subprocess
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_INPUT = REPO_ROOT / "third-party" / "foxglove-sdk" / "schemas" / "ros2"
DEFAULT_OUTPUT = (
    REPO_ROOT
    / "Packages"
    / "dev.unity2foxglove.sdk"
    / "Runtime"
    / "Schemas"
    / "Ros2Msg"
    / "FoxgloveRos2MsgSchemaCatalog.cs"
)
EXPECTED_FILE_COUNT = 41
SCHEMA_ENCODING = "ros2msg"


# Minimal ROS 2 common message definitions referenced by the official Foxglove
# ROS 2 .msg schemas. Keeping these pinned avoids requiring a ROS 2 install.
STANDARD_DEFINITIONS = {
    "builtin_interfaces/Time": "int32 sec\nuint32 nanosec\n",
    "builtin_interfaces/Duration": "int32 sec\nuint32 nanosec\n",
    "geometry_msgs/Point": "float64 x\nfloat64 y\nfloat64 z\n",
    "geometry_msgs/Quaternion": "float64 x\nfloat64 y\nfloat64 z\nfloat64 w\n",
    "geometry_msgs/Vector3": "float64 x\nfloat64 y\nfloat64 z\n",
    "geometry_msgs/Pose": "geometry_msgs/Point position\ngeometry_msgs/Quaternion orientation\n",
}

DEDICATED_JSON_OR_PROTOBUF_PUBLISHERS = {
    "CameraCalibration",
    "CompressedImage",
    "FrameTransform",
    "LaserScan",
    "PointCloud",
    "SceneUpdate",
}

CATEGORIES = {
    "ArrowPrimitive": "visualization",
    "CameraCalibration": "image",
    "CircleAnnotation": "annotation",
    "Color": "geometry",
    "CompressedImage": "image",
    "CompressedPointCloud": "point cloud",
    "CompressedVideo": "image",
    "CubePrimitive": "visualization",
    "CylinderPrimitive": "visualization",
    "FrameTransform": "transform",
    "FrameTransforms": "transform",
    "GeoJSON": "location",
    "Grid": "grid",
    "ImageAnnotations": "annotation",
    "JointState": "robot state",
    "JointStates": "robot state",
    "KeyValuePair": "metadata",
    "LaserScan": "range",
    "LinePrimitive": "visualization",
    "LocationFix": "location",
    "LocationFixes": "location",
    "Log": "debug",
    "ModelPrimitive": "visualization",
    "Odometry": "robot state",
    "PackedElementField": "layout",
    "Point2": "geometry",
    "PointCloud": "point cloud",
    "PointsAnnotation": "annotation",
    "PoseInFrame": "geometry",
    "PosesInFrame": "geometry",
    "RawAudio": "audio",
    "RawImage": "image",
    "SceneEntity": "visualization",
    "SceneEntityDeletion": "visualization",
    "SceneUpdate": "visualization",
    "SpherePrimitive": "visualization",
    "TextAnnotation": "annotation",
    "TextPrimitive": "visualization",
    "TriangleListPrimitive": "visualization",
    "Vector2": "geometry",
    "VoxelGrid": "grid",
}


def parse_args() -> argparse.Namespace:
    """Parse command-line options for input and output locations."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    return parser.parse_args()


def normalize_type(type_token: str) -> str:
    """Remove ROS array suffixes from a message type token."""
    return re.sub(r"\[[^\]]*\]$", "", type_token.strip())


def dependency_tokens(msg_text: str) -> list[str]:
    """Return dependency type tokens referenced by a ROS 2 .msg body."""
    deps: list[str] = []
    for raw_line in msg_text.splitlines():
        line = raw_line.split("#", 1)[0].strip()
        if not line or "=" in line:
            continue
        parts = line.split()
        if len(parts) < 2:
            continue
        token = normalize_type(parts[0])
        if "/" in token:
            deps.append(token)
    return deps


def dependency_content(dep: str, local_sources: dict[str, str]) -> str:
    """Resolve a dependency token to pinned standard or local Foxglove .msg text."""
    if dep in STANDARD_DEFINITIONS:
        return STANDARD_DEFINITIONS[dep]
    if dep.startswith("foxglove_msgs/"):
        name = dep.split("/", 1)[1]
        if name in local_sources:
            return local_sources[name]
    raise ValueError(f"Unknown ROS 2 .msg dependency: {dep}")


def collect_dependencies(root_text: str, local_sources: dict[str, str]) -> list[tuple[str, str]]:
    """Collect transitive dependencies in deterministic first-seen order."""
    seen: set[str] = set()
    ordered: list[tuple[str, str]] = []

    def visit(text: str) -> None:
        """Visit one message body and recursively collect dependencies."""
        for dep in dependency_tokens(text):
            content = dependency_content(dep, local_sources)
            if dep in seen:
                continue
            seen.add(dep)
            ordered.append((dep, content))
            visit(content)

    visit(root_text)
    return ordered


def merged_schema(root_text: str, local_sources: dict[str, str]) -> str:
    """Build Foxglove-style merged .msg text with dependency sections."""
    result = root_text
    if not result.endswith("\n"):
        result += "\n"
    for dep, content in collect_dependencies(root_text, local_sources):
        result += "================================================================================\n"
        result += f"MSG: {dep}\n"
        result += content
        if not result.endswith("\n"):
            result += "\n"
    return result


def source_tree_sha(files: list[Path]) -> str:
    """Compute a deterministic SHA-256 over sorted root filenames and bytes."""
    sha = hashlib.sha256()
    for path in files:
        sha.update(path.name.encode("utf-8"))
        sha.update(b"\0")
        sha.update(path.read_bytes())
        sha.update(b"\0")
    return sha.hexdigest()


def try_source_commit(input_dir: Path) -> str:
    """Return the upstream checkout commit when the snapshot is inside a git repo."""
    try:
        result = subprocess.run(
            ["git", "-C", str(input_dir.parents[1]), "rev-parse", "HEAD"],
            capture_output=True,
            check=True,
            text=True,
        )
        return result.stdout.strip()
    except Exception:
        return ""


def csharp_string(value: str) -> str:
    """Escape a Python string as a C# string literal."""
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def csharp_base64_literal(value: str, indent: str) -> str:
    """Encode text as a wrapped base64 C# string literal."""
    encoded = base64.b64encode(value.encode("utf-8")).decode("ascii")
    chunks = [encoded[i : i + 96] for i in range(0, len(encoded), 96)]
    if len(chunks) == 1:
        return csharp_string(chunks[0])
    lines = []
    for index, chunk in enumerate(chunks):
        suffix = " +" if index < len(chunks) - 1 else ""
        lines.append(f"{indent}{csharp_string(chunk)}{suffix}")
    return "\n".join(lines)


def generate(input_dir: Path, output: Path) -> str:
    """Generate the C# catalog source and return a short status message."""
    if not input_dir.is_dir():
        raise FileNotFoundError(f"ROS 2 schema input directory not found: {input_dir}")

    files = sorted(input_dir.glob("*.msg"), key=lambda path: path.name)
    if len(files) != EXPECTED_FILE_COUNT:
        raise RuntimeError(
            f"Expected {EXPECTED_FILE_COUNT} ROS 2 .msg files in {input_dir}, found {len(files)}"
        )

    local_sources = {path.stem: path.read_text(encoding="utf-8") for path in files}
    tree_sha = source_tree_sha(files)
    source_commit = try_source_commit(input_dir)

    entry_blocks: list[str] = []
    for path in files:
        name = path.stem
        schema_name = f"foxglove_msgs/msg/{name}"
        content = merged_schema(local_sources[name], local_sources)
        source_sha = hashlib.sha256(path.read_bytes()).hexdigest()
        category = CATEGORIES.get(name, "")
        has_publisher = "true" if name in DEDICATED_JSON_OR_PROTOBUF_PUBLISHERS else "false"
        content_literal = csharp_base64_literal(content, "                    ")
        entry_blocks.append(
            "            Entry(\n"
            f"                {csharp_string(schema_name)},\n"
            "                Decode(\n"
            f"{content_literal}),\n"
            f"                {csharp_string(path.name)},\n"
            f"                {csharp_string(source_sha)},\n"
            f"                {csharp_string(category)},\n"
            f"                {has_publisher})"
        )

    entries = ",\n".join(entry_blocks)
    output.parent.mkdir(parents=True, exist_ok=True)
    text = f"""// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg
// Purpose: Generated catalog of official Foxglove ROS 2 .msg schemas.
// Generated by Scripts/schema/generate_ros2_msg_schema_catalog.py.

using System;
using System.Collections.Generic;
using System.Text;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{{
    /// <summary>One official Foxglove ROS 2 .msg schema catalog entry.</summary>
    public sealed class FoxgloveRos2MsgSchemaCatalogEntry
    {{
        public FoxgloveRos2MsgSchemaCatalogEntry(
            string schemaName,
            string content,
            string sourceFile,
            string sourceSha256,
            string category,
            bool hasDedicatedJsonOrProtobufPublisher)
        {{
            SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
            SchemaEncoding = FoxgloveRos2MsgSchemaCatalog.SchemaEncoding;
            Content = content ?? throw new ArgumentNullException(nameof(content));
            SourceFile = sourceFile ?? string.Empty;
            SourceSha256 = sourceSha256 ?? string.Empty;
            Category = category ?? string.Empty;
            HasDedicatedJsonOrProtobufPublisher = hasDedicatedJsonOrProtobufPublisher;
        }}

        /// <summary>ROS 2 interface name, e.g. foxglove_msgs/msg/PointCloud.</summary>
        public string SchemaName {{ get; }}

        /// <summary>Foxglove schemaEncoding value for ROS 2 .msg definitions.</summary>
        public string SchemaEncoding {{ get; }}

        /// <summary>Merged ROS 2 .msg schema text, including transitive dependencies.</summary>
        public string Content {{ get; }}

        /// <summary>Source root .msg filename from the Foxglove SDK snapshot.</summary>
        public string SourceFile {{ get; }}

        /// <summary>SHA-256 of the source root .msg file.</summary>
        public string SourceSha256 {{ get; }}

        /// <summary>Coarse schema category used for documentation.</summary>
        public string Category {{ get; }}

        /// <summary>Whether Unity2Foxglove has an existing JSON/protobuf publisher UX for this schema.</summary>
        public bool HasDedicatedJsonOrProtobufPublisher {{ get; }}
    }}

    /// <summary>Generated catalog for the bundled official Foxglove ROS 2 .msg snapshot.</summary>
    public static class FoxgloveRos2MsgSchemaCatalog
    {{
        public const string SchemaEncoding = "{SCHEMA_ENCODING}";
        public const int SourceFileCount = {EXPECTED_FILE_COUNT};
        public const string SourceSnapshot = "third-party/foxglove-sdk/schemas/ros2";
        public const string SourceTreeSha256 = "{tree_sha}";
        public const string SourceCommit = "{source_commit}";

        private static readonly FoxgloveRos2MsgSchemaCatalogEntry[] EntriesArray =
        {{
{entries}
        }};

        /// <summary>Read-only list of all generated ROS 2 .msg schemas.</summary>
        public static IReadOnlyList<FoxgloveRos2MsgSchemaCatalogEntry> Entries {{ get; }} = Array.AsReadOnly(EntriesArray);

        /// <summary>Find a catalog entry by ROS 2 interface schema name.</summary>
        public static bool TryGet(string schemaName, out FoxgloveRos2MsgSchemaCatalogEntry entry)
        {{
            foreach (var candidate in EntriesArray)
            {{
                if (string.Equals(candidate.SchemaName, schemaName, StringComparison.Ordinal))
                {{
                    entry = candidate;
                    return true;
                }}
            }}

            entry = null;
            return false;
        }}

        /// <summary>Register all ROS 2 .msg schemas in the supplied registry.</summary>
        public static void RegisterSchemas(ISchemaRegistry registry)
        {{
            if (registry == null)
                return;

            foreach (var entry in EntriesArray)
            {{
                registry.Register(new SchemaEntry
                {{
                    Name = entry.SchemaName,
                    Encoding = entry.SchemaEncoding,
                    Content = entry.Content,
                    RawContent = null
                }});
            }}
        }}

        private static FoxgloveRos2MsgSchemaCatalogEntry Entry(
            string schemaName,
            string content,
            string sourceFile,
            string sourceSha256,
            string category,
            bool hasDedicatedJsonOrProtobufPublisher)
        {{
            return new FoxgloveRos2MsgSchemaCatalogEntry(
                schemaName,
                content,
                sourceFile,
                sourceSha256,
                category,
                hasDedicatedJsonOrProtobufPublisher);
        }}

        private static string Decode(string base64)
        {{
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }}
    }}
}}
"""
    output.write_text(text, encoding="utf-8", newline="\n")
    return f"Generated {len(files)} ROS 2 .msg schema entries at {output}"


def main() -> int:
    """Run catalog generation from command-line arguments."""
    args = parse_args()
    print(generate(args.input, args.output))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
