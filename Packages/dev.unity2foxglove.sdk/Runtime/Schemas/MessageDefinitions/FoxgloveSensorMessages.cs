// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/MessageDefinitions
// Purpose: Foxglove sensor schema DTOs for PointCloud, LaserScan, and CameraCalibration.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Schemas
{
    /// <summary>Foxglove packed binary field descriptor.</summary>
    public class PackedElementFieldMessage
    {
        /// <summary>Field name inside a packed point or cell.</summary>
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>Byte offset from the start of the packed element.</summary>
        [JsonProperty("offset")] public uint Offset { get; set; }
        /// <summary>Foxglove numeric type enum value.</summary>
        [JsonProperty("type")] public int Type { get; set; }
    }

    /// <summary>foxglove.PointCloud JSON message.</summary>
    [Unity.FoxgloveSDK.Protocol.FoxgloveSchema("foxglove.PointCloud")]
    public class PointCloudMessage
    {
        /// <summary>Timestamp of the point cloud.</summary>
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        /// <summary>Frame of reference.</summary>
        [JsonProperty("frame_id")] public string FrameId { get; set; }
        /// <summary>Point cloud origin relative to the frame.</summary>
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        /// <summary>Number of bytes per point.</summary>
        [JsonProperty("point_stride")] public uint PointStride { get; set; }
        /// <summary>Fields packed into each point.</summary>
        [JsonProperty("fields")] public List<PackedElementFieldMessage> Fields { get; set; } = new List<PackedElementFieldMessage>();
        /// <summary>Base64-encoded packed point bytes.</summary>
        [JsonProperty("data")] public string Data { get; set; }
    }

    /// <summary>foxglove.LaserScan JSON message.</summary>
    [Unity.FoxgloveSDK.Protocol.FoxgloveSchema("foxglove.LaserScan")]
    public class LaserScanMessage
    {
        /// <summary>Timestamp of the scan.</summary>
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        /// <summary>Frame of reference.</summary>
        [JsonProperty("frame_id")] public string FrameId { get; set; }
        /// <summary>Scan origin relative to the frame.</summary>
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        /// <summary>Bearing of the first point, in radians.</summary>
        [JsonProperty("start_angle")] public double StartAngle { get; set; }
        /// <summary>Bearing of the last point, in radians.</summary>
        [JsonProperty("end_angle")] public double EndAngle { get; set; }
        /// <summary>Range values in meters.</summary>
        [JsonProperty("ranges")] public List<double> Ranges { get; set; } = new List<double>();
        /// <summary>Optional intensity values.</summary>
        [JsonProperty("intensities")] public List<double> Intensities { get; set; } = new List<double>();
    }

    /// <summary>foxglove.CameraCalibration JSON message.</summary>
    [Unity.FoxgloveSDK.Protocol.FoxgloveSchema("foxglove.CameraCalibration")]
    public class CameraCalibrationMessage
    {
        /// <summary>Timestamp of calibration data.</summary>
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        /// <summary>Camera optical frame.</summary>
        [JsonProperty("frame_id")] public string FrameId { get; set; }
        /// <summary>Image width in pixels.</summary>
        [JsonProperty("width")] public uint Width { get; set; }
        /// <summary>Image height in pixels.</summary>
        [JsonProperty("height")] public uint Height { get; set; }
        /// <summary>Distortion model name.</summary>
        [JsonProperty("distortion_model")] public string DistortionModel { get; set; }
        /// <summary>Distortion coefficients.</summary>
        [JsonProperty("D")] public List<double> D { get; set; } = new List<double>();
        /// <summary>3x3 intrinsic camera matrix.</summary>
        [JsonProperty("K")] public List<double> K { get; set; } = new List<double>();
        /// <summary>3x3 rectification matrix.</summary>
        [JsonProperty("R")] public List<double> R { get; set; } = new List<double>();
        /// <summary>3x4 projection matrix.</summary>
        [JsonProperty("P")] public List<double> P { get; set; } = new List<double>();
    }

    /// <summary>
    /// One point in a generated point cloud frame. Value type (struct) so dense
    /// scans store points in a contiguous buffer with no per-point heap allocation.
    /// Optional fields use sentinels (<see cref="float.NaN"/> / <see cref="ushort.MaxValue"/>)
    /// instead of nullables to stay blittable-friendly; use the Has* properties to test presence.
    /// </summary>
    public struct PointCloudPoint
    {
        /// <summary>Create a point with required XYZ fields; optional fields default to absent.</summary>
        public PointCloudPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
            Intensity = float.NaN;
            Reflectivity = float.NaN;
            TimeOffsetSeconds = float.NaN;
            Ring = ushort.MaxValue;
        }

        /// <summary>X coordinate.</summary>
        public float X { get; set; }
        /// <summary>Y coordinate.</summary>
        public float Y { get; set; }
        /// <summary>Z coordinate.</summary>
        public float Z { get; set; }
        /// <summary>Optional intensity value; <see cref="float.NaN"/> means absent.</summary>
        public float Intensity { get; set; }
        /// <summary>Optional reflectivity value; <see cref="float.NaN"/> means absent.</summary>
        public float Reflectivity { get; set; }
        /// <summary>Optional laser ring/channel index; <see cref="ushort.MaxValue"/> means absent.</summary>
        public ushort Ring { get; set; }
        /// <summary>Optional per-point time offset in seconds; <see cref="float.NaN"/> means absent.</summary>
        public float TimeOffsetSeconds { get; set; }

        /// <summary>Whether an intensity value is present.</summary>
        public bool HasIntensity => !float.IsNaN(Intensity);
        /// <summary>Whether a reflectivity value is present.</summary>
        public bool HasReflectivity => !float.IsNaN(Reflectivity);
        /// <summary>Whether a ring/channel index is present.</summary>
        public bool HasRing => Ring != ushort.MaxValue;
        /// <summary>Whether a per-point time offset is present.</summary>
        public bool HasTimeOffset => !float.IsNaN(TimeOffsetSeconds);
    }

    /// <summary>Input frame for building PointCloud JSON/protobuf payloads.</summary>
    public class PointCloudFrame
    {
        /// <summary>Unix timestamp in nanoseconds.</summary>
        public ulong UnixNs { get; set; }
        /// <summary>Frame of reference.</summary>
        public string FrameId { get; set; }
        /// <summary>
        /// Optional valid point count from native pipelines. A value below 0 means
        /// use <see cref="Points"/>.Count.
        /// </summary>
        public int ValidCount { get; set; } = -1;
        /// <summary>Points to pack.</summary>
        public List<PointCloudPoint> Points { get; } = new List<PointCloudPoint>();
    }
}
