// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Shared constants for FoxRun generation-model descriptors.

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Version and naming constants shared by Roslyn, build-time, and tests.
    /// </summary>
    public static class FoxRunGenerationDescriptorConstants
    {
        // Integer schema version embedded in descriptor JSON and recorded MCAP
        // metadata. Bump this together with GeneratorVersion for incompatible
        // descriptor-shape changes.
        public const int DescriptorVersion = 2;
        // Descriptor/generator format version, not the package release version.
        // Bump when descriptor JSON changes in a backward-incompatible way.
        public const string GeneratorVersion = "2.0.0";
        public const string InheritEncoding = "inherit";
        public const string ProtobufEncoding = "protobuf";
        public const string JsonEncoding = "json";
        public const string InheritSubscriptionProvider = "inherit";
        public const string FoxgloveWebSocketSubscriptionProvider = "foxglove-websocket";
        public const string Ros2NativeSubscriptionProvider = "ros2-native";
        public const string InheritRos2Qos = "inherit";
        public const string DefaultRos2Qos = "default";
        public const string ReliableRos2Qos = "reliable";
        public const string SensorDataRos2Qos = "sensor-data";
        public const string TransientLocalRos2Qos = "transient-local";
        public const string DescriptorFileName = "foxrun.generation-descriptor.json";
    }
}
