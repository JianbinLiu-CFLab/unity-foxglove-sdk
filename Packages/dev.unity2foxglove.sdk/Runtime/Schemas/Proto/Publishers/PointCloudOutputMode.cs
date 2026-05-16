// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Point-cloud output mode and profile metadata for point-cloud publishers.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// User-facing point-cloud output modes supported by <see cref="FoxglovePointCloudPublisher"/>.
    /// </summary>
    public enum PointCloudOutputMode
    {
        Raw = 0,
        Draco = 1
    }

    /// <summary>
    /// Resolved point-cloud output settings for schemas, topics, and encoding support.
    /// </summary>
    public readonly struct PointCloudOutputProfile
    {
        internal PointCloudOutputProfile(
            PointCloudOutputMode mode,
            string displayName,
            string defaultTopic,
            string schemaName,
            bool supportsJson,
            bool supportsProtobuf)
        {
            Mode = mode;
            DisplayName = displayName ?? "";
            DefaultTopic = defaultTopic ?? "";
            SchemaName = schemaName ?? "";
            SupportsJson = supportsJson;
            SupportsProtobuf = supportsProtobuf;
        }

        public PointCloudOutputMode Mode { get; }
        public string DisplayName { get; }
        public string DefaultTopic { get; }
        public string SchemaName { get; }
        public bool SupportsJson { get; }
        public bool SupportsProtobuf { get; }

        public static PointCloudOutputProfile ForMode(PointCloudOutputMode mode)
        {
            switch (mode)
            {
                case PointCloudOutputMode.Draco:
                    return new PointCloudOutputProfile(
                        mode,
                        "Draco",
                        PointCloudOutputModeDefaults.DracoTopic,
                        PointCloudOutputModeDefaults.DracoSchema,
                        supportsJson: false,
                        supportsProtobuf: true);

                case PointCloudOutputMode.Raw:
                default:
                    return new PointCloudOutputProfile(
                        PointCloudOutputMode.Raw,
                        "Raw",
                        PointCloudOutputModeDefaults.RawTopic,
                        PointCloudOutputModeDefaults.RawSchema,
                        supportsJson: true,
                        supportsProtobuf: true);
            }
        }
    }

    /// <summary>
    /// Point-cloud output mode constants shared by runtime and Inspector code.
    /// </summary>
    public static class PointCloudOutputModeDefaults
    {
        public const string RawTopic = "/unity/point_cloud";
        public const string DracoTopic = "/unity/point_cloud_draco";
        public const string RawSchema = "foxglove.PointCloud";
        public const string DracoSchema = "foxglove.CompressedPointCloud";
    }
}
