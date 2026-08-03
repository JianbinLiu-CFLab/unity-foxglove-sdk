// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Resolves sensor camera profile topics and frame ids.

using System;
using Unity.FoxgloveSDK.Schemas.Camera;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Small adapter for optional sensor-camera profile values used by the camera publisher.
    /// </summary>
    internal static class CameraSensorProfileResolver
    {
        private const string DefaultFrameId = "unity_camera";
        private const string DefaultCompressedImageTopic = "/unity/sensor/camera/image/compressed";
        private const string DefaultRawImageTopic = "/unity/sensor/camera/image";

        public static ISensorCameraProfile ResolveProfile(object sensorUnitProfile)
            => sensorUnitProfile as ISensorCameraProfile;

        public static string ResolveFrameId(object sensorUnitProfile, string fallbackFrameId)
        {
            var profile = ResolveProfile(sensorUnitProfile);
            return profile != null
                ? profile.CameraFrameId
                : (string.IsNullOrWhiteSpace(fallbackFrameId) ? DefaultFrameId : fallbackFrameId);
        }

        public static string ResolveCompressedImageTopic(object sensorUnitProfile, string fallbackTopic)
        {
            var profile = ResolveProfile(sensorUnitProfile);
            return profile != null
                ? profile.CameraImageTopic
                : (string.IsNullOrWhiteSpace(fallbackTopic) ? DefaultCompressedImageTopic : fallbackTopic);
        }

        /// <summary>
        /// Resolves the compressed image topic kept for the canonical camera publisher alias.
        /// Use <see cref="ResolveRawImageTopic"/> when a raw Provider image topic is required.
        /// </summary>
        public static string ResolveImageTopic(object sensorUnitProfile, string fallbackTopic)
            => ResolveCompressedImageTopic(sensorUnitProfile, fallbackTopic);

        public static string ResolveRawImageTopic(object sensorUnitProfile, string fallbackTopic)
        {
            var profile = ResolveProfile(sensorUnitProfile);
            var source = profile != null
                ? profile.CameraImageTopic
                : null;

            if (!string.IsNullOrWhiteSpace(source)
                && source.EndsWith("/compressed", StringComparison.OrdinalIgnoreCase))
            {
                source = source.Substring(0, source.Length - "/compressed".Length);
            }

            return string.IsNullOrWhiteSpace(source)
                ? (string.IsNullOrWhiteSpace(fallbackTopic) ? DefaultRawImageTopic : fallbackTopic)
                : source;
        }

        public static void ApplyDefaults(
            object sensorUnitProfile,
            string activeDefaultTopic,
            string activeRawDefaultTopic,
            ref string topic,
            ref string rawTopic,
            ref string frameId)
        {
            var profile = ResolveProfile(sensorUnitProfile);
            if (profile == null)
                return;

            if (string.IsNullOrWhiteSpace(topic) || topic == activeDefaultTopic)
            {
                topic = ResolveCompressedImageTopic(profile, activeDefaultTopic);
            }

            if (string.IsNullOrWhiteSpace(rawTopic) || rawTopic == activeRawDefaultTopic)
            {
                rawTopic = ResolveRawImageTopic(profile, activeRawDefaultTopic);
            }

            if (string.IsNullOrWhiteSpace(frameId) || frameId == DefaultFrameId)
                frameId = profile.CameraFrameId;
        }

        public static bool HasCompressedImageDemand(bool hasSubscribers)
            => hasSubscribers;

        public static bool HasRawImageDemand(bool hasSubscribers)
            => hasSubscribers;

    }
}
