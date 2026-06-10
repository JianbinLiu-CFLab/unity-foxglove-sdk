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
        private readonly object _replayHandlersGate = new();

        private Action<string, byte[]>[] _replayMessageHandlers = Array.Empty<Action<string, byte[]>>();
        private Action<ReplayMessageContext>[] _replayMessageContextHandlers = Array.Empty<Action<ReplayMessageContext>>();
        private Action<ReplayBatchContext>[] _replayBatchCompletedHandlers = Array.Empty<Action<ReplayBatchContext>>();

        public event Action<string, byte[]> OnReplayMessage
        {
            add { AddHandler(ref _replayMessageHandlers, _replayHandlersGate, value); }
            remove { RemoveHandler(ref _replayMessageHandlers, _replayHandlersGate, value); }
        }

        public event Action<ReplayMessageContext> OnReplayMessageContext
        {
            add { AddHandler(ref _replayMessageContextHandlers, _replayHandlersGate, value); }
            remove { RemoveHandler(ref _replayMessageContextHandlers, _replayHandlersGate, value); }
        }

        public event Action<ReplayBatchContext> OnReplayBatchCompleted
        {
            add { AddHandler(ref _replayBatchCompletedHandlers, _replayHandlersGate, value); }
            remove { RemoveHandler(ref _replayBatchCompletedHandlers, _replayHandlersGate, value); }
        }

        private static void AddHandler<T>(ref T[] cache, object handlersGate, T handler) where T : Delegate
        {
            lock (handlersGate)
            {
                cache = ToTypedHandlerArray<T>(
                    Delegate.Combine(Delegate.Combine((Delegate[])(object)cache), handler));
            }
        }

        private static void RemoveHandler<T>(ref T[] cache, object handlersGate, T handler) where T : Delegate
        {
            lock (handlersGate)
            {
                cache = ToTypedHandlerArray<T>(
                    Delegate.Remove(Delegate.Combine((Delegate[])(object)cache), handler));
            }
        }

        // LINQ-free conversion to keep the replay path free of System.Linq (mirrors 134-3K-2).
        private static T[] ToTypedHandlerArray<T>(Delegate combined) where T : Delegate
        {
            if (combined == null)
                return Array.Empty<T>();

            var invocationList = combined.GetInvocationList();
            var result = new T[invocationList.Length];
            for (var i = 0; i < invocationList.Length; i++)
                result[i] = (T)invocationList[i];
            return result;
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
