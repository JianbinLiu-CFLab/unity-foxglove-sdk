// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Owns the FoxRun subscription session independently of server output.

using System;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private readonly FoxRunSubscriptionSessionState _foxRunSubscriptionSessionState = new();

        /// <summary>Current immutable subscription-session snapshot.</summary>
        public FoxRunSubscriptionSessionPolicy ActiveFoxRunSubscriptionSessionPolicy =>
            _foxRunSubscriptionSessionState.Current;

        /// <summary>
        /// Raised when subscriptions begin or end. Optional providers can use
        /// the session generation to reject stale callbacks and diagnostics.
        /// Callbacks run on the Unity main thread after the current snapshot has been updated.
        /// Late subscribers must read ActiveFoxRunSubscriptionSessionPolicy immediately after attaching.
        /// </summary>
        public event Action<FoxRunSubscriptionSessionPolicy> FoxRunSubscriptionSessionChanged;

        internal void BeginFoxRunSubscriptionSessionIfNeeded()
        {
            if (!_enableFoxRunInbound
                || _foxRunSubscriptionSessionState.Current.SubscriptionsEnabled)
            {
                return;
            }

            var policy = _foxRunSubscriptionSessionState.BeginIfNeeded(
                DefaultFoxRunSubscriptionSource,
                DefaultFoxRunSubscriptionEncoding,
                DefaultFoxRunNativeSubscribeQos,
                FoxRunRos2NativeCopyBudgetBytes,
                ConfiguredFoxRunSubscriptionMaxMessagesPerSecondPerTopic,
                ConfiguredFoxRunDefaultSubscribeRateHz,
                FoxRunSubscriptionMaxPayloadBytes);
            NotifyFoxRunSubscriptionSessionChanged(policy);
        }

        internal void EndFoxRunSubscriptionSession()
        {
            if (!_foxRunSubscriptionSessionState.Current.SubscriptionsEnabled)
                return;

            var policy = _foxRunSubscriptionSessionState.End();
            NotifyFoxRunSubscriptionSessionChanged(policy);
        }

        private void NotifyFoxRunSubscriptionSessionChanged(
            FoxRunSubscriptionSessionPolicy policy)
        {
            var handlers = FoxRunSubscriptionSessionChanged;
            if (handlers == null)
                return;

            foreach (var subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<FoxRunSubscriptionSessionPolicy>)subscriber)(policy);
                }
                catch (Exception ex)
                {
                    // Notification happens only on begin/end transitions, bounding
                    // failure logging while allowing later observers to run.
                    Debug.LogException(ex);
                }
            }
        }

        private void SyncFoxRunSubscriptionSession()
        {
            if (_enableFoxRunInbound)
                BeginFoxRunSubscriptionSessionIfNeeded();
            else
                EndFoxRunSubscriptionSession();
        }
    }
}
