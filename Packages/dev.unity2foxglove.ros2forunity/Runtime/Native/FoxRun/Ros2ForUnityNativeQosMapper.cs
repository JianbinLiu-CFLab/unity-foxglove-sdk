// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Portable FoxRun subscription QoS mapping through the common R2FU API.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Temporary native QoS ownership. R2FU copies the policy during synchronous
    /// endpoint creation, so callers dispose this object as soon as registration
    /// returns.
    /// </summary>
    internal interface IFoxRunRos2NativeQosProfile : IDisposable
    {
        ROS2.QualityOfServiceProfile NativeProfile { get; }

        void SetHistory(ROS2.HistoryPolicy history, int depth);

        void SetPolicies(
            ROS2.HistoryPolicy history,
            int depth,
            ROS2.ReliabilityPolicy reliability,
            ROS2.DurabilityPolicy durability);
    }

    /// <summary>Injectable creation seam for native-free mapping tests.</summary>
    internal interface IFoxRunRos2NativeQosProfileFactory
    {
        IFoxRunRos2NativeQosProfile Create(ROS2.QosPresetProfile preset);
    }

    /// <summary>Maps resolved portable ROS 2 policies to one common R2FU QoS profile.</summary>
    internal static class Ros2ForUnityNativeQosMapper
    {
        private const string InvalidQosDiagnostic =
            "FoxRun native QoS must be a fully resolved portable ROS 2 policy.";
        private const string UnsupportedSurfaceDiagnostic =
            "The selected ROS2 runtime does not expose the required QoS policy surface.";

        private static readonly IFoxRunRos2NativeQosProfileFactory DefaultFactory =
            new Ros2ForUnityQosProfileFactory();

        internal static FoxRunRos2RegistrationResult TryCreate(
            FoxRunResolvedQos qos,
            out IFoxRunRos2NativeQosProfile profile)
            => TryCreate(qos, DefaultFactory, out profile);

        internal static FoxRunRos2RegistrationResult TryCreate(
            FoxRunResolvedQos qos,
            IFoxRunRos2NativeQosProfileFactory factory,
            out IFoxRunRos2NativeQosProfile profile)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            profile = null;
            if (!IsResolved(qos))
            {
                return FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.UnsupportedQos,
                    InvalidQosDiagnostic);
            }

            IFoxRunRos2NativeQosProfile created = null;
            try
            {
                created = factory.Create(MapPreset(qos.Profile));
                if (created == null)
                    throw new InvalidOperationException("Native QoS factory returned no profile.");

                Configure(created, qos);
                profile = created;
                return FoxRunRos2RegistrationResult.Success();
            }
            catch (Exception exception)
            {
                try
                {
                    created?.Dispose();
                }
                catch
                {
                    // Preserve the mapping failure; the partial profile is no longer published.
                }

                var unsupported = IsMissingPolicySurface(exception);
                return FoxRunRos2RegistrationResult.Failure(
                    unsupported
                        ? FoxRunRos2RegistrationError.UnsupportedQos
                        : FoxRunRos2RegistrationError.BackendFailure,
                    unsupported
                        ? UnsupportedSurfaceDiagnostic
                        : exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void Configure(
            IFoxRunRos2NativeQosProfile profile,
            FoxRunResolvedQos qos)
        {
            profile.SetPolicies(
                MapHistory(qos.History),
                qos.Depth,
                MapReliability(qos.Reliability),
                MapDurability(qos.Durability));
        }

        private static bool IsResolved(FoxRunResolvedQos qos)
            => (qos.Profile == FoxRunQosProfile.Default
                || qos.Profile == FoxRunQosProfile.SensorData
                || qos.Profile == FoxRunQosProfile.SystemDefault)
               && (qos.Reliability == FoxRunQosReliability.SystemDefault
                   || qos.Reliability == FoxRunQosReliability.Reliable
                   || qos.Reliability == FoxRunQosReliability.BestEffort)
               && (qos.Durability == FoxRunQosDurability.SystemDefault
                   || qos.Durability == FoxRunQosDurability.Volatile
                   || qos.Durability == FoxRunQosDurability.TransientLocal)
               && (qos.History == FoxRunQosHistory.SystemDefault
                   || qos.History == FoxRunQosHistory.KeepLast
                   || qos.History == FoxRunQosHistory.KeepAll)
               && (qos.History == FoxRunQosHistory.KeepLast
                   ? qos.Depth > 0
                   : qos.Depth == 0);

        private static ROS2.HistoryPolicy MapHistory(FoxRunQosHistory history)
        {
            switch (history)
            {
                case FoxRunQosHistory.SystemDefault:
                    return ROS2.HistoryPolicy.QOS_POLICY_HISTORY_SYSTEM_DEFAULT;
                case FoxRunQosHistory.KeepLast:
                    return ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST;
                case FoxRunQosHistory.KeepAll:
                    return ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_ALL;
                default:
                    throw new ArgumentOutOfRangeException(nameof(history));
            }
        }

        private static ROS2.QosPresetProfile MapPreset(FoxRunQosProfile profile)
        {
            switch (profile)
            {
                case FoxRunQosProfile.Default:
                    return ROS2.QosPresetProfile.DEFAULT;
                case FoxRunQosProfile.SensorData:
                    return ROS2.QosPresetProfile.SENSOR_DATA;
                case FoxRunQosProfile.SystemDefault:
                    return ROS2.QosPresetProfile.SYSTEM_DEFAULT;
                default:
                    throw new ArgumentOutOfRangeException(nameof(profile));
            }
        }

        private static ROS2.ReliabilityPolicy MapReliability(FoxRunQosReliability reliability)
        {
            switch (reliability)
            {
                case FoxRunQosReliability.SystemDefault:
                    return ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT;
                case FoxRunQosReliability.Reliable:
                    return ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE;
                case FoxRunQosReliability.BestEffort:
                    return ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT;
                default:
                    throw new ArgumentOutOfRangeException(nameof(reliability));
            }
        }

        private static ROS2.DurabilityPolicy MapDurability(FoxRunQosDurability durability)
        {
            switch (durability)
            {
                case FoxRunQosDurability.SystemDefault:
                    return ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_SYSTEM_DEFAULT;
                case FoxRunQosDurability.Volatile:
                    return ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE;
                case FoxRunQosDurability.TransientLocal:
                    return ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_TRANSIENT_LOCAL;
                default:
                    throw new ArgumentOutOfRangeException(nameof(durability));
            }
        }

        private static bool IsMissingPolicySurface(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is MissingMethodException
                    || current is TypeLoadException
                    || current is EntryPointNotFoundException
                    || current is NotSupportedException)
                    return true;
            }

            return false;
        }

        private sealed class Ros2ForUnityQosProfileFactory : IFoxRunRos2NativeQosProfileFactory
        {
            public IFoxRunRos2NativeQosProfile Create(ROS2.QosPresetProfile preset)
                => new Ros2ForUnityQosProfile(new ROS2.QualityOfServiceProfile(preset));
        }

        private sealed class Ros2ForUnityQosProfile : IFoxRunRos2NativeQosProfile
        {
            private ROS2.QualityOfServiceProfile _profile;

            public Ros2ForUnityQosProfile(ROS2.QualityOfServiceProfile profile)
            {
                _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            }

            public ROS2.QualityOfServiceProfile NativeProfile
                => _profile ?? throw new ObjectDisposedException(nameof(Ros2ForUnityQosProfile));

            public void SetHistory(ROS2.HistoryPolicy history, int depth)
                => NativeProfile.SetHistory(history, depth);

            public void SetPolicies(
                ROS2.HistoryPolicy history,
                int depth,
                ROS2.ReliabilityPolicy reliability,
                ROS2.DurabilityPolicy durability)
                => NativeProfile.SetPolicies(history, depth, reliability, durability);

            public void Dispose()
            {
                var profile = _profile;
                _profile = null;
                profile?.Dispose();
            }
        }
    }
}
#endif
