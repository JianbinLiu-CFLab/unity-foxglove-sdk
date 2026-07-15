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

    /// <summary>Maps resolved FoxRun presets to one common R2FU QoS profile.</summary>
    internal static class Ros2ForUnityNativeQosMapper
    {
        private const string InvalidPresetDiagnostic =
            "FoxRun native subscription QoS must be a resolved portable preset.";
        private const string UnsupportedSurfaceDiagnostic =
            "The selected ROS2 runtime does not expose the required QoS policy surface.";

        private static readonly IFoxRunRos2NativeQosProfileFactory DefaultFactory =
            new Ros2ForUnityQosProfileFactory();

        internal static FoxRunRos2RegistrationResult TryCreate(
            FoxRunRos2QosPreset preset,
            out IFoxRunRos2NativeQosProfile profile)
            => TryCreate(preset, DefaultFactory, out profile);

        internal static FoxRunRos2RegistrationResult TryCreate(
            FoxRunRos2QosPreset preset,
            IFoxRunRos2NativeQosProfileFactory factory,
            out IFoxRunRos2NativeQosProfile profile)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            profile = null;
            if (!IsResolved(preset))
            {
                return FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.UnsupportedQos,
                    InvalidPresetDiagnostic);
            }

            IFoxRunRos2NativeQosProfile created = null;
            try
            {
                created = factory.Create(ROS2.QosPresetProfile.DEFAULT);
                if (created == null)
                    throw new InvalidOperationException("Native QoS factory returned no profile.");

                Configure(created, preset);
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
            FoxRunRos2QosPreset preset)
        {
            const ROS2.HistoryPolicy history =
                ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST;
            const ROS2.DurabilityPolicy volatileDurability =
                ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE;
            const ROS2.ReliabilityPolicy reliable =
                ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE;

            switch (preset)
            {
                case FoxRunRos2QosPreset.Default:
                    profile.SetHistory(history, 10);
                    return;
                case FoxRunRos2QosPreset.Reliable:
                    profile.SetPolicies(history, 10, reliable, volatileDurability);
                    return;
                case FoxRunRos2QosPreset.SensorData:
                    profile.SetPolicies(
                        history,
                        5,
                        ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT,
                        volatileDurability);
                    return;
                case FoxRunRos2QosPreset.TransientLocal:
                    profile.SetPolicies(
                        history,
                        1,
                        reliable,
                        ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_TRANSIENT_LOCAL);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }

        private static bool IsResolved(FoxRunRos2QosPreset preset)
            => preset == FoxRunRos2QosPreset.Default
               || preset == FoxRunRos2QosPreset.Reliable
               || preset == FoxRunRos2QosPreset.SensorData
               || preset == FoxRunRos2QosPreset.TransientLocal;

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
