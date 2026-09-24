// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Owns replay subscriber events and failure-isolated forwarding.

using System;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private readonly ReplaySubscriberFanoutState<Action<string, byte[]>> _replayMessageSubscribers =
            new ReplaySubscriberFanoutState<Action<string, byte[]>>();
        private readonly ReplaySubscriberFanoutState<Action<ReplayMessageContext>> _replayMessageContextSubscribers =
            new ReplaySubscriberFanoutState<Action<ReplayMessageContext>>();
        private readonly ReplaySubscriberFanoutState<Action<ReplayBatchContext>> _replayBatchSubscribers =
            new ReplaySubscriberFanoutState<Action<ReplayBatchContext>>();

        /// <summary>Fires when a replay message is forwarded on the main thread.</summary>
        public event Action<string, byte[]> OnReplayMessage
        {
            add => _replayMessageSubscribers.Add(value);
            remove => _replayMessageSubscribers.Remove(value);
        }

        /// <summary>Fires when replay data is forwarded with channel, schema, and log-time context.</summary>
        public event Action<ReplayMessageContext> OnReplayMessageContext
        {
            add => _replayMessageContextSubscribers.Add(value);
            remove => _replayMessageContextSubscribers.Remove(value);
        }

        /// <summary>Fires after a replay batch has been forwarded to scene listeners.</summary>
        public event Action<ReplayBatchContext> OnReplayBatchCompleted
        {
            add => _replayBatchSubscribers.Add(value);
            remove => _replayBatchSubscribers.Remove(value);
        }

        private void InvokeReplayMessageSubscribers(string topic, byte[] data)
        {
            _replayMessageSubscribers.Invoke(
                handler => handler(topic, data),
                ex => UnityEngine.Debug.LogWarning("[Foxglove] Replay message listener failed: " + ex.Message));
        }

        private void InvokeReplayMessageContextSubscribers(ReplayMessageContext context)
        {
            _replayMessageContextSubscribers.Invoke(
                handler => handler(context),
                ex => UnityEngine.Debug.LogWarning("[Foxglove] Replay message context listener failed: " + ex.Message));
        }

        private void InvokeReplayBatchSubscribers(ReplayBatchContext context)
        {
            _replayBatchSubscribers.Invoke(
                handler => handler(context),
                ex => UnityEngine.Debug.LogWarning("[Foxglove] Replay batch listener failed: " + ex.Message));
        }
    }
}
