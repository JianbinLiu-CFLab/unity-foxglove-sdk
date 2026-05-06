// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas
// Purpose: Foxglove visual schema DTOs — FrameTransform, SceneUpdate,
// and related geometry types for 3D visualization.

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Unity.FoxgloveSDK.Schemas
{
    /// <summary>Foxglove time: { sec, nsec }.</summary>
    public class FoxgloveTime
    {
        [JsonProperty("sec")] public ulong Sec { get; set; }
        [JsonProperty("nsec")] public uint Nsec { get; set; }
    }

    /// <summary>Foxglove duration: { sec, nsec }.</summary>
    public class FoxgloveDuration
    {
        [JsonProperty("sec")] public long Sec { get; set; }
        [JsonProperty("nsec")] public uint Nsec { get; set; }
    }

    /// <summary>3D vector.</summary>
    public class FoxgloveVector3
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
    }

    /// <summary>Quaternion orientation.</summary>
    public class FoxgloveQuaternion
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
        [JsonProperty("w")] public double W { get; set; }
    }

    /// <summary>Pose: position + orientation.</summary>
    public class FoxglovePose
    {
        [JsonProperty("position")] public FoxgloveVector3 Position { get; set; }
        [JsonProperty("orientation")] public FoxgloveQuaternion Orientation { get; set; }
    }

    /// <summary>RGBA color, 0 to 1 range.</summary>
    public class FoxgloveColor
    {
        [JsonProperty("r")] public double R { get; set; }
        [JsonProperty("g")] public double G { get; set; }
        [JsonProperty("b")] public double B { get; set; }
        [JsonProperty("a")] public double A { get; set; }
    }

    /// <summary>Key-value string pair.</summary>
    public class FoxgloveKeyValuePair
    {
        [JsonProperty("key")] public string Key { get; set; }
        [JsonProperty("value")] public string Value { get; set; }
    }

    // ── FrameTransform ──

    /// <summary>foxglove.FrameTransform message.</summary>
    [Unity.FoxgloveSDK.Protocol.FoxgloveSchema("foxglove.FrameTransform")]
    public class FrameTransformMessage
    {
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        [JsonProperty("parent_frame_id")] public string ParentFrameId { get; set; }
        [JsonProperty("child_frame_id")] public string ChildFrameId { get; set; }
        [JsonProperty("translation")] public FoxgloveVector3 Translation { get; set; }
        [JsonProperty("rotation")] public FoxgloveQuaternion Rotation { get; set; }
    }

    // ── SceneUpdate ──

    /// <summary>foxglove.SceneUpdate message.</summary>
    [Unity.FoxgloveSDK.Protocol.FoxgloveSchema("foxglove.SceneUpdate")]
    public class SceneUpdateMessage
    {
        [JsonProperty("deletions")] public List<SceneEntityDeletion> Deletions { get; set; } = new List<SceneEntityDeletion>();
        [JsonProperty("entities")] public List<SceneEntity> Entities { get; set; } = new List<SceneEntity>();
    }

    /// <summary>SceneEntity deletion command.</summary>
    public class SceneEntityDeletion
    {
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        [JsonProperty("type")] public SceneEntityDeletionType Type { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
    }

    /// <summary>SceneEntityDeletion type enum. Serialized as integer per official v1 spec.</summary>
    public enum SceneEntityDeletionType
    {
        MatchingId = 0,
        All = 1
    }

    /// <summary>A visual element in a 3D scene.</summary>
    public class SceneEntity
    {
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        [JsonProperty("frame_id")] public string FrameId { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("lifetime")] public FoxgloveDuration Lifetime { get; set; }
        [JsonProperty("frame_locked")] public bool FrameLocked { get; set; }
        [JsonProperty("metadata")] public List<FoxgloveKeyValuePair> Metadata { get; set; } = new List<FoxgloveKeyValuePair>();

        // Phase 3: only CubePrimitive is typed; all others are empty arrays
        [JsonProperty("arrows")] public List<object> Arrows { get; set; } = new List<object>();
        [JsonProperty("cubes")] public List<CubePrimitive> Cubes { get; set; } = new List<CubePrimitive>();
        [JsonProperty("spheres")] public List<object> Spheres { get; set; } = new List<object>();
        [JsonProperty("cylinders")] public List<object> Cylinders { get; set; } = new List<object>();
        [JsonProperty("lines")] public List<object> Lines { get; set; } = new List<object>();
        [JsonProperty("triangles")] public List<object> Triangles { get; set; } = new List<object>();
        [JsonProperty("texts")] public List<object> Texts { get; set; } = new List<object>();
        [JsonProperty("models")] public List<object> Models { get; set; } = new List<object>();
    }

    /// <summary>Cube or rectangular prism primitive.</summary>
    public class CubePrimitive
    {
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        [JsonProperty("size")] public FoxgloveVector3 Size { get; set; }
        [JsonProperty("color")] public FoxgloveColor Color { get; set; }
    }
}
