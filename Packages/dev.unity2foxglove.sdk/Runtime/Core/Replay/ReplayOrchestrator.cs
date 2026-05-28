// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay

using System;

namespace Unity.FoxgloveSDK.Core
{
    internal class ReplayOrchestrator
    {
        private readonly IFoxgloveLogger _logger;
        private Action<string, byte[]> _replayForwarder;
        private Action<ReplayMessageContext> _replayContextForwarder;
        private Action<ReplayBatchContext> _replayBatchForwarder;

        public event Action<string, byte[]> OnReplayMessage;
        public event Action<ReplayMessageContext> OnReplayMessageContext;
        public event Action<ReplayBatchContext> OnReplayBatchCompleted;

        public ReplayOrchestrator(IFoxgloveLogger logger) { _logger = logger; }

        public void Attach(ReplayController replay, FoxgloveSession session)
        {
            replay.RegisterChannels(session);
            Action<string, byte[]> replayForwarder = SafeInvokeReplayMessage;
            Action<ReplayMessageContext> replayContextForwarder = SafeInvokeReplayMessageContext;
            Action<ReplayBatchContext> replayBatchForwarder = SafeInvokeReplayBatchCompleted;
            _replayForwarder = replayForwarder;
            _replayContextForwarder = replayContextForwarder;
            _replayBatchForwarder = replayBatchForwarder;
            try
            {
                replay.OnReplayMessage += replayForwarder;
                replay.OnReplayMessageContext += replayContextForwarder;
                replay.OnReplayBatchCompleted += replayBatchForwarder;
            }
            catch
            {
                Detach(replay);
                throw;
            }
        }

        public void Detach(ReplayController replay)
        {
            if (_replayForwarder != null) { replay.OnReplayMessage -= _replayForwarder; _replayForwarder = null; }
            if (_replayContextForwarder != null) { replay.OnReplayMessageContext -= _replayContextForwarder; _replayContextForwarder = null; }
            if (_replayBatchForwarder != null) { replay.OnReplayBatchCompleted -= _replayBatchForwarder; _replayBatchForwarder = null; }
        }

        private void SafeInvokeReplayMessage(string topic, byte[] data)
        {
            var handlers = OnReplayMessage;
            if (handlers == null)
                return;

            foreach (Action<string, byte[]> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(topic, data);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Replay message listener failed: {ex.Message}");
                }
            }
        }

        private void SafeInvokeReplayMessageContext(ReplayMessageContext context)
        {
            var handlers = OnReplayMessageContext;
            if (handlers == null)
                return;

            foreach (Action<ReplayMessageContext> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(context);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Replay message context listener failed: {ex.Message}");
                }
            }
        }

        private void SafeInvokeReplayBatchCompleted(ReplayBatchContext context)
        {
            var handlers = OnReplayBatchCompleted;
            if (handlers == null)
                return;

            foreach (Action<ReplayBatchContext> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(context);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Replay batch listener failed: {ex.Message}");
                }
            }
        }
    }
}
