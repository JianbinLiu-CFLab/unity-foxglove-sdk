// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Immutable FoxRun publish-profile snapshot and lifecycle state.

using System;
using Unity.FoxgloveSDK.Ros2Bridge;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Immutable defaults captured for the complete enabled lifetime of one
    /// Manager. Individual transport restarts must not recapture this policy.
    /// </summary>
    public sealed class FoxRunPublishSessionPolicy
    {
        internal FoxRunPublishSessionPolicy(
            ulong sessionGeneration,
            bool sessionActive,
            FoxRunEndpoint defaultTargets,
            FoxRunEncoding foxgloveEncoding,
            float defaultPublishRateHz,
            FoxRunRos2QosPreset nativeRos2Qos,
            Ros2BridgeQosProfile bridgeRos2Qos)
        {
            SessionGeneration = sessionGeneration;
            SessionActive = sessionActive;
            DefaultTargets = defaultTargets;
            FoxgloveEncoding = foxgloveEncoding;
            DefaultPublishRateHz = defaultPublishRateHz;
            NativeRos2Qos = nativeRos2Qos;
            BridgeRos2Qos = bridgeRos2Qos;
        }

        public ulong SessionGeneration { get; }
        public bool SessionActive { get; }
        public FoxRunEndpoint DefaultTargets { get; }
        public FoxRunEncoding FoxgloveEncoding { get; }
        public float DefaultPublishRateHz { get; }
        public FoxRunRos2QosPreset NativeRos2Qos { get; }
        public Ros2BridgeQosProfile BridgeRos2Qos { get; }

        internal static FoxRunPublishSessionPolicy Disabled(ulong generation)
            => new(
                generation,
                false,
                defaultTargets: 0,
                foxgloveEncoding: 0,
                defaultPublishRateHz: 0f,
                nativeRos2Qos: FoxRunRos2QosPreset.Default,
                bridgeRos2Qos: Ros2BridgeQosProfile.ReliableDefault);
    }

    internal sealed class FoxRunPublishSessionState
    {
        internal FoxRunPublishSessionState(ulong initialGeneration = 0)
        {
            Current = FoxRunPublishSessionPolicy.Disabled(initialGeneration);
        }

        internal FoxRunPublishSessionPolicy Current { get; private set; }

        internal FoxRunPublishSessionPolicy BeginIfNeeded(
            FoxRunEndpoint defaultTargets,
            FoxRunEncoding foxgloveEncoding,
            float defaultPublishRateHz,
            FoxRunRos2QosPreset nativeRos2Qos,
            Ros2BridgeQosProfile bridgeRos2Qos)
        {
            if (Current.SessionActive)
                return Current;
            if (Current.SessionGeneration == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "FoxRun publish session generation is exhausted.");
            }

            Current = new FoxRunPublishSessionPolicy(
                Current.SessionGeneration + 1UL,
                true,
                FoxRunEndpointResolver.ValidateProfileTargets(defaultTargets),
                FoxRunEncodingResolver.ValidateProfileDefault(foxgloveEncoding),
                NormalizeRate(defaultPublishRateHz),
                FoxRunRos2QosResolver.NormalizeManagerDefault(nativeRos2Qos),
                bridgeRos2Qos);
            return Current;
        }

        internal FoxRunPublishSessionPolicy End()
        {
            if (!Current.SessionActive)
                return Current;

            Current = FoxRunPublishSessionPolicy.Disabled(Current.SessionGeneration);
            return Current;
        }

        private static float NormalizeRate(float rateHz)
            => float.IsNaN(rateHz) || float.IsInfinity(rateHz) || rateHz <= 0f
                ? 10f
                : rateHz;
    }
}
