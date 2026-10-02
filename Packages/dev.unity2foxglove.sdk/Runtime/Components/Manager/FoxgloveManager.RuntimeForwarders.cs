// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Owns the production runtime callback wiring shared by server startup and boundary tests.

using System;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        internal void AttachRuntimeForwarders(FoxgloveSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            _runtimeForwarderSession = session;
            _replayForwarder = InvokeReplayMessageSubscribers;
            _replayContextForwarder = InvokeReplayMessageContextSubscribers;
            _replayBatchForwarder = InvokeReplayBatchSubscribers;
            _runtime.OnReplayMessage += _replayForwarder;
            _runtime.OnReplayMessageContext += _replayContextForwarder;
            _runtime.OnReplayBatchCompleted += _replayBatchForwarder;

            var generation = AdvanceComponentPublisherSession(
                () =>
                {
                    AdvanceChannelSessionGeneration();
                    return _connectionState.ChannelSessionGeneration;
                },
                _clientEventAdmission.Activate);
            var transport = session.Transport;
            if (transport == null)
                return;

            _clientConnectedForwarder = id =>
                EnqueueClientLifecycleEvent(ClientEvent.Connect(generation, id));
            _clientDisconnectedForwarder = id =>
                EnqueueClientLifecycleEvent(ClientEvent.Disconnect(generation, id));
            _clientMessageForwarder = (cid, chId, topic, encoding, payload) =>
                EnqueueClientMessageEvent(ClientEvent.Message(
                    generation, cid, chId, topic, encoding, payload));
            transport.OnClientConnected += _clientConnectedForwarder;
            transport.OnClientDisconnected += _clientDisconnectedForwarder;
            // The runtime publishes Session only after this setup callback
            // returns. Subscribe to the freshly-created session directly so
            // the callback can run before the transport listener starts.
            session.OnClientMessageWithEncoding += _clientMessageForwarder;
        }

    }
}
