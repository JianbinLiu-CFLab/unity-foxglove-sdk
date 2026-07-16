// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Determines when optional R2FU runtime/RMW preflight is required.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Shared, ROS-free policy for optional native runtime preflight. This is
    /// deliberately separate from <c>Ros2NativeOutputPolicy</c>: inbound
    /// demand must select and guard the R2FU runtime without enabling output.
    /// </summary>
    public static class FoxRunNativeDemandPolicy
    {
        /// <summary>
        /// Returns whether generated capability metadata includes a contract
        /// that explicitly selected the native provider. A merely native-capable
        /// inherited contract follows the Manager default and must not create
        /// demand when that default remains WebSocket.
        /// </summary>
        public static bool HasExplicitNativeContract(
            System.Collections.Generic.IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo> bindings)
        {
            if (bindings == null)
                return false;

            for (var i = 0; i < bindings.Count; i++)
            {
                if (bindings[i] != null
                    && bindings[i].DeclaredProvider == FoxRunSubscriptionProvider.Ros2Native)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether this Manager needs a native R2FU runtime/RMW choice.
        /// A native default is intentional user demand even before generated
        /// contracts exist; the inbound hub still owns the separate decision to
        /// create zero bindings and no node in that case.
        /// </summary>
        public static bool HasNativeRuntimeDemand(
            bool nativeOutputEnabled,
            bool subscriptionsEnabled,
            FoxRunSubscriptionProvider defaultSubscriptionProvider,
            bool hasExplicitNativeContract)
        {
            return nativeOutputEnabled
                   || (subscriptionsEnabled
                       && (defaultSubscriptionProvider == FoxRunSubscriptionProvider.Ros2Native
                           || hasExplicitNativeContract));
        }
    }
}
