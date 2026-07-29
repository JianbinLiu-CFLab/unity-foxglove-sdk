// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Fans one FoxRun topic payload out to additional registered sinks.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

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
        /// <summary>One of "register", "unregister", "publish", or "flush".</summary>
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
        private readonly Dictionary<string, FoxRunEndpoint> _contractTargets = new Dictionary<string, FoxRunEndpoint>(StringComparer.Ordinal);
        private readonly Dictionary<string, FoxRunResolvedPublishContract> _resolvedContracts =
            new Dictionary<string, FoxRunResolvedPublishContract>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _contractOwnerCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _reportedFaults = new HashSet<string>(StringComparer.Ordinal);
        private bool _disposed;

        /// <summary>Raised once per unique (sink, topic, operation, exception type) failure.</summary>
        public event Action<FoxTopicSinkFault> SinkFaulted;

        /// <summary>Number of registered sinks.</summary>
        public int SinkCount => _sinks.Count;

        /// <summary>Whether any sink is registered.</summary>
        public bool HasSinks => _sinks.Count > 0;

        public bool HasReadyTarget(FoxRunEndpoint target, FoxTopicContract contract)
        {
            if (_disposed
                || contract == null
                || !_contracts.TryGetValue(contract.Topic, out var registered)
                || !ContractsMatch(registered, contract)
                || !_contractTargets.TryGetValue(contract.Topic, out var targets)
                || (targets & target) == 0)
                return false;
            for (var index = 0; index < _sinks.Count; index++)
            {
                var sink = _sinks[index];
                if (sink is IFoxTopicTargetSink targeted)
                {
                    if (targeted.Target != target)
                        continue;
                    try
                    {
                        if (targeted.IsReady(contract, out _))
                            return true;
                    }
                    catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                    {
                        ReportFault(sink, contract.Topic, "readiness", ex);
                    }
                }
                else if (target == FoxRunEndpoint.Ros2Native)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Add a sink. Duplicate references are ignored and order is preserved.
        /// Contracts registered before this sink was added are replayed so the
        /// sink can be attached at any time.
        /// </summary>
        public void AddSink(IFoxTopicSink sink)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FoxTopicSinkRouter));
            if (sink == null)
                throw new ArgumentNullException(nameof(sink));
            if (_sinks.Contains(sink))
                return;

            var attemptedContracts = new List<FoxTopicContract>();
            try
            {
                foreach (var contract in _contracts.Values)
                {
                    if (!_contractTargets.TryGetValue(contract.Topic, out var targets)
                        || !SelectsSink(targets, sink))
                        continue;
                    attemptedContracts.Add(contract);
                    _resolvedContracts.TryGetValue(contract.Topic, out var resolved);
                    try
                    {
                        RegisterSink(sink, contract, resolved);
                    }
                    catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                    {
                        ReportFault(sink, contract.Topic, "register", ex);
                    }
                }

                _sinks.Add(sink);
            }
            catch (Exception exception)
            {
                var primary = ExceptionDispatchInfo.Capture(exception);
                RollbackAddedSink(sink, attemptedContracts);
                primary.Throw();
                throw;
            }
        }

        /// <summary>Remove a sink. Returns whether it was present.</summary>
        public bool RemoveSink(IFoxTopicSink sink)
        {
            if (_disposed || sink == null || !_sinks.Contains(sink))
                return false;

            ExceptionDispatchInfo fatal = null;
            if (sink is IFoxTopicSinkContractLifecycle lifecycle)
            {
                foreach (var contract in _contracts.Values)
                {
                    if (!_contractTargets.TryGetValue(
                            contract.Topic,
                            out var targets)
                        || !SelectsSink(targets, sink))
                    {
                        continue;
                    }

                    try
                    {
                        lifecycle.Unregister(contract.Topic);
                    }
                    catch (Exception exception) when (
                        FoxRunExceptionPolicy.IsRecoverable(exception))
                    {
                        try
                        {
                            ReportFault(
                                sink,
                                contract.Topic,
                                "unregister",
                                exception);
                        }
                        catch (Exception notificationException)
                        {
                            fatal ??= ExceptionDispatchInfo.Capture(
                                notificationException);
                        }
                    }
                    catch (Exception exception)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(exception);
                    }
                }
            }

            _sinks.Remove(sink);
            fatal?.Throw();
            return true;
        }

        /// <summary>
        /// Remove a previously registered exported contract and notify sinks
        /// that opt into per-contract lifecycle ownership.
        /// </summary>
        public bool Unregister(string topic)
        {
            if (_disposed)
                return false;
            if (string.IsNullOrWhiteSpace(topic))
                return false;
            if (!_contracts.TryGetValue(topic, out var registered))
                return false;
            if (registered.WriterPolicy == FoxTopicWriterPolicy.MultiWriter
                && _contractOwnerCounts.TryGetValue(topic, out var ownerCount)
                && ownerCount > 1)
            {
                _contractOwnerCounts[topic] = ownerCount - 1;
                return true;
            }

            _contracts.Remove(topic);
            _contractTargets.TryGetValue(topic, out var targets);
            _contractTargets.Remove(topic);
            _resolvedContracts.Remove(topic);
            _contractOwnerCounts.Remove(topic);

            ExceptionDispatchInfo fatal = null;
            for (var i = 0; i < _sinks.Count; i++)
            {
                var sink = _sinks[i];
                if (!SelectsSink(targets, sink)
                    || !(sink is IFoxTopicSinkContractLifecycle lifecycle))
                    continue;

                try
                {
                    lifecycle.Unregister(topic);
                }
                catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                {
                    try
                    {
                        ReportFault(sink, topic, "unregister", ex);
                    }
                    catch (Exception notificationException)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(
                            notificationException);
                    }
                }
                catch (Exception ex)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(ex);
                }
            }

            fatal?.Throw();
            return true;
        }

        /// <summary>
        /// Register an exported contract with every sink. Local-only contracts
        /// are not exported and are skipped.
        /// </summary>
        public bool Register(FoxTopicContract contract)
            => RegisterTargets(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                contract);

        /// <summary>
        /// Register a contract only with sinks selected by the frozen publish
        /// targets. Foxglove-only declarations do not create native or Bridge
        /// endpoints.
        /// </summary>
        public bool RegisterTargets(FoxRunEndpoint targets, FoxTopicContract contract)
            => RegisterTargetsCore(targets, contract, resolved: null);

        /// <summary>
        /// Register a contract with the exact immutable session-resolved target
        /// and QoS policy.
        /// </summary>
        public bool RegisterTargets(
            FoxRunResolvedPublishContract resolved,
            FoxTopicContract contract)
        {
            if (resolved == null)
                throw new ArgumentNullException(nameof(resolved));
            return RegisterTargetsCore(resolved.Targets, contract, resolved);
        }

        private bool RegisterTargetsCore(
            FoxRunEndpoint targets,
            FoxTopicContract contract,
            FoxRunResolvedPublishContract resolved)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FoxTopicSinkRouter));
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (contract.Visibility == FoxTopicVisibility.LocalOnly)
                return false;

            targets &= FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge;
            if (_contracts.TryGetValue(contract.Topic, out var existing))
            {
                _contractTargets.TryGetValue(contract.Topic, out var existingTargets);
                _resolvedContracts.TryGetValue(contract.Topic, out var existingResolved);
                if (!ContractsMatch(existing, contract)
                    || existingTargets != targets
                    || !ResolvedContractsMatch(existingResolved, resolved))
                {
                    return false;
                }

                // An identical public MultiWriter shares one sink endpoint.
                // Its fresh generated contract object remains valid through
                // semantic identity; do not duplicate endpoint registration.
                if (contract.WriterPolicy == FoxTopicWriterPolicy.MultiWriter)
                    _contractOwnerCounts[contract.Topic] =
                        _contractOwnerCounts[contract.Topic] + 1;
                return true;
            }

            var attemptedSinks = new List<IFoxTopicSink>();
            try
            {
                _contracts.Add(contract.Topic, contract);
                _contractTargets.Add(contract.Topic, targets);
                _contractOwnerCounts.Add(contract.Topic, 1);
                if (resolved != null)
                    _resolvedContracts.Add(contract.Topic, resolved);
                for (var i = 0; i < _sinks.Count; i++)
                {
                    var sink = _sinks[i];
                    if (!SelectsSink(targets, sink))
                        continue;
                    attemptedSinks.Add(sink);
                    try
                    {
                        RegisterSink(sink, contract, resolved);
                    }
                    catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                    {
                        ReportFault(sink, contract.Topic, "register", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                var primary = ExceptionDispatchInfo.Capture(ex);
                RollbackRegistration(contract.Topic, attemptedSinks);
                primary.Throw();
                throw;
            }
            return true;
        }

        /// <summary>
        /// Deliver one serialized payload to every sink in deterministic order.
        /// Local-only contracts are not exported. A failing sink is isolated and
        /// does not stop the remaining sinks.
        /// </summary>
        public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin)
            => PublishCore(
                contract,
                contract,
                timestampNs,
                payload,
                origin,
                compatibleOnly: false);

        /// <summary>
        /// Deliver one serialized payload only to additive byte sinks. Target
        /// transport sinks are excluded because their typed/transport-specific
        /// path is owned by <see cref="PublishTarget"/>.
        /// </summary>
        public void PublishCompatible(
            FoxTopicContract contract,
            FoxRunEncoding wireEncoding,
            ulong timestampNs,
            byte[] payload,
            string origin)
            => PublishCore(
                contract,
                ResolveWireContract(contract, wireEncoding),
                timestampNs,
                payload,
                origin,
                compatibleOnly: true);

        private void PublishCore(
            FoxTopicContract registeredContract,
            FoxTopicContract wireContract,
            ulong timestampNs,
            byte[] payload,
            string origin,
            bool compatibleOnly)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FoxTopicSinkRouter));
            if (registeredContract == null)
                throw new ArgumentNullException(nameof(registeredContract));
            if (wireContract == null)
                throw new ArgumentNullException(nameof(wireContract));
            if (registeredContract.Visibility == FoxTopicVisibility.LocalOnly)
                return;
            if (_sinks.Count == 0)
                return;
            if (!_contracts.TryGetValue(
                    registeredContract.Topic,
                    out var registered)
                || !ContractsMatch(registered, registeredContract))
                return;
            _contractTargets.TryGetValue(registeredContract.Topic, out var targets);

            payload ??= Array.Empty<byte>();
            for (var i = 0; i < _sinks.Count; i++)
            {
                var sink = _sinks[i];
                if (compatibleOnly && sink is IFoxTopicTargetSink)
                    continue;
                if (!SelectsSink(targets, sink))
                    continue;
                try
                {
                    sink.Publish(wireContract, timestampNs, payload, origin);
                }
                catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                {
                    ReportFault(sink, registeredContract.Topic, "publish", ex);
                }
            }
        }

        private static FoxTopicContract ResolveWireContract(
            FoxTopicContract logicalContract,
            FoxRunEncoding wireEncoding)
        {
            if (logicalContract == null)
                throw new ArgumentNullException(nameof(logicalContract));

            var protocolEncoding =
                FoxRunEncodingResolver.ToProtocolEncoding(wireEncoding);
            var schemaName = wireEncoding == FoxRunEncoding.MessagePack
                ? string.Empty
                : logicalContract.SchemaName;
            return new FoxTopicContract(
                logicalContract.Topic,
                schemaName,
                protocolEncoding,
                logicalContract.CanonicalType,
                logicalContract.StableFingerprint,
                logicalContract.Visibility,
                logicalContract.WriterPolicy);
        }

        public FoxTopicSinkPublishResult PublishTarget(
            FoxRunEndpoint target,
            FoxTopicContract contract,
            ulong timestampNs,
            byte[] payload,
            string origin)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FoxTopicSinkRouter));
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (contract.Visibility == FoxTopicVisibility.LocalOnly
                || !_contracts.TryGetValue(contract.Topic, out var registered)
                || !ContractsMatch(registered, contract)
                || !_contractTargets.TryGetValue(contract.Topic, out var targets)
                || (targets & target) == 0)
                return default;

            payload ??= Array.Empty<byte>();
            var hadReady = false;
            var succeeded = false;
            for (var index = 0; index < _sinks.Count; index++)
            {
                var sink = _sinks[index];
                if (sink is IFoxTopicTargetSink targeted)
                {
                    if (targeted.Target != target)
                        continue;
                    try
                    {
                        if (!targeted.IsReady(contract, out _))
                            continue;
                        hadReady = true;
                        if (targeted.TryPublish(contract, timestampNs, payload, origin, out _))
                            succeeded = true;
                        else
                            ReportFault(
                                sink,
                                contract.Topic,
                                "publish",
                                new InvalidOperationException("Target sink rejected the payload."));
                    }
                    catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                    {
                        hadReady = true;
                        ReportFault(sink, contract.Topic, "publish", ex);
                    }
                }
                else if (target == FoxRunEndpoint.Ros2Native)
                {
                    hadReady = true;
                    try
                    {
                        sink.Publish(contract, timestampNs, payload, origin);
                        succeeded = true;
                    }
                    catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                    {
                        ReportFault(sink, contract.Topic, "publish", ex);
                    }
                }
            }

            return new FoxTopicSinkPublishResult(hadReady, succeeded);
        }

        /// <summary>Flush every sink. A failing sink is isolated.</summary>
        public void Flush()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FoxTopicSinkRouter));
            for (var i = 0; i < _sinks.Count; i++)
            {
                var sink = _sinks[i];
                try
                {
                    sink.Flush();
                }
                catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                {
                    ReportFault(sink, string.Empty, "flush", ex);
                }
            }
        }

        /// <summary>Dispose every sink and clear the router.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            ExceptionDispatchInfo fatal = null;
            for (var i = 0; i < _sinks.Count; i++)
            {
                try
                {
                    _sinks[i].Dispose();
                }
                catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                {
                    // Best-effort teardown; a sink that throws on dispose must not
                    // block the remaining sinks.
                }
                catch (Exception ex)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(ex);
                }
            }

            _sinks.Clear();
            _contracts.Clear();
            _contractTargets.Clear();
            _resolvedContracts.Clear();
            _contractOwnerCounts.Clear();
            _reportedFaults.Clear();
            SinkFaulted = null;
            fatal?.Throw();
        }

        private void RollbackRegistration(
            string topic,
            IReadOnlyList<IFoxTopicSink> attemptedSinks)
        {
            _contracts.Remove(topic);
            _contractTargets.Remove(topic);
            _resolvedContracts.Remove(topic);
            _contractOwnerCounts.Remove(topic);
            for (var index = attemptedSinks.Count - 1; index >= 0; index--)
            {
                if (!(attemptedSinks[index] is IFoxTopicSinkContractLifecycle lifecycle))
                    continue;
                try
                {
                    lifecycle.Unregister(topic);
                }
                catch
                {
                    // A rollback failure cannot replace the fatal registration
                    // exception that initiated this transaction cleanup.
                }
            }
        }

        private void RollbackAddedSink(
            IFoxTopicSink sink,
            IReadOnlyList<FoxTopicContract> attemptedContracts)
        {
            _sinks.Remove(sink);
            if (!(sink is IFoxTopicSinkContractLifecycle lifecycle))
                return;

            for (var index = attemptedContracts.Count - 1; index >= 0; index--)
            {
                try
                {
                    lifecycle.Unregister(attemptedContracts[index].Topic);
                }
                catch
                {
                    // A rollback failure cannot replace the fatal replay
                    // exception that initiated this transaction cleanup.
                }
            }
        }

        private void ReportFault(IFoxTopicSink sink, string topic, string operation, Exception exception)
        {
            var name = sink?.Name ?? string.Empty;
            var key = name + ":" + topic + ":" + operation + ":" + (exception?.GetType().FullName ?? string.Empty);
            if (!_reportedFaults.Add(key))
                return;

            var handlers = SinkFaulted;
            if (handlers == null)
                return;
            var fault = new FoxTopicSinkFault(
                name,
                topic,
                operation,
                exception);
            foreach (Action<FoxTopicSinkFault> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(fault);
                }
                catch (Exception diagnosticException) when (
                    FoxRunExceptionPolicy.IsRecoverable(diagnosticException))
                {
                    // One diagnostic observer cannot interrupt later observers
                    // or the sink isolation path.
                }
            }
        }

        private static bool SelectsSink(FoxRunEndpoint targets, IFoxTopicSink sink)
        {
            if (sink is IFoxTopicTargetSink targeted)
                return (targets & targeted.Target) != 0;
            return (targets & FoxRunEndpoint.Ros2Native) != 0;
        }

        private static void RegisterSink(
            IFoxTopicSink sink,
            FoxTopicContract contract,
            FoxRunResolvedPublishContract resolved)
        {
            if (resolved != null && sink is IFoxTopicResolvedContractSink resolvedSink)
                resolvedSink.Register(contract, resolved);
            else
                sink.Register(contract);
        }

        private static bool ContractsMatch(
            FoxTopicContract left,
            FoxTopicContract right)
            => left != null
               && right != null
               && string.Equals(left.Topic, right.Topic, StringComparison.Ordinal)
               && string.Equals(left.StableFingerprint, right.StableFingerprint, StringComparison.Ordinal)
               && string.Equals(left.SchemaName, right.SchemaName, StringComparison.Ordinal)
               && string.Equals(left.Encoding, right.Encoding, StringComparison.Ordinal)
               && string.Equals(left.CanonicalType, right.CanonicalType, StringComparison.Ordinal)
               && left.Visibility == right.Visibility
               && left.WriterPolicy == right.WriterPolicy;

        private static bool ResolvedContractsMatch(
            FoxRunResolvedPublishContract left,
            FoxRunResolvedPublishContract right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            return left.Targets == right.Targets
                   && left.FoxgloveEncoding == right.FoxgloveEncoding
                   && left.NativeQos == right.NativeQos
                   && left.BridgeQos == right.BridgeQos;
        }
    }
}
