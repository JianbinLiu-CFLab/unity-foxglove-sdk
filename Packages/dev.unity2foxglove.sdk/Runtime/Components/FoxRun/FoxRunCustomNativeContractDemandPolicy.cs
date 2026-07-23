// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Resolves directional R2FU demand for generated custom DTO contracts.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Keeps custom native transport demand distinct from the general Phase179
    /// subscription demand. A custom publisher is meaningful only when native
    /// output is selected; a custom inbound binding additionally requires the
    /// resolved subscription provider to be native.
    /// </summary>
    public static class FoxRunCustomNativeContractDemandPolicy
    {
        public static bool HasDemand(
            IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> contracts,
            FoxRunEndpoint defaultPublishTargets,
            bool subscriptionsEnabled,
            FoxRunEndpoint defaultSubscriptionSource)
        {
            if (contracts == null)
                return false;

            for (var index = 0; index < contracts.Count; index++)
            {
                var contract = contracts[index];
                if (contract == null
                    || !contract.SupportsRos2Native
                    || string.IsNullOrWhiteSpace(contract.CustomEnvelopeIdentity))
                {
                    continue;
                }

                var isPublish = string.Equals(contract.Flow, "Publish", StringComparison.Ordinal)
                                || string.Equals(contract.Flow, "PublishAndSubscribe", StringComparison.Ordinal);
                var publishTargets = contract.DeclaredTargets == 0
                    ? defaultPublishTargets
                    : contract.DeclaredTargets;
                if (isPublish
                    && (publishTargets & FoxRunEndpoint.Ros2Native) != 0)
                    return true;

                var isSubscribe = string.Equals(contract.Flow, "Subscribe", StringComparison.Ordinal)
                                  || string.Equals(contract.Flow, "PublishAndSubscribe", StringComparison.Ordinal);
                if (!subscriptionsEnabled || !isSubscribe)
                    continue;

                var provider = contract.DeclaredSource == (FoxRunEndpoint)0
                    ? defaultSubscriptionSource
                    : contract.DeclaredSource;
                if (provider == FoxRunEndpoint.Ros2Native)
                    return true;
            }

            return false;
        }

        public static bool HasSubscriptionDemand(
            IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> contracts,
            bool subscriptionsEnabled,
            FoxRunEndpoint defaultSubscriptionSource)
        {
            if (contracts == null || !subscriptionsEnabled)
                return false;

            for (var index = 0; index < contracts.Count; index++)
            {
                var contract = contracts[index];
                if (contract == null
                    || !contract.SupportsRos2Native
                    || string.IsNullOrWhiteSpace(contract.CustomEnvelopeIdentity))
                {
                    continue;
                }

                var isSubscribe = string.Equals(contract.Flow, "Subscribe", StringComparison.Ordinal)
                                  || string.Equals(
                                      contract.Flow,
                                      "PublishAndSubscribe",
                                      StringComparison.Ordinal);
                if (!isSubscribe)
                    continue;

                var source = contract.DeclaredSource == 0
                    ? defaultSubscriptionSource
                    : contract.DeclaredSource;
                if (source == FoxRunEndpoint.Ros2Native)
                    return true;
            }

            return false;
        }

        public static bool HasExplicitNativePublishContract(
            IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> contracts)
        {
            if (contracts == null)
                return false;

            for (var index = 0; index < contracts.Count; index++)
            {
                var contract = contracts[index];
                if (contract == null
                    || !contract.SupportsRos2Native
                    || string.IsNullOrWhiteSpace(contract.CustomEnvelopeIdentity))
                {
                    continue;
                }

                var isPublish = string.Equals(contract.Flow, "Publish", StringComparison.Ordinal)
                                || string.Equals(
                                    contract.Flow,
                                    "PublishAndSubscribe",
                                    StringComparison.Ordinal);
                if (isPublish
                    && contract.DeclaredTargets != 0
                    && (contract.DeclaredTargets & FoxRunEndpoint.Ros2Native) != 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
