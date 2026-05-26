// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/MessageDefinitions
// Purpose: Foxglove visual schema DTOs — FrameTransform, SceneUpdate,
// and related geometry types for 3D visualization.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Unity.FoxgloveSDK.Schemas
{
    /// <summary>Foxglove time: { sec, nsec }.</summary>
    public class FoxgloveTime
    {
        private uint _nsec;

        /// <summary>Whole seconds.</summary>
        [JsonProperty("sec")] public ulong Sec { get; set; }
        /// <summary>Nanoseconds fraction. Values above one second are normalized into <see cref="Sec"/>.</summary>
        [JsonProperty("nsec")]
        public uint Nsec
        {
            get => _nsec;
            set
            {
                var carry = value / 1_000_000_000U;
                if (carry != 0)
                {
                    if (Sec > ulong.MaxValue - carry)
                        throw new ArgumentOutOfRangeException(nameof(value), "Nanoseconds overflow timestamp seconds.");

                    Sec += carry;
                }

                _nsec = value % 1_000_000_000U;
            }
        }
    }

    /// <summary>Foxglove duration: { sec, nsec }.</summary>
    public class FoxgloveDuration
    {
        private uint _nsec;

        /// <summary>Whole seconds.</summary>
        [JsonProperty("sec")] public long Sec { get; set; }
        /// <summary>Nanoseconds fraction. Values above one second are normalized into <see cref="Sec"/>.</summary>
        [JsonProperty("nsec")]
        public uint Nsec
        {
            get => _nsec;
            set
            {
                var carry = value / 1_000_000_000U;
                if (carry != 0)
                {
                    if (Sec > long.MaxValue - carry)
                        throw new ArgumentOutOfRangeException(nameof(value), "Nanoseconds overflow duration seconds.");

                    Sec += carry;
                }

                _nsec = value % 1_000_000_000U;
            }
        }
    }

    /// <summary>3D vector.</summary>
    public class FoxgloveVector3
    {
        /// <summary>X component.</summary>
        [JsonProperty("x")] public double X { get; set; }
        /// <summary>Y component.</summary>
        [JsonProperty("y")] public double Y { get; set; }
        /// <summary>Z component.</summary>
        [JsonProperty("z")] public double Z { get; set; }
    }

    /// <summary>Quaternion orientation.</summary>
    public class FoxgloveQuaternion
    {
        /// <summary>X component.</summary>
        [JsonProperty("x")] public double X { get; set; }
        /// <summary>Y component.</summary>
        [JsonProperty("y")] public double Y { get; set; }
        /// <summary>Z component.</summary>
        [JsonProperty("z")] public double Z { get; set; }
        /// <summary>W component.</summary>
        [JsonProperty("w")] public double W { get; set; }
    }

    /// <summary>Pose: position + orientation.</summary>
    public class FoxglovePose
    {
        /// <summary>Position in 3D space.</summary>
        [JsonProperty("position")] public FoxgloveVector3 Position { get; set; }
        /// <summary>Orientation as a quaternion.</summary>
        [JsonProperty("orientation")] public FoxgloveQuaternion Orientation { get; set; }
    }

    /// <summary>RGBA color, 0 to 1 range.</summary>
    public class FoxgloveColor
    {
        /// <summary>Red channel (0-1).</summary>
        [JsonProperty("r")] public double R { get; set; }
        /// <summary>Green channel (0-1).</summary>
        [JsonProperty("g")] public double G { get; set; }
        /// <summary>Blue channel (0-1).</summary>
        [JsonProperty("b")] public double B { get; set; }
        /// <summary>Alpha channel (0-1).</summary>
        [JsonProperty("a")] public double A { get; set; }
    }

    /// <summary>Key-value string pair.</summary>
    public class FoxgloveKeyValuePair
    {
        /// <summary>Key.</summary>
        [JsonProperty("key")] public string Key { get; set; }
        /// <summary>Value.</summary>
        [JsonProperty("value")] public string Value { get; set; }
    }

    // ── FrameTransform ──

    /// <summary>foxglove.FrameTransform message.</summary>
    [Unity.FoxgloveSDK.Protocol.FoxgloveSchema("foxglove.FrameTransform")]
    public class FrameTransformMessage
    {
        /// <summary>Timestamp of the transform.</summary>
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        /// <summary>Name of the parent reference frame.</summary>
        [JsonProperty("parent_frame_id")] public string ParentFrameId { get; set; }
        /// <summary>Name of the child frame.</summary>
        [JsonProperty("child_frame_id")] public string ChildFrameId { get; set; }
        /// <summary>Translation from parent to child origin.</summary>
        [JsonProperty("translation")] public FoxgloveVector3 Translation { get; set; }
        /// <summary>Rotation from parent to child orientation.</summary>
        [JsonProperty("rotation")] public FoxgloveQuaternion Rotation { get; set; }
    }

    // ── SceneUpdate ──

    /// <summary>foxglove.SceneUpdate message.</summary>
    [Unity.FoxgloveSDK.Protocol.FoxgloveSchema("foxglove.SceneUpdate")]
    public class SceneUpdateMessage
    {
        /// <summary>Entities to delete.</summary>
        [JsonProperty("deletions")] public List<SceneEntityDeletion> Deletions { get; set; } = new List<SceneEntityDeletion>();
        /// <summary>Entities to add or update.</summary>
        [JsonProperty("entities")] public List<SceneEntity> Entities { get; set; } = new List<SceneEntity>();
    }

    /// <summary>SceneEntity deletion command.</summary>
    public class SceneEntityDeletion
    {
        /// <summary>Timestamp of the deletion.</summary>
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        /// <summary>Type of deletion action.</summary>
        [JsonProperty("type")] public SceneEntityDeletionType Type { get; set; }
        /// <summary>Entity ID to match (required for MatchingId type).</summary>
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
        /// <summary>Timestamp of the entity.</summary>
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        /// <summary>Frame of reference for the entity.</summary>
        [JsonProperty("frame_id")] public string FrameId { get; set; }
        /// <summary>Entity identifier (replaces prior entity with same id).</summary>
        [JsonProperty("id")] public string Id { get; set; }
        /// <summary>Duration after which the entity should be automatically removed.</summary>
        [JsonProperty("lifetime")] public FoxgloveDuration Lifetime { get; set; }
        /// <summary>Whether the entity follows its frame as it moves.</summary>
        [JsonProperty("frame_locked")] public bool FrameLocked { get; set; }
        /// <summary>User-provided metadata key-value pairs.</summary>
        [JsonProperty("metadata")] public List<FoxgloveKeyValuePair> Metadata { get; set; } = new List<FoxgloveKeyValuePair>();

        // Phase 3: only CubePrimitive is typed; all others are empty arrays
        /// <summary>Arrow primitives.</summary>
        [JsonProperty("arrows")] public List<object> Arrows { get; set; } = new List<object>();
        /// <summary>Cube or rectangular prism primitives.</summary>
        [JsonProperty("cubes")] public List<CubePrimitive> Cubes { get; set; } = new List<CubePrimitive>();
        /// <summary>Sphere primitives.</summary>
        [JsonProperty("spheres")] public List<object> Spheres { get; set; } = new List<object>();
        /// <summary>Cylinder primitives.</summary>
        [JsonProperty("cylinders")] public List<object> Cylinders { get; set; } = new List<object>();
        /// <summary>Line primitives.</summary>
        [JsonProperty("lines")] public List<object> Lines { get; set; } = new List<object>();
        /// <summary>Triangle list primitives.</summary>
        [JsonProperty("triangles")] public List<object> Triangles { get; set; } = new List<object>();
        /// <summary>Text primitives.</summary>
        [JsonProperty("texts")] public List<object> Texts { get; set; } = new List<object>();
        /// <summary>Model primitives.</summary>
        [JsonProperty("models")] public List<object> Models { get; set; } = new List<object>();
    }

    /// <summary>Cube or rectangular prism primitive.</summary>
    public class CubePrimitive
    {
        /// <summary>Position and orientation of the cube.</summary>
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        /// <summary>Size of the cube along each axis.</summary>
        [JsonProperty("size")] public FoxgloveVector3 Size { get; set; }
        /// <summary>Color of the cube.</summary>
        [JsonProperty("color")] public FoxgloveColor Color { get; set; }
    }
}
