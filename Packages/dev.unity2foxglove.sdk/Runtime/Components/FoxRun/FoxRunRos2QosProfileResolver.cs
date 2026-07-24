// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Strict portable ROS 2 QoS profile and override resolution.

namespace Unity.FoxgloveSDK.Components
{
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

    /// <summary>Pure resolver shared by Native and Bridge directions.</summary>
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
                return Failure(FoxRunQosDiagnosticCode.InvalidInheritedQos, "Inherited ROS 2 QoS is invalid.");
            if (hasProfile && !FoxRunResolvedQos.IsDefined(profile))
                return Failure(FoxRunQosDiagnosticCode.InvalidProfile, "FoxRun QoS profile is invalid.");
            if (hasReliability && !FoxRunResolvedQos.IsDefined(reliability))
                return Failure(FoxRunQosDiagnosticCode.InvalidReliability, "FoxRun QoS reliability is invalid.");
            if (hasDurability && !FoxRunResolvedQos.IsDefined(durability))
                return Failure(FoxRunQosDiagnosticCode.InvalidDurability, "FoxRun QoS durability is invalid.");
            if (hasHistory && !FoxRunResolvedQos.IsDefined(history))
                return Failure(FoxRunQosDiagnosticCode.InvalidHistory, "FoxRun QoS history is invalid.");
            if (hasDepth && depth <= 0)
                return Failure(FoxRunQosDiagnosticCode.InvalidDepth, "FoxRun QoS depth must be positive.");

            var basis = hasProfile ? FromProfile(profile) : inherited;
            var resolvedReliability = hasReliability ? reliability : basis.Reliability;
            var resolvedDurability = hasDurability ? durability : basis.Durability;
            var resolvedHistory = hasHistory ? history : basis.History;

            if (hasDepth && resolvedHistory != FoxRunQosHistory.KeepLast)
            {
                return Failure(
                    FoxRunQosDiagnosticCode.DepthRequiresKeepLast,
                    "FoxRun QoS depth is valid only with Keep Last history.");
            }

            int resolvedDepth;
            if (resolvedHistory == FoxRunQosHistory.KeepLast)
            {
                resolvedDepth = hasDepth
                    ? depth
                    : basis.History == FoxRunQosHistory.KeepLast && basis.Depth > 0
                        ? basis.Depth
                        : 10;
            }
            else
            {
                resolvedDepth = 0;
            }

            return Success(new FoxRunResolvedQos(
                hasProfile ? profile : basis.Profile,
                resolvedReliability,
                resolvedDurability,
                resolvedHistory,
                resolvedDepth));
        }

        public static FoxRunResolvedQos FromProfile(FoxRunQosProfile profile)
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
                    throw new System.ArgumentOutOfRangeException(nameof(profile));
            }
        }

        private static bool IsValidResolved(FoxRunResolvedQos value)
        {
            if (!FoxRunResolvedQos.IsDefined(value.Profile)
                || !FoxRunResolvedQos.IsDefined(value.Reliability)
                || !FoxRunResolvedQos.IsDefined(value.Durability)
                || !FoxRunResolvedQos.IsDefined(value.History))
                return false;
            return value.History == FoxRunQosHistory.KeepLast
                ? value.Depth > 0
                : value.Depth == 0;
        }

        private static FoxRunQosResolution Success(FoxRunResolvedQos qos)
            => new(true, qos, FoxRunQosDiagnosticCode.None, string.Empty);

        private static FoxRunQosResolution Failure(
            FoxRunQosDiagnosticCode code,
            string message)
            => new(false, default, code, message);
    }
}
