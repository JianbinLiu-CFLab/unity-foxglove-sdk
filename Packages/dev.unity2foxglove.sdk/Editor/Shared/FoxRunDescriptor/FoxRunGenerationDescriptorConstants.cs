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
        public const int DescriptorVersion = 5;
        // Descriptor/generator format version, not the package release version.
        // Bump when descriptor JSON changes in a backward-incompatible way.
        public const string GeneratorVersion = "5.0.0";
        public const string InheritEncoding = "inherit";
        public const string ProtobufEncoding = "protobuf";
        public const string JsonEncoding = "json";
        public const string MessagePackEncoding = "msgpack";
        public const string InheritSource = "inherit";
        public const string FoxgloveWebSocketSource = "foxglove-websocket";
        public const string Ros2NativeSource = "ros2-native";
        public const string InheritTargets = "inherit";
        public const string FoxgloveTarget = "foxglove";
        public const string Ros2NativeTarget = "ros2-native";
        public const string Ros2BridgeTarget = "ros2-bridge";
        public const string InheritQosProfile = "inherit";
        public const string DefaultQosProfile = "default";
        public const string SensorDataQosProfile = "sensor-data";
        public const string SystemDefaultQosProfile = "system-default";
        public const string InheritQosPolicy = "inherit";
        public const string SystemDefaultQosPolicy = "system-default";
        public const string ReliableQosReliability = "reliable";
        public const string BestEffortQosReliability = "best-effort";
        public const string VolatileQosDurability = "volatile";
        public const string TransientLocalQosDurability = "transient-local";
        public const string KeepLastQosHistory = "keep-last";
        public const string KeepAllQosHistory = "keep-all";
        public const string DescriptorFileName = "foxrun.generation-descriptor.json";
    }
}
