// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: PackedPointCloud visualization motion-compensation configuration.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>How PackedPointCloud Native output should route raw and deskewed frames.</summary>
    public enum PointCloudMotionCompensationOutputPolicy
    {
        /// <summary>Publish only the unchanged raw point cloud.</summary>
        RawOnly,
        /// <summary>Publish raw output normally and emit a second deskewed visualization topic.</summary>
        RawAndDeskewedTopic,
        /// <summary>Publish deskewed visualization data on the normal point-cloud topic.</summary>
        ReplaceOutput
    }

    /// <summary>Reference time used when re-expressing rolling scan points.</summary>
    public enum PointCloudMotionCompensationReferenceTime
    {
        /// <summary>Use the scan start timestamp.</summary>
        ScanStart,
        /// <summary>Use the midpoint between first and last point timestamp.</summary>
        ScanMidpoint,
        /// <summary>Use the final point timestamp.</summary>
        ScanEnd
    }

    /// <summary>Motion source used for visualization deskew.</summary>
    public enum PointCloudMotionCompensationSource
    {
        /// <summary>Sample the publisher or sensor unit transform on the Unity main thread.</summary>
        SensorTransform
    }

    /// <summary>Resolved PackedPointCloud motion-compensation settings.</summary>
    internal readonly struct PointCloudMotionCompensationSettings
    {
        /// <summary>Create resolved PackedPointCloud motion-compensation settings.</summary>
        public PointCloudMotionCompensationSettings(
            bool enabled,
            PointCloudMotionCompensationOutputPolicy outputPolicy,
            string deskewedTopic,
            PointCloudMotionCompensationReferenceTime referenceTime,
            PointCloudMotionCompensationSource motionSource)
        {
            Enabled = enabled;
            OutputPolicy = outputPolicy;
            DeskewedTopic = NormalizeTopic(deskewedTopic, PointCloudMotionCompensationOptions.DefaultDeskewedTopic);
            ReferenceTime = referenceTime;
            MotionSource = motionSource;
        }

        /// <summary>True when motion-compensated visualization output is enabled.</summary>
        public bool Enabled { get; }

        /// <summary>Routing policy for raw and deskewed PackedPointCloud Native frames.</summary>
        public PointCloudMotionCompensationOutputPolicy OutputPolicy { get; }

        /// <summary>Normalized topic used for the separate deskewed visualization stream.</summary>
        public string DeskewedTopic { get; }

        /// <summary>Reference timestamp used when deskewed points are expressed in one frame.</summary>
        public PointCloudMotionCompensationReferenceTime ReferenceTime { get; }

        /// <summary>Main-thread motion source used to sample poses for deskew.</summary>
        public PointCloudMotionCompensationSource MotionSource { get; }

        /// <summary>True when the raw PackedPointCloud stream should still be emitted.</summary>
        public bool PreserveRawOutput => !Enabled || OutputPolicy != PointCloudMotionCompensationOutputPolicy.ReplaceOutput;

        /// <summary>True when a deskewed PackedPointCloud visualization frame should be emitted.</summary>
        public bool EmitDeskewedOutput => Enabled && OutputPolicy != PointCloudMotionCompensationOutputPolicy.RawOnly;

        /// <summary>Resolve the effective deskewed output topic for the current raw topic.</summary>
        public string ResolveDeskewedTopic(string rawTopic)
        {
            if (OutputPolicy == PointCloudMotionCompensationOutputPolicy.ReplaceOutput)
                return NormalizeTopic(rawTopic, PointCloudOutputProfile.ForMode(PointCloudOutputMode.PackedPointCloud).DefaultTopic);

            return DeskewedTopic;
        }

        /// <summary>True when replacing the raw topic would likely overwrite a SLAM input stream.</summary>
        public bool IsLikelySlamReplacementTopic(string rawTopic)
            => Enabled
               && OutputPolicy == PointCloudMotionCompensationOutputPolicy.ReplaceOutput
               && PointCloudMotionCompensationOptions.IsLikelySlamInputTopic(rawTopic);

        private static string NormalizeTopic(string topic, string fallback)
            => PointCloudMotionCompensationOptions.NormalizeTopic(topic, fallback);
    }

    /// <summary>Validation and default helpers for PackedPointCloud motion compensation.</summary>
    internal static class PointCloudMotionCompensationOptions
    {
        /// <summary>Default topic for the separate deskewed PackedPointCloud visualization stream.</summary>
        public const string DefaultDeskewedTopic = "/unity/point_cloud2_deskewed";

        /// <summary>Create default-off settings that preserve raw output when enabled later.</summary>
        public static PointCloudMotionCompensationSettings CreateDefault()
            => new PointCloudMotionCompensationSettings(
                enabled: false,
                outputPolicy: PointCloudMotionCompensationOutputPolicy.RawAndDeskewedTopic,
                deskewedTopic: DefaultDeskewedTopic,
                referenceTime: PointCloudMotionCompensationReferenceTime.ScanStart,
                motionSource: PointCloudMotionCompensationSource.SensorTransform);

        /// <summary>Normalize a ROS-style topic and fall back when the input is empty.</summary>
        public static string NormalizeTopic(string topic, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(topic) ? fallback : topic.Trim();
            if (string.IsNullOrWhiteSpace(value))
                value = DefaultDeskewedTopic;
            return value[0] == '/' ? value : "/" + value;
        }

        /// <summary>True when a topic name looks like a raw PackedPointCloud stream commonly used by SLAM.</summary>
        public static bool IsLikelySlamInputTopic(string topic)
        {
            var value = NormalizeTopic(topic, "");
            return string.Equals(value, "/unity/point_cloud2", StringComparison.Ordinal)
                   || string.Equals(value, "/points", StringComparison.Ordinal)
                   || value.EndsWith("/points", StringComparison.Ordinal);
        }
    }
}
