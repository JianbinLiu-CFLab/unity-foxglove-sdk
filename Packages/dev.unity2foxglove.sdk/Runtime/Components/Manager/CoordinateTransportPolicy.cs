// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Pure defaults, normalization, and legacy migration for directional transport coordinates.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Versioned policy for external coordinate conventions on the output and
    /// input sides of a <see cref="FoxgloveManager"/> transport boundary.
    /// </summary>
    public static class CoordinateTransportPolicy
    {
        /// <summary>First serialized version with independent input and output settings.</summary>
        public const int CurrentSerializationVersion = 1;

        /// <summary>Default convention for newly created Unity-to-external paths.</summary>
        public const CoordinateMode DefaultOutputCoordinateMode = CoordinateMode.RightHand;

        /// <summary>Default convention for newly created external-to-Unity paths.</summary>
        public const CoordinateMode DefaultInputCoordinateMode = CoordinateMode.RightHand;

        /// <summary>
        /// Normalizes malformed serialized enum values without changing legacy
        /// behavior: values other than <see cref="CoordinateMode.RightHand"/>
        /// historically behaved as left-handed because conversion only occurred
        /// for the explicit right-handed value.
        /// </summary>
        public static CoordinateMode NormalizeSerializedCoordinateMode(CoordinateMode mode)
            => mode == CoordinateMode.RightHand ? CoordinateMode.RightHand : CoordinateMode.LeftHand;

        /// <summary>
        /// Performs the one-time migration from one legacy coordinate setting
        /// to independent output and input conventions.
        /// </summary>
        public static void Migrate(
            ref int serializationVersion,
            CoordinateMode legacyCoordinateMode,
            ref CoordinateMode outputCoordinateMode,
            ref CoordinateMode inputCoordinateMode)
        {
            if (serializationVersion < CurrentSerializationVersion)
            {
                var legacy = NormalizeSerializedCoordinateMode(legacyCoordinateMode);
                outputCoordinateMode = legacy;
                inputCoordinateMode = legacy;
                serializationVersion = CurrentSerializationVersion;
                return;
            }

            outputCoordinateMode = NormalizeSerializedCoordinateMode(outputCoordinateMode);
            inputCoordinateMode = NormalizeSerializedCoordinateMode(inputCoordinateMode);
        }

        /// <summary>Stable MCAP metadata value for a serialized coordinate convention.</summary>
        public static string ToMcapCoordinateMode(CoordinateMode mode)
            => NormalizeSerializedCoordinateMode(mode) == CoordinateMode.RightHand
                ? "RightHand"
                : "LeftHand";
    }
}
