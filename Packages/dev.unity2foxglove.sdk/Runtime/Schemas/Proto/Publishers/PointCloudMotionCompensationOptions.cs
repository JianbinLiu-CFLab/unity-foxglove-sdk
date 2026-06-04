// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: PointCloud2 visualization motion-compensation configuration.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>How PointCloud2 Native output should route raw and deskewed frames.</summary>
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

    /// <summary>Resolved PointCloud2 motion-compensation settings.</summary>
    internal readonly struct PointCloudMotionCompensationSettings
    {
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

        public bool Enabled { get; }
        public PointCloudMotionCompensationOutputPolicy OutputPolicy { get; }
        public string DeskewedTopic { get; }
        public PointCloudMotionCompensationReferenceTime ReferenceTime { get; }
        public PointCloudMotionCompensationSource MotionSource { get; }
        public bool PreserveRawOutput => !Enabled || OutputPolicy != PointCloudMotionCompensationOutputPolicy.ReplaceOutput;
        public bool EmitDeskewedOutput => Enabled && OutputPolicy != PointCloudMotionCompensationOutputPolicy.RawOnly;

        public string ResolveDeskewedTopic(string rawTopic)
        {
            if (OutputPolicy == PointCloudMotionCompensationOutputPolicy.ReplaceOutput)
                return NormalizeTopic(rawTopic, PointCloudOutputProfile.ForMode(PointCloudOutputMode.PointCloud2Native).DefaultTopic);

            return DeskewedTopic;
        }

        public bool IsLikelySlamReplacementTopic(string rawTopic)
            => Enabled
               && OutputPolicy == PointCloudMotionCompensationOutputPolicy.ReplaceOutput
               && PointCloudMotionCompensationOptions.IsLikelySlamInputTopic(rawTopic);

        private static string NormalizeTopic(string topic, string fallback)
            => PointCloudMotionCompensationOptions.NormalizeTopic(topic, fallback);
    }

    /// <summary>Validation and default helpers for PointCloud2 motion compensation.</summary>
    internal static class PointCloudMotionCompensationOptions
    {
        public const string DefaultDeskewedTopic = "/unity/point_cloud2_deskewed";

        public static PointCloudMotionCompensationSettings CreateDefault()
            => new PointCloudMotionCompensationSettings(
                enabled: false,
                outputPolicy: PointCloudMotionCompensationOutputPolicy.RawAndDeskewedTopic,
                deskewedTopic: DefaultDeskewedTopic,
                referenceTime: PointCloudMotionCompensationReferenceTime.ScanStart,
                motionSource: PointCloudMotionCompensationSource.SensorTransform);

        public static string NormalizeTopic(string topic, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(topic) ? fallback : topic.Trim();
            if (string.IsNullOrWhiteSpace(value))
                value = DefaultDeskewedTopic;
            return value[0] == '/' ? value : "/" + value;
        }

        public static bool IsLikelySlamInputTopic(string topic)
        {
            var value = NormalizeTopic(topic, "");
            return string.Equals(value, "/unity/point_cloud2", StringComparison.Ordinal)
                   || string.Equals(value, "/points", StringComparison.Ordinal)
                   || value.EndsWith("/points", StringComparison.Ordinal);
        }
    }
}
