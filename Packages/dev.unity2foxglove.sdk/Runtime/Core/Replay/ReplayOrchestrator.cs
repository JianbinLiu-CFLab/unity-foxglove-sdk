// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay

using System;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Orchestrates attachment/detachment of a <see cref="ReplayController"/> to
    /// a <see cref="FoxgloveSession"/>, forwarding replay messages with exception-safe
    /// invoke wrappers that prevent a single faulty listener from taking down the
    /// entire dispatch.
    /// </summary>
    internal class ReplayOrchestrator
    {
        private readonly IFoxgloveLogger _logger;
        private Action<string, byte[]> _replayForwarder;
        private Action<ReplayMessageContext> _replayContextForwarder;
        private Action<ReplayBatchContext> _replayBatchForwarder;

        public event Action<string, byte[]> OnReplayMessage;
        public event Action<ReplayMessageContext> OnReplayMessageContext;
        public event Action<ReplayBatchContext> OnReplayBatchCompleted;

        /// <summary>
        /// Creates a <see cref="ReplayOrchestrator"/> with the given logger for
        /// diagnostic output in safe-invoke wrappers.
        /// </summary>
        public ReplayOrchestrator(IFoxgloveLogger logger) { _logger = logger; }

        /// <summary>
        /// Registers channels on the session and wires replay message forwarding
        /// from <paramref name="replay"/> to the session. On failure during wiring,
        /// cleans up via <see cref="Detach"/> and re-throws.
        /// </summary>
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

        /// <summary>
        /// Unwires all previously attached replay event forwarders from the given
        /// <paramref name="replay"/> controller. Safe to call multiple times.
        /// </summary>
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
