// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Resolves sensor camera profile topics, frame ids, and ROS image payloads.

using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Small adapter for optional sensor-camera profile values used by the camera publisher.
    /// </summary>
    internal static class CameraSensorProfileResolver
    {
        private const string DefaultFrameId = "unity_camera";
        private const string DefaultImageTopic = "/unity/sensor/camera/image/compressed";

        public static ISensorCameraProfile ResolveProfile(object sensorUnitProfile)
            => sensorUnitProfile as ISensorCameraProfile;

        public static string ResolveFrameId(object sensorUnitProfile, string fallbackFrameId)
        {
            var profile = ResolveProfile(sensorUnitProfile);
            return profile != null
                ? profile.CameraFrameId
                : (string.IsNullOrWhiteSpace(fallbackFrameId) ? DefaultFrameId : fallbackFrameId);
        }

        public static string ResolveImageTopic(object sensorUnitProfile, string fallbackTopic)
        {
            var profile = ResolveProfile(sensorUnitProfile);
            return profile != null
                ? profile.CameraImageTopic
                : (string.IsNullOrWhiteSpace(fallbackTopic) ? DefaultImageTopic : fallbackTopic);
        }

        public static void ApplyDefaults(
            object sensorUnitProfile,
            bool publishStandardRos2CompressedImage,
            string activeDefaultTopic,
            ref string topic,
            ref string frameId)
        {
            var profile = ResolveProfile(sensorUnitProfile);
            if (profile == null || !publishStandardRos2CompressedImage)
                return;

            if (string.IsNullOrWhiteSpace(topic) || topic == activeDefaultTopic)
                topic = profile.CameraImageTopic;
            if (string.IsNullOrWhiteSpace(frameId) || frameId == DefaultFrameId)
                frameId = profile.CameraFrameId;
        }

        public static bool HasCompressedImageDemand(bool standardRos2CompressedImageOutput, bool hasSubscribers)
            => standardRos2CompressedImageOutput && hasSubscribers;

        public static byte[] SerializeCompressedImage(
            bool publishStandardRos2CompressedImage,
            ulong unixNs,
            string frameId,
            byte[] jpeg,
            string format)
            => publishStandardRos2CompressedImage
                ? Ros2CdrSensorCompressedImageBuilder.Serialize(unixNs, frameId, jpeg, format)
                : Ros2CdrCompressedImageBuilder.Serialize(unixNs, frameId, jpeg, format);
    }
}
