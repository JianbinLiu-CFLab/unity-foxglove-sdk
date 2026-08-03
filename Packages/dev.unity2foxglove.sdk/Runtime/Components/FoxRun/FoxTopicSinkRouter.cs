// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Provider-neutral additive topic-sink fanout.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxTopicSinkFault
    {
        public FoxTopicSinkFault(
            string sinkName,
            string topic,
            string operation,
            Exception exception)
        {
            SinkName = sinkName ?? string.Empty;
            Topic = topic ?? string.Empty;
            Operation = operation ?? string.Empty;
            Exception = exception;
        }

        public string SinkName { get; }
        public string Topic { get; }
        public string Operation { get; }
        public Exception Exception { get; }
    }

    /// <summary>
    /// Main-thread additive fanout. Transport Providers use the dedicated
    /// Provider SPI; this router remains available for observers, recorders,
    /// and test sinks that consume an already-serialized payload.
    /// </summary>
    public sealed class FoxTopicSinkRouter : IDisposable
    {
        private readonly List<IFoxTopicSink> _sinks =
            new List<IFoxTopicSink>();
        private readonly Dictionary<string, FoxTopicContract> _contracts =
            new Dictionary<string, FoxTopicContract>(
                StringComparer.Ordinal);
        private readonly Dictionary<string, FoxTopicContract> _wireContracts =
            new Dictionary<string, FoxTopicContract>(
                StringComparer.Ordinal);
        private readonly Dictionary<string, int> _ownerCounts =
            new Dictionary<string, int>(
                StringComparer.Ordinal);
        private readonly HashSet<string> _reportedFaults =
            new HashSet<string>(StringComparer.Ordinal);
        private bool _disposed;

        public event Action<FoxTopicSinkFault> SinkFaulted;

        public int SinkCount => _sinks.Count;
        public bool HasSinks => _sinks.Count > 0;

        public void AddSink(IFoxTopicSink sink)
        {
            ThrowIfDisposed();
            if (sink == null)
                throw new ArgumentNullException(nameof(sink));
            if (_sinks.Contains(sink))
                return;

            var registered = new List<string>();
            try
            {
                foreach (var contract in _contracts.Values)
                {
                    registered.Add(contract.Topic);
                    try
                    {
                        sink.Register(
                            _wireContracts[contract.Topic]);
                    }
                    catch (Exception exception)
                        when (FoxRunExceptionPolicy
                            .IsRecoverable(exception))
                    {
                        ReportFault(
                            sink,
                            contract.Topic,
                            "register",
                            exception);
                    }
                }

                _sinks.Add(sink);
            }
            catch (Exception exception)
            {
                var primary =
                    ExceptionDispatchInfo.Capture(exception);
                RollBackAddedSink(
                    sink,
                    registered);
                primary.Throw();
                throw;
            }
        }

        public bool RemoveSink(IFoxTopicSink sink)
        {
            if (sink == null || !_sinks.Remove(sink))
                return false;
            ExceptionDispatchInfo fatal = null;
            if (sink is IFoxTopicSinkContractLifecycle lifecycle)
            {
                foreach (var topic in _contracts.Keys)
                {
                    try
                    {
                        lifecycle.Unregister(topic);
                    }
                    catch (Exception exception)
                        when (FoxRunExceptionPolicy
                            .IsRecoverable(exception))
                    {
                        ReportFault(
                            sink,
                            topic,
                            "unregister",
                            exception);
                    }
                    catch (Exception exception)
                    {
                        fatal ??=
                            ExceptionDispatchInfo.Capture(
                                exception);
                    }
                }
            }

            try
            {
                sink.Dispose();
            }
            catch (Exception exception)
                when (FoxRunExceptionPolicy
                    .IsRecoverable(exception))
            {
                ReportFault(
                    sink,
                    string.Empty,
                    "dispose",
                    exception);
            }
            catch (Exception exception)
            {
                fatal ??=
                    ExceptionDispatchInfo.Capture(exception);
            }

            fatal?.Throw();
            return true;
        }

        public bool Register(FoxTopicContract contract)
            => Register(
                contract,
                DefaultWireEncoding(contract));

        public bool Register(
            FoxTopicContract contract,
            FoxRunEncoding wireEncoding)
        {
            ThrowIfDisposed();
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (contract.Visibility == FoxTopicVisibility.LocalOnly)
                return false;
            var wireContract =
                contract.ForWireEncoding(wireEncoding);

            if (_contracts.TryGetValue(
                    contract.Topic,
                    out var existing))
            {
                if (!ContractsMatch(existing, contract)
                    || !_wireContracts.TryGetValue(
                        contract.Topic,
                        out var existingWire)
                    || !ContractsMatch(
                        existingWire,
                        wireContract))
                {
                    return false;
                }
                _ownerCounts[contract.Topic]++;
                return true;
            }

            var attempted = new List<IFoxTopicSink>();
            try
            {
                foreach (var sink in _sinks)
                {
                    attempted.Add(sink);
                    try
                    {
                        sink.Register(wireContract);
                    }
                    catch (Exception exception)
                        when (FoxRunExceptionPolicy
                            .IsRecoverable(exception))
                    {
                        ReportFault(
                            sink,
                            contract.Topic,
                            "register",
                            exception);
                    }
                }
            }
            catch (Exception exception)
            {
                var primary =
                    ExceptionDispatchInfo.Capture(exception);
                RollBackRegistration(
                    attempted,
                    contract.Topic);
                primary.Throw();
                throw;
            }

            _contracts.Add(contract.Topic, contract);
            _wireContracts.Add(
                contract.Topic,
                wireContract);
            _ownerCounts.Add(contract.Topic, 1);
            return true;
        }

        public bool Unregister(string topic)
        {
            if (string.IsNullOrEmpty(topic)
                || !_ownerCounts.TryGetValue(
                    topic,
                    out var count))
            {
                return false;
            }

            if (count > 1)
            {
                _ownerCounts[topic] = count - 1;
                return true;
            }

            _ownerCounts.Remove(topic);
            _contracts.Remove(topic);
            _wireContracts.Remove(topic);
            ExceptionDispatchInfo fatal = null;
            foreach (var sink in _sinks)
            {
                if (!(sink
                      is IFoxTopicSinkContractLifecycle lifecycle))
                {
                    continue;
                }

                try
                {
                    lifecycle.Unregister(topic);
                }
                catch (Exception exception)
                    when (FoxRunExceptionPolicy
                        .IsRecoverable(exception))
                {
                    ReportFault(
                        sink,
                        topic,
                        "unregister",
                        exception);
                }
                catch (Exception exception)
                {
                    fatal ??=
                        ExceptionDispatchInfo.Capture(
                            exception);
                }
            }

            fatal?.Throw();
            return true;
        }

        public void Publish(
            FoxTopicContract contract,
            ulong timestampNs,
            byte[] payload,
            string origin)
            => PublishCompatible(
                contract,
                FoxRunEncoding.JSON,
                timestampNs,
                payload,
                origin);

        public void PublishCompatible(
            FoxTopicContract contract,
            FoxRunEncoding encoding,
            ulong timestampNs,
            byte[] payload,
            string origin)
        {
            ThrowIfDisposed();
            if (contract == null
                || contract.Visibility == FoxTopicVisibility.LocalOnly
                || !_contracts.TryGetValue(
                    contract.Topic,
                    out var registered)
                || !ContractsMatch(
                    registered,
                    contract))
            {
                return;
            }

            var wireContract =
                ResolveWireContract(
                    registered,
                    encoding);
            payload ??= Array.Empty<byte>();
            foreach (var sink in _sinks)
            {
                try
                {
                    sink.Publish(
                        wireContract,
                        timestampNs,
                        payload,
                        origin ?? string.Empty);
                }
                catch (Exception exception)
                    when (FoxRunExceptionPolicy
                        .IsRecoverable(exception))
                {
                    ReportFault(
                        sink,
                        contract.Topic,
                        "publish",
                        exception);
                }
            }
        }

        public void Flush()
        {
            if (_disposed)
                return;
            foreach (var sink in _sinks)
            {
                try
                {
                    sink.Flush();
                }
                catch (Exception exception)
                    when (FoxRunExceptionPolicy
                        .IsRecoverable(exception))
                {
                    ReportFault(
                        sink,
                        string.Empty,
                        "flush",
                        exception);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            ExceptionDispatchInfo fatal = null;
            for (var index = _sinks.Count - 1;
                 index >= 0;
                 index--)
            {
                var sink = _sinks[index];
                try
                {
                    sink.Dispose();
                }
                catch (Exception exception)
                    when (FoxRunExceptionPolicy
                        .IsRecoverable(exception))
                {
                    ReportFault(
                        sink,
                        string.Empty,
                        "dispose",
                        exception);
                }
                catch (Exception exception)
                {
                    fatal ??=
                        ExceptionDispatchInfo.Capture(
                            exception);
                }
            }

            _sinks.Clear();
            _contracts.Clear();
            _wireContracts.Clear();
            _ownerCounts.Clear();
            _reportedFaults.Clear();
            SinkFaulted = null;
            fatal?.Throw();
        }

        private static bool ContractsMatch(
            FoxTopicContract left,
            FoxTopicContract right)
            => ReferenceEquals(left, right)
               || (left != null
                   && right != null
                   && string.Equals(
                       left.Topic,
                       right.Topic,
                       StringComparison.Ordinal)
                   && string.Equals(
                       left.SchemaName,
                       right.SchemaName,
                       StringComparison.Ordinal)
                   && string.Equals(
                       left.Encoding,
                       right.Encoding,
                       StringComparison.Ordinal)
                   && string.Equals(
                       left.CanonicalType,
                       right.CanonicalType,
                       StringComparison.Ordinal)
                   && string.Equals(
                       left.StableFingerprint,
                       right.StableFingerprint,
                       StringComparison.Ordinal)
                   && left.Visibility == right.Visibility
                   && left.WriterPolicy == right.WriterPolicy);

        private FoxTopicContract ResolveWireContract(
            FoxTopicContract logicalContract,
            FoxRunEncoding wireEncoding)
        {
            var requested =
                logicalContract.ForWireEncoding(
                    wireEncoding);
            if (!_wireContracts.TryGetValue(
                    logicalContract.Topic,
                    out var registered)
                || !ContractsMatch(
                    registered,
                    requested))
            {
                throw new InvalidOperationException(
                    "FoxRun additive sink wire encoding '"
                    + requested.Encoding
                    + "' does not match the registered wire contract for topic '"
                    + logicalContract.Topic
                    + "'.");
            }

            return registered;
        }

        private static FoxRunEncoding DefaultWireEncoding(
            FoxTopicContract contract)
            => contract != null
               && string.Equals(
                   contract.Encoding,
                   "msgpack",
                   StringComparison.Ordinal)
                ? FoxRunEncoding.MessagePack
                : FoxRunEncoding.JSON;

        private static void RollBackRegistration(
            IReadOnlyList<IFoxTopicSink> sinks,
            string topic)
        {
            for (var index = sinks.Count - 1;
                 index >= 0;
                 index--)
            {
                if (sinks[index]
                    is IFoxTopicSinkContractLifecycle lifecycle)
                {
                    try
                    {
                        lifecycle.Unregister(topic);
                    }
                    catch
                    {
                        // Rollback cannot replace the fatal registration
                        // exception that initiated transaction cleanup.
                    }
                }
            }
        }

        private static void RollBackAddedSink(
            IFoxTopicSink sink,
            IReadOnlyList<string> topics)
        {
            if (!(sink
                  is IFoxTopicSinkContractLifecycle lifecycle))
            {
                return;
            }

            for (var index = topics.Count - 1;
                 index >= 0;
                 index--)
            {
                try
                {
                    lifecycle.Unregister(topics[index]);
                }
                catch
                {
                    // Rollback cannot replace the fatal replay exception.
                }
            }
        }

        private void ReportFault(
            IFoxTopicSink sink,
            string topic,
            string operation,
            Exception exception)
        {
            string sinkName;
            try
            {
                sinkName = sink?.Name
                           ?? string.Empty;
            }
            catch (Exception nameException)
                when (FoxRunExceptionPolicy
                    .IsRecoverable(nameException))
            {
                sinkName = sink?.GetType().FullName
                           ?? string.Empty;
            }
            var key = sinkName
                      + "|"
                      + (topic ?? string.Empty)
                      + "|"
                      + (operation ?? string.Empty)
                      + "|"
                      + exception?.GetType().FullName;
            if (!_reportedFaults.Add(key))
                return;
            var fault = new FoxTopicSinkFault(
                sinkName,
                topic,
                operation,
                exception);
            var observers = SinkFaulted;
            if (observers == null)
                return;
            foreach (Action<FoxTopicSinkFault> observer
                     in observers.GetInvocationList())
            {
                try
                {
                    observer(fault);
                }
                catch (Exception observerException)
                    when (FoxRunExceptionPolicy
                        .IsRecoverable(observerException))
                {
                    // Diagnostics are isolated from routing and from later
                    // diagnostic observers.
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(FoxTopicSinkRouter));
            }
        }

    }
}
