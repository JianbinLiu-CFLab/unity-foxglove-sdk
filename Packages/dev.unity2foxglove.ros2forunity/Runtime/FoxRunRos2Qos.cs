// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime
// Purpose: R2FU Provider-owned QoS vocabulary and strict resolution.

using System;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public enum FoxRunQosProfile
    {
        Default = 1,
        SensorData = 2,
        SystemDefault = 3
    }

    public enum FoxRunQosReliability
    {
        SystemDefault = 1,
        Reliable = 2,
        BestEffort = 3
    }

    public enum FoxRunQosDurability
    {
        SystemDefault = 1,
        Volatile = 2,
        TransientLocal = 3
    }

    public enum FoxRunQosHistory
    {
        SystemDefault = 1,
        KeepLast = 2,
        KeepAll = 3
    }

    public enum FoxRunQosDiagnosticCode
    {
        None = 0,
        InvalidProfile = 1,
        InvalidReliability = 2,
        InvalidDurability = 3,
        InvalidHistory = 4,
        InvalidDepth = 5,
        DepthRequiresKeepLast = 6,
        InvalidInheritedQos = 7
    }

    public readonly struct FoxRunQosResolution
    {
        internal FoxRunQosResolution(
            bool success,
            FoxRunResolvedQos qos,
            FoxRunQosDiagnosticCode diagnosticCode,
            string diagnosticMessage)
        {
            Success = success;
            Qos = qos;
            DiagnosticCode = diagnosticCode;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
        }

        public bool Success { get; }
        public FoxRunResolvedQos Qos { get; }
        public FoxRunQosDiagnosticCode DiagnosticCode { get; }
        public string DiagnosticMessage { get; }
    }

    public readonly struct FoxRunResolvedQos :
        IEquatable<FoxRunResolvedQos>
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

        public static bool operator ==(
            FoxRunResolvedQos left,
            FoxRunResolvedQos right)
            => left.Equals(right);

        public static bool operator !=(
            FoxRunResolvedQos left,
            FoxRunResolvedQos right)
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

    public static class FoxRunRos2QosProfileResolver
    {
        public static FoxRunQosResolution Resolve(
            FoxRunQosProfile profile,
            bool hasProfile,
            FoxRunQosReliability reliability,
            bool hasReliability,
            FoxRunQosDurability durability,
            bool hasDurability,
            FoxRunQosHistory history,
            bool hasHistory,
            int depth,
            bool hasDepth,
            FoxRunResolvedQos inherited)
        {
            if (!IsValidResolved(inherited))
            {
                return Failure(
                    FoxRunQosDiagnosticCode.InvalidInheritedQos,
                    "Inherited ROS 2 QoS is invalid.");
            }
            if (hasProfile && !FoxRunResolvedQos.IsDefined(profile))
            {
                return Failure(
                    FoxRunQosDiagnosticCode.InvalidProfile,
                    "FoxRun QoS profile is invalid.");
            }
            if (hasReliability
                && !FoxRunResolvedQos.IsDefined(reliability))
            {
                return Failure(
                    FoxRunQosDiagnosticCode.InvalidReliability,
                    "FoxRun QoS reliability is invalid.");
            }
            if (hasDurability
                && !FoxRunResolvedQos.IsDefined(durability))
            {
                return Failure(
                    FoxRunQosDiagnosticCode.InvalidDurability,
                    "FoxRun QoS durability is invalid.");
            }
            if (hasHistory && !FoxRunResolvedQos.IsDefined(history))
            {
                return Failure(
                    FoxRunQosDiagnosticCode.InvalidHistory,
                    "FoxRun QoS history is invalid.");
            }
            if (hasDepth && depth <= 0)
            {
                return Failure(
                    FoxRunQosDiagnosticCode.InvalidDepth,
                    "FoxRun QoS depth must be positive.");
            }

            var basis = hasProfile ? FromProfile(profile) : inherited;
            var resolvedReliability =
                hasReliability ? reliability : basis.Reliability;
            var resolvedDurability =
                hasDurability ? durability : basis.Durability;
            var resolvedHistory =
                hasHistory ? history : basis.History;
            if (hasDepth
                && resolvedHistory != FoxRunQosHistory.KeepLast)
            {
                return Failure(
                    FoxRunQosDiagnosticCode.DepthRequiresKeepLast,
                    "FoxRun QoS depth is valid only with Keep Last history.");
            }

            var resolvedDepth =
                resolvedHistory == FoxRunQosHistory.KeepLast
                    ? hasDepth
                        ? depth
                        : basis.History == FoxRunQosHistory.KeepLast
                          && basis.Depth > 0
                            ? basis.Depth
                            : 10
                    : 0;
            return Success(
                new FoxRunResolvedQos(
                    hasProfile ? profile : basis.Profile,
                    resolvedReliability,
                    resolvedDurability,
                    resolvedHistory,
                    resolvedDepth));
        }

        public static FoxRunResolvedQos FromProfile(
            FoxRunQosProfile profile)
        {
            switch (profile)
            {
                case FoxRunQosProfile.Default:
                    return FoxRunResolvedQos.Default;
                case FoxRunQosProfile.SensorData:
                    return FoxRunResolvedQos.SensorData;
                case FoxRunQosProfile.SystemDefault:
                    return FoxRunResolvedQos.SystemDefault;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(profile));
            }
        }

        private static bool IsValidResolved(FoxRunResolvedQos value)
            => FoxRunResolvedQos.IsDefined(value.Profile)
               && FoxRunResolvedQos.IsDefined(value.Reliability)
               && FoxRunResolvedQos.IsDefined(value.Durability)
               && FoxRunResolvedQos.IsDefined(value.History)
               && (value.History == FoxRunQosHistory.KeepLast
                   ? value.Depth > 0
                   : value.Depth == 0);

        private static FoxRunQosResolution Success(
            FoxRunResolvedQos qos)
            => new(
                true,
                qos,
                FoxRunQosDiagnosticCode.None,
                string.Empty);

        private static FoxRunQosResolution Failure(
            FoxRunQosDiagnosticCode code,
            string message)
            => new(false, default, code, message);
    }

    [Serializable]
    internal sealed class FoxRunRos2QosProfileSettings
    {
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private FoxRunQosProfile _profile =
            FoxRunQosProfile.Default;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private bool _overrideReliability;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private FoxRunQosReliability _reliability =
            FoxRunQosReliability.Reliable;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private bool _overrideDurability;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private FoxRunQosDurability _durability =
            FoxRunQosDurability.Volatile;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private bool _overrideHistory;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private FoxRunQosHistory _history =
            FoxRunQosHistory.KeepLast;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private bool _overrideDepth;
#if UNITY_5_3_OR_NEWER
        [SerializeField, Min(1)]
#endif
        private int _depth = 10;

        internal FoxRunResolvedQos Resolve()
        {
            var resolution =
                FoxRunRos2QosProfileResolver.Resolve(
                    _profile,
                    hasProfile: true,
                    _reliability,
                    _overrideReliability,
                    _durability,
                    _overrideDurability,
                    _history,
                    _overrideHistory,
                    _depth,
                    _overrideDepth,
                    FoxRunResolvedQos.Default);
            if (!resolution.Success)
            {
                throw new InvalidOperationException(
                    "R2FU QoS is invalid: "
                    + resolution.DiagnosticMessage);
            }

            return resolution.Qos;
        }
    }
}
