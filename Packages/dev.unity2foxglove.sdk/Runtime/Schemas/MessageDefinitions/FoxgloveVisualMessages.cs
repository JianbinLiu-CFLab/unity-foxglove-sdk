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

    /// <summary>3D point.</summary>
    public class FoxglovePoint3
    {
        /// <summary>X coordinate.</summary>
        [JsonProperty("x")] public double X { get; set; }
        /// <summary>Y coordinate.</summary>
        [JsonProperty("y")] public double Y { get; set; }
        /// <summary>Z coordinate.</summary>
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

        /// <summary>Arrow primitives.</summary>
        [JsonProperty("arrows")] public List<ArrowPrimitive> Arrows { get; set; } = new List<ArrowPrimitive>();
        /// <summary>Cube or rectangular prism primitives.</summary>
        [JsonProperty("cubes")] public List<CubePrimitive> Cubes { get; set; } = new List<CubePrimitive>();
        /// <summary>Sphere primitives.</summary>
        [JsonProperty("spheres")] public List<SpherePrimitive> Spheres { get; set; } = new List<SpherePrimitive>();
        /// <summary>Cylinder primitives.</summary>
        [JsonProperty("cylinders")] public List<CylinderPrimitive> Cylinders { get; set; } = new List<CylinderPrimitive>();
        /// <summary>Line primitives.</summary>
        [JsonProperty("lines")] public List<LinePrimitive> Lines { get; set; } = new List<LinePrimitive>();
        /// <summary>Triangle list primitives.</summary>
        [JsonProperty("triangles")] public List<TriangleListPrimitive> Triangles { get; set; } = new List<TriangleListPrimitive>();
        /// <summary>Text primitives.</summary>
        [JsonProperty("texts")] public List<TextPrimitive> Texts { get; set; } = new List<TextPrimitive>();
        /// <summary>Model primitives.</summary>
        [JsonProperty("models")] public List<ModelPrimitive> Models { get; set; } = new List<ModelPrimitive>();
    }

    /// <summary>Arrow primitive.</summary>
    public class ArrowPrimitive
    {
        /// <summary>Tail pose and orientation.</summary>
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        /// <summary>Length of the arrow shaft.</summary>
        [JsonProperty("shaft_length")] public double ShaftLength { get; set; }
        /// <summary>Diameter of the arrow shaft.</summary>
        [JsonProperty("shaft_diameter")] public double ShaftDiameter { get; set; }
        /// <summary>Length of the arrow head.</summary>
        [JsonProperty("head_length")] public double HeadLength { get; set; }
        /// <summary>Diameter of the arrow head.</summary>
        [JsonProperty("head_diameter")] public double HeadDiameter { get; set; }
        /// <summary>Arrow color.</summary>
        [JsonProperty("color")] public FoxgloveColor Color { get; set; }
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

    /// <summary>Sphere or ellipsoid primitive.</summary>
    public class SpherePrimitive
    {
        /// <summary>Center pose and orientation.</summary>
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        /// <summary>Diameter along each axis.</summary>
        [JsonProperty("size")] public FoxgloveVector3 Size { get; set; }
        /// <summary>Sphere color.</summary>
        [JsonProperty("color")] public FoxgloveColor Color { get; set; }
    }

    /// <summary>Cylinder, elliptic cylinder, or truncated cone primitive.</summary>
    public class CylinderPrimitive
    {
        /// <summary>Center pose and orientation.</summary>
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        /// <summary>Bounding box size.</summary>
        [JsonProperty("size")] public FoxgloveVector3 Size { get; set; }
        /// <summary>Bottom face scale.</summary>
        [JsonProperty("bottom_scale")] public double BottomScale { get; set; }
        /// <summary>Top face scale.</summary>
        [JsonProperty("top_scale")] public double TopScale { get; set; }
        /// <summary>Cylinder color.</summary>
        [JsonProperty("color")] public FoxgloveColor Color { get; set; }
    }

    /// <summary>Line primitive point interpretation.</summary>
    public enum LinePrimitiveType
    {
        LineStrip = 0,
        LineLoop = 1,
        LineList = 2
    }

    /// <summary>Line primitive.</summary>
    public class LinePrimitive
    {
        /// <summary>How points are connected.</summary>
        [JsonProperty("type")] public LinePrimitiveType Type { get; set; }
        /// <summary>Origin of line points relative to the reference frame.</summary>
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        /// <summary>Line thickness.</summary>
        [JsonProperty("thickness")] public double Thickness { get; set; }
        /// <summary>Whether thickness is fixed in screen pixels.</summary>
        [JsonProperty("scale_invariant")] public bool ScaleInvariant { get; set; }
        /// <summary>Line vertices.</summary>
        [JsonProperty("points")] public List<FoxglovePoint3> Points { get; set; } = new List<FoxglovePoint3>();
        /// <summary>Fallback line color.</summary>
        [JsonProperty("color")] public FoxgloveColor Color { get; set; }
        /// <summary>Per-point colors.</summary>
        [JsonProperty("colors")] public List<FoxgloveColor> Colors { get; set; } = new List<FoxgloveColor>();
        /// <summary>Optional point indices.</summary>
        [JsonProperty("indices")] public List<uint> Indices { get; set; } = new List<uint>();
    }

    /// <summary>Triangle list primitive.</summary>
    public class TriangleListPrimitive
    {
        /// <summary>Origin of triangle points relative to the reference frame.</summary>
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        /// <summary>Triangle vertices.</summary>
        [JsonProperty("points")] public List<FoxglovePoint3> Points { get; set; } = new List<FoxglovePoint3>();
        /// <summary>Fallback triangle color.</summary>
        [JsonProperty("color")] public FoxgloveColor Color { get; set; }
        /// <summary>Per-vertex colors.</summary>
        [JsonProperty("colors")] public List<FoxgloveColor> Colors { get; set; } = new List<FoxgloveColor>();
        /// <summary>Optional point indices.</summary>
        [JsonProperty("indices")] public List<uint> Indices { get; set; } = new List<uint>();
    }

    /// <summary>Text label primitive.</summary>
    public class TextPrimitive
    {
        /// <summary>Text box pose.</summary>
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        /// <summary>Whether text always faces the camera.</summary>
        [JsonProperty("billboard")] public bool Billboard { get; set; }
        /// <summary>Font size.</summary>
        [JsonProperty("font_size")] public double FontSize { get; set; }
        /// <summary>Whether font size is fixed in screen pixels.</summary>
        [JsonProperty("scale_invariant")] public bool ScaleInvariant { get; set; }
        /// <summary>Text color.</summary>
        [JsonProperty("color")] public FoxgloveColor Color { get; set; }
        /// <summary>Text content.</summary>
        [JsonProperty("text")] public string Text { get; set; }
    }

    /// <summary>3D model primitive.</summary>
    public class ModelPrimitive
    {
        /// <summary>Model pose.</summary>
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        /// <summary>Model scale.</summary>
        [JsonProperty("scale")] public FoxgloveVector3 Scale { get; set; }
        /// <summary>Override color.</summary>
        [JsonProperty("color")] public FoxgloveColor Color { get; set; }
        /// <summary>Whether to override embedded model colors.</summary>
        [JsonProperty("override_color")] public bool OverrideColor { get; set; }
        /// <summary>Model URL.</summary>
        [JsonProperty("url")] public string Url { get; set; }
        /// <summary>Embedded model media type.</summary>
        [JsonProperty("media_type")] public string MediaType { get; set; }
        /// <summary>Embedded model bytes.</summary>
        [JsonProperty("data")] public byte[] Data { get; set; }
    }
}
