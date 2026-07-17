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
        KiB = 0,
        MiB = 1
    }

    /// <summary>One concrete portable QoS choice displayed by the Manager Inspector.</summary>
    internal readonly struct FoxRunRos2QosInspectorChoice
    {
        internal FoxRunRos2QosInspectorChoice(
            FoxRunRos2QosPreset preset,
            string label,
            string summary)
        {
            Preset = preset;
            Label = label;
            Summary = summary;
        }

        internal FoxRunRos2QosPreset Preset { get; }
        internal string Label { get; }
        internal string Summary { get; }
    }

    /// <summary>Pure presentation model for native ROS2 subscription Inspector controls.</summary>
    internal static class FoxRunRos2SubscriptionInspectorPresentation
    {
        private static readonly FoxRunRos2QosInspectorChoice[] ConcreteManagerQosChoices =
        {
            new(
                FoxRunRos2QosPreset.Default,
                "ROS 2 Default (R2FU)",
                "R2FU default / Keep Last 10"),
            new(
                FoxRunRos2QosPreset.Reliable,
                "Reliable",
                "Reliable / Volatile / Keep Last 10"),
            new(
                FoxRunRos2QosPreset.SensorData,
                "Sensor Data",
                "Best Effort / Volatile / Keep Last 5"),
            new(
                FoxRunRos2QosPreset.TransientLocal,
                "Transient Local",
                "Reliable / Transient Local / Keep Last 1")
        };

        /// <summary>Concrete Manager choices; source-only Inherit is deliberately omitted.</summary>
        internal static IReadOnlyList<FoxRunRos2QosInspectorChoice> ManagerQosChoices =>
            ConcreteManagerQosChoices;

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
                case FoxRunRos2NativeCopyBudgetUnit.KiB:
                    return 1024;
                case FoxRunRos2NativeCopyBudgetUnit.MiB:
                    return 1024 * 1024;
                default:
                    throw new ArgumentOutOfRangeException(nameof(unit));
            }
        }
    }
}
