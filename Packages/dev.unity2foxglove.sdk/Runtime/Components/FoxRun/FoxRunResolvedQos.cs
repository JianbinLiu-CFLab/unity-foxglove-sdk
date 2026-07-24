// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Immutable transport-neutral ROS 2 QoS contract.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Fully resolved portable ROS 2 QoS. A Keep Last contract always has a
    /// positive depth; Keep All and System Default histories always use zero.
    /// </summary>
    public readonly struct FoxRunResolvedQos : IEquatable<FoxRunResolvedQos>
    {
        public FoxRunResolvedQos(
            FoxRunQosProfile profile,
            FoxRunQosReliability reliability,
            FoxRunQosDurability durability,
            FoxRunQosHistory history,
            int depth)
        {
            if (!IsDefined(profile))
                throw new ArgumentOutOfRangeException(nameof(profile));
            if (!IsDefined(reliability))
                throw new ArgumentOutOfRangeException(nameof(reliability));
            if (!IsDefined(durability))
                throw new ArgumentOutOfRangeException(nameof(durability));
            if (!IsDefined(history))
                throw new ArgumentOutOfRangeException(nameof(history));
            if (history == FoxRunQosHistory.KeepLast)
            {
                if (depth <= 0)
                    throw new ArgumentOutOfRangeException(nameof(depth));
            }
            else if (depth != 0)
            {
                throw new ArgumentException(
                    "ROS 2 QoS depth is valid only with Keep Last history.",
                    nameof(depth));
            }

            Profile = profile;
            Reliability = reliability;
            Durability = durability;
            History = history;
            Depth = depth;
        }

        public FoxRunQosProfile Profile { get; }
        public FoxRunQosReliability Reliability { get; }
        public FoxRunQosDurability Durability { get; }
        public FoxRunQosHistory History { get; }
        public int Depth { get; }

        public static FoxRunResolvedQos Default =>
            new(
                FoxRunQosProfile.Default,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepLast,
                10);

        public static FoxRunResolvedQos SensorData =>
            new(
                FoxRunQosProfile.SensorData,
                FoxRunQosReliability.BestEffort,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepLast,
                5);

        public static FoxRunResolvedQos SystemDefault =>
            new(
                FoxRunQosProfile.SystemDefault,
                FoxRunQosReliability.SystemDefault,
                FoxRunQosDurability.SystemDefault,
                FoxRunQosHistory.SystemDefault,
                0);

        public bool Equals(FoxRunResolvedQos other)
            => Profile == other.Profile
               && Reliability == other.Reliability
               && Durability == other.Durability
               && History == other.History
               && Depth == other.Depth;

        public override bool Equals(object obj)
            => obj is FoxRunResolvedQos other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Profile;
                hash = (hash * 397) ^ (int)Reliability;
                hash = (hash * 397) ^ (int)Durability;
                hash = (hash * 397) ^ (int)History;
                return (hash * 397) ^ Depth;
            }
        }

        public static bool operator ==(FoxRunResolvedQos left, FoxRunResolvedQos right)
            => left.Equals(right);

        public static bool operator !=(FoxRunResolvedQos left, FoxRunResolvedQos right)
            => !left.Equals(right);

        internal static bool IsDefined(FoxRunQosProfile value)
            => value == FoxRunQosProfile.Default
               || value == FoxRunQosProfile.SensorData
               || value == FoxRunQosProfile.SystemDefault;

        internal static bool IsDefined(FoxRunQosReliability value)
            => value == FoxRunQosReliability.SystemDefault
               || value == FoxRunQosReliability.Reliable
               || value == FoxRunQosReliability.BestEffort;

        internal static bool IsDefined(FoxRunQosDurability value)
            => value == FoxRunQosDurability.SystemDefault
               || value == FoxRunQosDurability.Volatile
               || value == FoxRunQosDurability.TransientLocal;

        internal static bool IsDefined(FoxRunQosHistory value)
            => value == FoxRunQosHistory.SystemDefault
               || value == FoxRunQosHistory.KeepLast
               || value == FoxRunQosHistory.KeepAll;
    }
}
