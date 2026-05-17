// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Product bridge-output policy independent from WebSocket encoding.

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Per-publisher ROS2 Bridge output override.</summary>
    public enum Ros2BridgeOutputOverride
    {
        UseManager = 0,
        Disabled = 1,
        Enabled = 2
    }

    /// <summary>Effective ROS2 Bridge output after manager and publisher policy resolution.</summary>
    public enum Ros2BridgeEffectiveOutput
    {
        Disabled = 0,
        Enabled = 1,
        Unsupported = 2
    }

    /// <summary>Resolved bridge output request and fallback state.</summary>
    public readonly struct Ros2BridgeOutputResolution
    {
        public Ros2BridgeOutputResolution(
            Ros2BridgeEffectiveOutput requested,
            Ros2BridgeEffectiveOutput effective,
            bool fellBack)
        {
            Requested = requested;
            Effective = effective;
            FellBack = fellBack;
        }

        public Ros2BridgeEffectiveOutput Requested { get; }
        public Ros2BridgeEffectiveOutput Effective { get; }
        public bool FellBack { get; }
        public bool IsEnabled => Effective == Ros2BridgeEffectiveOutput.Enabled;
        public string RequestedLabel => Ros2BridgeOutputPolicy.ToDisplayLabel(Requested);
        public string EffectiveLabel => Ros2BridgeOutputPolicy.ToDisplayLabel(Effective);
    }

    /// <summary>Pure bridge-output policy helper used by runtime and Inspector tests.</summary>
    public static class Ros2BridgeOutputPolicy
    {
        public static Ros2BridgeOutputResolution Resolve(
            bool managerEnabled,
            bool managerDefaultEnabled,
            bool allowPublisherOverride,
            Ros2BridgeOutputOverride publisherOverride,
            bool supportsBridge)
        {
            var requested = ResolveRequested(
                managerEnabled,
                managerDefaultEnabled,
                allowPublisherOverride,
                publisherOverride);

            if (!supportsBridge && requested == Ros2BridgeEffectiveOutput.Enabled)
                return new Ros2BridgeOutputResolution(requested, Ros2BridgeEffectiveOutput.Unsupported, fellBack: true);

            return new Ros2BridgeOutputResolution(requested, requested, fellBack: false);
        }

        public static string ToDisplayLabel(Ros2BridgeEffectiveOutput output)
        {
            switch (output)
            {
                case Ros2BridgeEffectiveOutput.Enabled:
                    return "Enabled";
                case Ros2BridgeEffectiveOutput.Unsupported:
                    return "Unsupported";
                case Ros2BridgeEffectiveOutput.Disabled:
                default:
                    return "Disabled";
            }
        }

        private static Ros2BridgeEffectiveOutput ResolveRequested(
            bool managerEnabled,
            bool managerDefaultEnabled,
            bool allowPublisherOverride,
            Ros2BridgeOutputOverride publisherOverride)
        {
            if (!managerEnabled)
                return Ros2BridgeEffectiveOutput.Disabled;

            if (allowPublisherOverride)
            {
                if (publisherOverride == Ros2BridgeOutputOverride.Enabled)
                    return Ros2BridgeEffectiveOutput.Enabled;
                if (publisherOverride == Ros2BridgeOutputOverride.Disabled)
                    return Ros2BridgeEffectiveOutput.Disabled;
            }

            return managerDefaultEnabled
                ? Ros2BridgeEffectiveOutput.Enabled
                : Ros2BridgeEffectiveOutput.Disabled;
        }
    }
}
