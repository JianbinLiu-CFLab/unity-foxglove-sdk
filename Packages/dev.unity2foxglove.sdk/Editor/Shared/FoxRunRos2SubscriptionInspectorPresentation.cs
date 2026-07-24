// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared
// Purpose: Unity-free labels and copied-data unit conversion for native ROS2 subscription controls.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Units available when editing a native ROS2 copied-data budget.</summary>
    internal enum FoxRunRos2NativeCopyBudgetUnit
    {
        KB = 0,
        MB = 1
    }

    /// <summary>One concrete portable QoS choice displayed by the Manager Inspector.</summary>
    internal readonly struct FoxRunRos2QosInspectorChoice
    {
        internal FoxRunRos2QosInspectorChoice(
            FoxRunQosProfile profile,
            string label,
            string summary)
        {
            Profile = profile;
            Label = label;
            Summary = summary;
        }

        internal FoxRunQosProfile Profile { get; }
        internal string Label { get; }
        internal string Summary { get; }
    }

    /// <summary>Pure presentation model for native ROS2 subscription Inspector controls.</summary>
    internal static class FoxRunRos2SubscriptionInspectorPresentation
    {
        private static readonly string[] NativeCopyBudgetUnitLabels = { "KB", "MB" };

        private static readonly string[] ConcreteManagerQosLabels =
        {
            "Default",
            "Sensor Data",
            "System Default"
        };

        private static readonly FoxRunRos2QosInspectorChoice[] ConcreteManagerQosChoices =
        {
            new(
                FoxRunQosProfile.Default,
                ConcreteManagerQosLabels[0],
                "Reliable / Volatile / Keep Last 10"),
            new(
                FoxRunQosProfile.SensorData,
                ConcreteManagerQosLabels[1],
                "Best Effort / Volatile / Keep Last 5"),
            new(
                FoxRunQosProfile.SystemDefault,
                ConcreteManagerQosLabels[2],
                "System Default / System Default / System Default")
        };

        /// <summary>Concrete Manager choices; source-only Inherit is deliberately omitted.</summary>
        internal static IReadOnlyList<FoxRunRos2QosInspectorChoice> ManagerQosChoices =>
            ConcreteManagerQosChoices;

        /// <summary>Stable Popup labels paired positionally with <see cref="ManagerQosChoices"/>.</summary>
        internal static string[] ManagerQosLabels => ConcreteManagerQosLabels;

        /// <summary>
        /// Human-facing decimal unit labels. The stored budget remains an exact byte count.
        /// </summary>
        internal static string[] NativeCopyBudgetLabels => NativeCopyBudgetUnitLabels;

        internal static string Summary(FoxRunResolvedQos qos)
        {
            var depth = qos.History == FoxRunQosHistory.KeepLast
                ? " " + qos.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
            return PolicyLabel(qos.Reliability)
                   + " / "
                   + PolicyLabel(qos.Durability)
                   + " / "
                   + PolicyLabel(qos.History)
                   + depth;
        }

        internal static string DeclaredSummary(
            FoxRunQosProfile profile,
            FoxRunQosReliability reliability,
            FoxRunQosDurability durability,
            FoxRunQosHistory history,
            int depth)
        {
            var parts = new List<string> { ProfileLabel(profile) };
            if (reliability != 0)
                parts.Add("Reliability=" + PolicyLabel(reliability));
            if (durability != 0)
                parts.Add("Durability=" + PolicyLabel(durability));
            if (history != 0)
                parts.Add("History=" + PolicyLabel(history));
            if (depth > 0)
            {
                parts.Add(
                    "Depth="
                    + depth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return string.Join("; ", parts);
        }

        private static string ProfileLabel(FoxRunQosProfile value)
            => value == 0
                ? "Inherit"
                : value == FoxRunQosProfile.SensorData
                    ? "Sensor Data"
                    : value == FoxRunQosProfile.SystemDefault
                        ? "System Default"
                        : "Default";

        private static string PolicyLabel(FoxRunQosReliability value)
            => value == FoxRunQosReliability.BestEffort
                ? "Best Effort"
                : value == FoxRunQosReliability.SystemDefault
                    ? "System Default"
                    : "Reliable";

        private static string PolicyLabel(FoxRunQosDurability value)
            => value == FoxRunQosDurability.TransientLocal
                ? "Transient Local"
                : value == FoxRunQosDurability.SystemDefault
                    ? "System Default"
                    : "Volatile";

        private static string PolicyLabel(FoxRunQosHistory value)
            => value == FoxRunQosHistory.KeepAll
                ? "Keep All"
                : value == FoxRunQosHistory.SystemDefault
                    ? "System Default"
                    : "Keep Last";

        /// <summary>Converts a serialized budget to the selected display unit.</summary>
        internal static double ToDisplayValue(
            int serializedBytes,
            FoxRunRos2NativeCopyBudgetUnit unit)
        {
            return FoxRunRos2NativeCopyBudgetPolicy.NormalizeSerializedBytes(serializedBytes)
                   / (double)GetBytesPerUnit(unit);
        }

        /// <summary>
        /// Converts an edited display value to portable bytes without overflowing on malformed input.
        /// </summary>
        internal static int ToClampedBytes(
            double displayValue,
            FoxRunRos2NativeCopyBudgetUnit unit)
        {
            var bytesPerUnit = GetBytesPerUnit(unit);
            if (double.IsNaN(displayValue) || displayValue <= 0d)
                return FoxRunRos2NativeCopyBudgetPolicy.MinBytes;

            var maximumDisplayValue =
                FoxRunRos2NativeCopyBudgetPolicy.MaxBytes / (double)bytesPerUnit;
            if (double.IsPositiveInfinity(displayValue) || displayValue >= maximumDisplayValue)
                return FoxRunRos2NativeCopyBudgetPolicy.MaxBytes;

            var roundedBytes = Math.Round(
                displayValue * bytesPerUnit,
                MidpointRounding.AwayFromZero);
            return FoxRunRos2NativeCopyBudgetPolicy.ClampUserEditedBytes((int)roundedBytes);
        }

        private static int GetBytesPerUnit(FoxRunRos2NativeCopyBudgetUnit unit)
        {
            switch (unit)
            {
                case FoxRunRos2NativeCopyBudgetUnit.KB:
                    return 1_000;
                case FoxRunRos2NativeCopyBudgetUnit.MB:
                    return 1_000_000;
                default:
                    throw new ArgumentOutOfRangeException(nameof(unit));
            }
        }
    }
}
