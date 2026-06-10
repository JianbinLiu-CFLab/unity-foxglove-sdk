// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay

using System;
using System.Linq;

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

        private Action<string, byte[]>[] _replayMessageHandlers = Array.Empty<Action<string, byte[]>>();
        private Action<ReplayMessageContext>[] _replayMessageContextHandlers = Array.Empty<Action<ReplayMessageContext>>();
        private Action<ReplayBatchContext>[] _replayBatchCompletedHandlers = Array.Empty<Action<ReplayBatchContext>>();

        public event Action<string, byte[]> OnReplayMessage
        {
            add { AddHandler(ref _replayMessageHandlers, value); }
            remove { RemoveHandler(ref _replayMessageHandlers, value); }
        }

        public event Action<ReplayMessageContext> OnReplayMessageContext
        {
            add { AddHandler(ref _replayMessageContextHandlers, value); }
            remove { RemoveHandler(ref _replayMessageContextHandlers, value); }
        }

        public event Action<ReplayBatchContext> OnReplayBatchCompleted
        {
            add { AddHandler(ref _replayBatchCompletedHandlers, value); }
            remove { RemoveHandler(ref _replayBatchCompletedHandlers, value); }
        }

        private static void AddHandler<T>(ref T[] cache, T handler) where T : Delegate
        {
            cache = ((Delegate)(object)Delegate.Combine((Delegate)(object)cache, handler))
                .GetInvocationList().Cast<T>().ToArray();
        }

        private static void RemoveHandler<T>(ref T[] cache, T handler) where T : Delegate
        {
            var combined = Delegate.Remove(Delegate.Combine((Delegate)(object)cache), handler);
            cache = combined != null
                ? combined.GetInvocationList().Cast<T>().ToArray()
                : Array.Empty<T>();
        }

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
            var handlers = _replayMessageHandlers;
            foreach (var handler in handlers)
            {
                try { handler(topic, data); }
                catch (Exception ex) { _logger.LogWarning($"Replay message listener failed: {ex.Message}"); }
            }
        }

        private void SafeInvokeReplayMessageContext(ReplayMessageContext context)
        {
            var handlers = _replayMessageContextHandlers;
            foreach (var handler in handlers)
            {
                try { handler(context); }
                catch (Exception ex) { _logger.LogWarning($"Replay message context listener failed: {ex.Message}"); }
            }
        }

        private void SafeInvokeReplayBatchCompleted(ReplayBatchContext context)
        {
            var handlers = _replayBatchCompletedHandlers;
            foreach (var handler in handlers)
            {
                try { handler(context); }
                catch (Exception ex) { _logger.LogWarning($"Replay batch listener failed: {ex.Message}"); }
            }
        }
    }
}
