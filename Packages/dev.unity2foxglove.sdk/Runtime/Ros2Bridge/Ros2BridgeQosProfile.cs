// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Preset-first ROS 2 QoS model for bridge-side GenericPublisher creation.

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Product-level bridge QoS presets.</summary>
    public enum Ros2BridgeQosPreset
    {
        ReliableDefault = 0,
        SensorData = 1,
        TransientLocal = 2,
        Custom = 3
    }

    /// <summary>Small reliability subset exposed by the bridge UX.</summary>
    public enum Ros2BridgeReliability
    {
        Reliable = 0,
        BestEffort = 1
    }

    /// <summary>Small durability subset exposed by the bridge UX.</summary>
    public enum Ros2BridgeDurability
    {
        Volatile = 0,
        TransientLocal = 1
    }

    /// <summary>Resolved bridge QoS profile sent in U2R2 headers.</summary>
    public readonly struct Ros2BridgeQosProfile
    {
        public Ros2BridgeQosProfile(
            Ros2BridgeReliability reliability,
            Ros2BridgeDurability durability,
            int depth,
            string presetName)
        {
            Reliability = reliability;
            Durability = durability;
            Depth = depth < 1 ? 1 : depth;
            PresetName = string.IsNullOrWhiteSpace(presetName) ? "Custom" : presetName;
        }

        public Ros2BridgeReliability Reliability { get; }
        public Ros2BridgeDurability Durability { get; }
        public int Depth { get; }
        public string PresetName { get; }
        public string ReliabilityWireValue => Reliability == Ros2BridgeReliability.BestEffort ? "best_effort" : "reliable";
        public string DurabilityWireValue => Durability == Ros2BridgeDurability.TransientLocal ? "transient_local" : "volatile";
        public string DisplaySummary => $"{ToDisplayLabel(Reliability)} / {ToDisplayLabel(Durability)} / Depth {Depth}";

        public static Ros2BridgeQosProfile ReliableDefault =>
            new Ros2BridgeQosProfile(Ros2BridgeReliability.Reliable, Ros2BridgeDurability.Volatile, 10, "Reliable Default");

        public static Ros2BridgeQosProfile Resolve(
            Ros2BridgeQosPreset preset,
            Ros2BridgeReliability customReliability,
            Ros2BridgeDurability customDurability,
            int customDepth)
        {
            switch (preset)
            {
                case Ros2BridgeQosPreset.SensorData:
                    return new Ros2BridgeQosProfile(Ros2BridgeReliability.BestEffort, Ros2BridgeDurability.Volatile, 5, "Sensor Data");
                case Ros2BridgeQosPreset.TransientLocal:
                    return new Ros2BridgeQosProfile(Ros2BridgeReliability.Reliable, Ros2BridgeDurability.TransientLocal, 1, "Transient Local");
                case Ros2BridgeQosPreset.Custom:
                    return new Ros2BridgeQosProfile(customReliability, customDurability, customDepth, "Custom");
                case Ros2BridgeQosPreset.ReliableDefault:
                default:
                    return ReliableDefault;
            }
        }

        public static string ToDisplayLabel(Ros2BridgeQosPreset preset)
        {
            switch (preset)
            {
                case Ros2BridgeQosPreset.SensorData:
                    return "Sensor Data";
                case Ros2BridgeQosPreset.TransientLocal:
                    return "Transient Local";
                case Ros2BridgeQosPreset.Custom:
                    return "Custom";
                case Ros2BridgeQosPreset.ReliableDefault:
                default:
                    return "Reliable Default";
            }
        }

        public static string ToDisplayLabel(Ros2BridgeReliability reliability)
            => reliability == Ros2BridgeReliability.BestEffort ? "Best Effort" : "Reliable";

        public static string ToDisplayLabel(Ros2BridgeDurability durability)
            => durability == Ros2BridgeDurability.TransientLocal ? "Transient Local" : "Volatile";
    }
}
