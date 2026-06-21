// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Fans one FoxRun topic payload out to additional registered sinks.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>One isolated sink failure surfaced for diagnostics.</summary>
    public sealed class FoxTopicSinkFault
    {
        public FoxTopicSinkFault(string sinkName, string topic, string operation, Exception exception)
        {
            SinkName = sinkName ?? string.Empty;
            Topic = topic ?? string.Empty;
            Operation = operation ?? string.Empty;
            Exception = exception;
        }

        public string SinkName { get; }
        public string Topic { get; }
        /// <summary>One of "register", "publish", or "flush".</summary>
        public string Operation { get; }
        public Exception Exception { get; }
    }

    /// <summary>
    /// Routes an exported FoxRun topic payload to every additional registered
    /// <see cref="IFoxTopicSink"/>.
    /// </summary>
    /// <remarks>
    /// This boundary is additive: live Foxglove and MCAP recording keep their
    /// existing primary publish paths, so a sink failure here can never break
    /// live output. Local-only contracts are never exported. Payloads are passed
    /// through by reference so all sinks share one serialized buffer. Not
    /// thread-safe; register and publish on the Unity main thread only.
    /// </remarks>
    public sealed class FoxTopicSinkRouter : IDisposable
    {
        private readonly List<IFoxTopicSink> _sinks = new List<IFoxTopicSink>();
        private readonly Dictionary<string, FoxTopicContract> _contracts = new Dictionary<string, FoxTopicContract>(StringComparer.Ordinal);
        private readonly HashSet<string> _reportedFaults = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Raised once per unique (sink, topic, operation, exception type) failure.</summary>
        public event Action<FoxTopicSinkFault> SinkFaulted;

        /// <summary>Number of registered sinks.</summary>
        public int SinkCount => _sinks.Count;

        /// <summary>Whether any sink is registered.</summary>
        public bool HasSinks => _sinks.Count > 0;

        /// <summary>
        /// Add a sink. Duplicate references are ignored and order is preserved.
        /// Contracts registered before this sink was added are replayed so the
        /// sink can be attached at any time.
        /// </summary>
        public void AddSink(IFoxTopicSink sink)
        {
            if (sink == null)
                throw new ArgumentNullException(nameof(sink));
            if (_sinks.Contains(sink))
                return;

            _sinks.Add(sink);
            foreach (var contract in _contracts.Values)
            {
                try
                {
                    sink.Register(contract);
                }
                catch (Exception ex)
                {
                    ReportFault(sink, contract.Topic, "register", ex);
                }
            }
        }

        /// <summary>Remove a sink. Returns whether it was present.</summary>
        public bool RemoveSink(IFoxTopicSink sink)
            => sink != null && _sinks.Remove(sink);

        /// <summary>Remove a previously registered exported contract.</summary>
        public bool Unregister(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
                return false;

            return _contracts.Remove(topic);
        }

        /// <summary>
        /// Register an exported contract with every sink. Local-only contracts
        /// are not exported and are skipped.
        /// </summary>
        public void Register(FoxTopicContract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (contract.Visibility == FoxTopicVisibility.LocalOnly)
                return;

            _contracts[contract.Topic] = contract;
            for (var i = 0; i < _sinks.Count; i++)
            {
                var sink = _sinks[i];
                try
                {
                    sink.Register(contract);
                }
                catch (Exception ex)
                {
                    ReportFault(sink, contract.Topic, "register", ex);
                }
            }
        }

        /// <summary>
        /// Deliver one serialized payload to every sink in deterministic order.
        /// Local-only contracts are not exported. A failing sink is isolated and
        /// does not stop the remaining sinks.
        /// </summary>
        public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (contract.Visibility == FoxTopicVisibility.LocalOnly)
                return;
            if (_sinks.Count == 0)
                return;

            payload ??= Array.Empty<byte>();
            for (var i = 0; i < _sinks.Count; i++)
            {
                var sink = _sinks[i];
                try
                {
                    sink.Publish(contract, timestampNs, payload, origin);
                }
                catch (Exception ex)
                {
                    ReportFault(sink, contract.Topic, "publish", ex);
                }
            }
        }

        /// <summary>Flush every sink. A failing sink is isolated.</summary>
        public void Flush()
        {
            for (var i = 0; i < _sinks.Count; i++)
            {
                var sink = _sinks[i];
                try
                {
                    sink.Flush();
                }
                catch (Exception ex)
                {
                    ReportFault(sink, string.Empty, "flush", ex);
                }
            }
        }

        /// <summary>Dispose every sink and clear the router.</summary>
        public void Dispose()
        {
            for (var i = 0; i < _sinks.Count; i++)
            {
                try
                {
                    _sinks[i].Dispose();
                }
                catch
                {
                    // Best-effort teardown; a sink that throws on dispose must not
                    // block the remaining sinks.
                }
            }

            _sinks.Clear();
            _contracts.Clear();
            _reportedFaults.Clear();
        }

        private void ReportFault(IFoxTopicSink sink, string topic, string operation, Exception exception)
        {
            var name = sink?.Name ?? string.Empty;
            var key = name + ":" + topic + ":" + operation + ":" + (exception?.GetType().FullName ?? string.Empty);
            if (!_reportedFaults.Add(key))
                return;

            SinkFaulted?.Invoke(new FoxTopicSinkFault(name, topic, operation, exception));
        }
    }
}
