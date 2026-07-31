// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: One-reader/one-writer bounded U2R2 v2 connection owner.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed class Ros2BridgeConnection : IDisposable
    {
        private readonly object _gate = new object();
        private readonly IRos2BridgeSessionTransport _transport;
        private readonly U2R2ProtocolLimits _limits;
        private readonly bool _requiresSubscription;
        private readonly int _writerCapacity;
        private readonly int _pendingCapacity;
        private readonly int _timeoutMs;
        private readonly Queue<PendingRequest> _writerQueue =
            new Queue<PendingRequest>();
        private readonly Dictionary<ulong, PendingRequest> _pending =
            new Dictionary<ulong, PendingRequest>();
        private readonly AutoResetEvent _writerSignal =
            new AutoResetEvent(false);
        private readonly U2R2RequestIdCounter _requestIds =
            new U2R2RequestIdCounter();

        private Thread _reader;
        private Thread _writer;
        private Ros2BridgeV2SessionSnapshot _snapshot;
        private Ros2BridgeSessionLifecycleState _lifecycleState;
        private Exception _lastFault;
        private bool _stopRequested;
        private bool _disposeRequested;
        private int _workersRemaining;
        private int _resourcesDisposed;
        private int _readerManagedThreadId;
        private int _writerManagedThreadId;

        internal Ros2BridgeConnection(
            IRos2BridgeSessionTransport transport,
            U2R2ProtocolLimits limits,
            bool requiresSubscription,
            int writerCapacity,
            int pendingCapacity,
            int timeoutMs)
        {
            _transport = transport
                ?? throw new ArgumentNullException(nameof(transport));
            _limits = limits
                ?? throw new ArgumentNullException(nameof(limits));
            if (writerCapacity <= 0
                || checked((ulong)writerCapacity)
                > limits.MaxTotalQueueDepth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(writerCapacity));
            }
            if (pendingCapacity <= 0
                || checked((ulong)pendingCapacity)
                > limits.MaxOutstandingRequests)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pendingCapacity));
            }
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            _requiresSubscription = requiresSubscription;
            _writerCapacity = writerCapacity;
            _pendingCapacity = pendingCapacity;
            _timeoutMs = timeoutMs;
            _lifecycleState =
                Ros2BridgeSessionLifecycleState.Stopped;
        }

        internal Ros2BridgeSessionLifecycleState LifecycleState
        {
            get
            {
                lock (_gate)
                    return _lifecycleState;
            }
        }

        internal Exception LastFault
        {
            get
            {
                lock (_gate)
                    return _lastFault;
            }
        }

        internal int ReaderManagedThreadId
            => Volatile.Read(ref _readerManagedThreadId);

        internal int WriterManagedThreadId
            => Volatile.Read(ref _writerManagedThreadId);

        internal Ros2BridgeV2SessionSnapshot Start()
        {
            Ros2BridgeV2Request hello;
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                if (_lifecycleState
                    != Ros2BridgeSessionLifecycleState.Stopped)
                {
                    throw new InvalidOperationException(
                        "The Bridge connection has already started.");
                }
                if (!_transport.IsConnected)
                {
                    throw new InvalidOperationException(
                        "The Bridge session transport is not connected.");
                }

                _transport.BeginV2(_limits, _timeoutMs);
                _stopRequested = false;
                _lastFault = null;
                _lifecycleState =
                    Ros2BridgeSessionLifecycleState.AwaitingHandshake;
                _workersRemaining = 2;
                _reader = new Thread(ReaderEntry)
                {
                    IsBackground = true,
                    Name = "Unity2Foxglove ROS2 Bridge reader",
                };
                _writer = new Thread(WriterEntry)
                {
                    IsBackground = true,
                    Name = "Unity2Foxglove ROS2 Bridge writer",
                };
                _reader.Start();
                _writer.Start();
                hello = Ros2BridgeV2SessionCodec.CreateHello(
                    _requestIds.Next(),
                    _requiresSubscription,
                    _limits);
            }

            byte[] response;
            try
            {
                response = EnqueueAndWait(
                    hello,
                    EffectiveTimeout(
                        _timeoutMs,
                        _limits.HandshakeTimeoutMs));
                var snapshot =
                    Ros2BridgeV2SessionCodec.AcceptHello(
                        hello,
                        response,
                        _limits);
                lock (_gate)
                {
                    if (_lifecycleState
                        == Ros2BridgeSessionLifecycleState.Faulted)
                    {
                        ExceptionDispatchInfo.Capture(
                            _lastFault
                            ?? new U2R2ProtocolException(
                                "invalid_frame",
                                "The Bridge connection faulted during its handshake."))
                            .Throw();
                    }
                    if (_stopRequested)
                    {
                        throw new ObjectDisposedException(
                            nameof(Ros2BridgeConnection));
                    }
                    _snapshot = snapshot;
                    _lifecycleState =
                        Ros2BridgeSessionLifecycleState.Ready;
                    return snapshot;
                }
            }
            catch (Exception exception)
            {
                Fault(exception);
                throw;
            }
        }

        internal U2R2Message Exchange(
            Func<
                ulong,
                Ros2BridgeV2SessionSnapshot,
                Ros2BridgeV2Request> requestFactory,
            int timeoutMs)
        {
            if (requestFactory == null)
                throw new ArgumentNullException(nameof(requestFactory));
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            Ros2BridgeV2SessionSnapshot snapshot;
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                if (_lifecycleState
                    != Ros2BridgeSessionLifecycleState.Ready
                    || _snapshot == null)
                {
                    throw new InvalidOperationException(
                        "Normal Bridge work requires a correlated hello_ack.");
                }
                snapshot = _snapshot;
            }

            var requestId = _requestIds.Next();
            var request = requestFactory(requestId, snapshot)
                ?? throw new InvalidOperationException(
                    "The Bridge request factory returned null.");
            if (request.Expectation.RequestId != requestId
                || request.Expectation.AssignsSessionIdentity
                || !string.Equals(
                    request.Expectation.SessionId,
                    snapshot.SessionId,
                    StringComparison.Ordinal)
                || request.Expectation.ConnectionGeneration
                != snapshot.ConnectionGeneration)
            {
                throw new U2R2ProtocolException(
                    "invalid_configuration",
                    "The Bridge request does not target the active session.",
                    terminal: true);
            }

            var response = EnqueueAndWait(request, timeoutMs);
            return U2R2ProtocolCodec.ParseV2(
                U2R2ProtocolCodec.DecodeFrame(
                    response,
                    _limits));
        }

        private byte[] EnqueueAndWait(
            Ros2BridgeV2Request request,
            int timeoutMs)
        {
            using var pending = new PendingRequest(request);
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                if (_stopRequested
                    || _lifecycleState
                    == Ros2BridgeSessionLifecycleState.Faulted
                    || _lifecycleState
                    == Ros2BridgeSessionLifecycleState.Stopping
                    || _lifecycleState
                    == Ros2BridgeSessionLifecycleState.Stopped)
                {
                    throw new InvalidOperationException(
                        "The Bridge connection is not admitting work.");
                }
                if (_pending.Count >= _pendingCapacity)
                {
                    throw new U2R2ProtocolException(
                        "capacity_exceeded",
                        "The Bridge outstanding-request table is full.",
                        terminal: false);
                }
                if (_writerQueue.Count >= _writerCapacity)
                {
                    throw new U2R2ProtocolException(
                        "capacity_exceeded",
                        "The Bridge writer queue is full.",
                        terminal: false);
                }
                if (_pending.ContainsKey(
                        request.Expectation.RequestId))
                {
                    throw new U2R2ProtocolException(
                        "request_id_conflict",
                        "The Bridge request ID is already in flight.",
                        terminal: true);
                }

                _pending.Add(
                    request.Expectation.RequestId,
                    pending);
                _writerQueue.Enqueue(pending);
            }
            _writerSignal.Set();

            if (!pending.Wait(timeoutMs))
            {
                var timeout = new U2R2ProtocolException(
                    "timeout",
                    "The Bridge request exceeded its absolute deadline.",
                    terminal: true);
                Fault(timeout);
                throw timeout;
            }
            return pending.GetResponse();
        }

        private void WriterEntry()
        {
            Volatile.Write(
                ref _writerManagedThreadId,
                Thread.CurrentThread.ManagedThreadId);
            try
            {
                WriterLoop();
            }
            finally
            {
                OnWorkerExited();
            }
        }

        private void WriterLoop()
        {
            while (true)
            {
                PendingRequest request = null;
                lock (_gate)
                {
                    if (_stopRequested)
                        return;
                    if (_writerQueue.Count != 0)
                        request = _writerQueue.Dequeue();
                }
                if (request == null)
                {
                    _writerSignal.WaitOne(50);
                    continue;
                }

                try
                {
                    _transport.WriteV2(
                        request.Request.WireBytes,
                        _limits,
                        EffectiveTimeout(
                            _timeoutMs,
                            _limits.WriteTimeoutMs));
                }
                catch (Exception exception)
                {
                    Fault(NormalizeTransportFault(
                        exception,
                        "The Bridge writer failed."));
                    return;
                }
            }
        }

        private void ReaderEntry()
        {
            Volatile.Write(
                ref _readerManagedThreadId,
                Thread.CurrentThread.ManagedThreadId);
            try
            {
                ReaderLoop();
            }
            finally
            {
                OnWorkerExited();
            }
        }

        private void ReaderLoop()
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_stopRequested)
                        return;
                }

                byte[] wireBytes;
                try
                {
                    wireBytes = _transport.ReadV2(
                        _limits,
                        EffectiveTimeout(
                            _timeoutMs,
                            _limits.ReadTimeoutMs));
                }
                catch (Exception exception)
                {
                    lock (_gate)
                    {
                        if (_stopRequested)
                            return;
                    }
                    Fault(NormalizeTransportFault(
                        exception,
                        "The Bridge reader failed."));
                    return;
                }

                try
                {
                    var message = U2R2ProtocolCodec.ParseV2(
                        U2R2ProtocolCodec.DecodeFrame(
                            wireBytes,
                            _limits));
                    if (!message.IsResponse)
                    {
                        throw new U2R2ProtocolException(
                            message.Operation
                            == U2R2Operation.Message
                                ? "unknown_contract"
                                : "invalid_frame",
                            "The Bridge reader received an unowned non-response frame.",
                            terminal: true);
                    }

                    PendingRequest pending;
                    lock (_gate)
                    {
                        if (!_pending.TryGetValue(
                                message.RequestId,
                                out pending))
                        {
                            throw new U2R2ProtocolException(
                                "response_mismatch",
                                "The Bridge response has no matching outstanding request.",
                                terminal: true);
                        }
                    }
                    U2R2ProtocolCodec.ValidateResponseCorrelation(
                        pending.Request.Expectation,
                        message);
                    lock (_gate)
                    {
                        if (!_pending.TryGetValue(
                                message.RequestId,
                                out var current)
                            || !ReferenceEquals(current, pending))
                        {
                            throw new U2R2ProtocolException(
                                "response_mismatch",
                                "The Bridge response raced a completed request.",
                                terminal: true);
                        }
                        _pending.Remove(message.RequestId);
                    }
                    if (!pending.TryComplete(wireBytes))
                    {
                        throw new U2R2ProtocolException(
                            "response_mismatch",
                            "The Bridge response completed a request more than once.",
                            terminal: true);
                    }
                }
                catch (Exception exception)
                {
                    Fault(exception);
                    return;
                }
            }
        }

        private void Fault(Exception exception)
        {
            if (exception == null)
            {
                exception = new U2R2ProtocolException(
                    "invalid_frame",
                    "The Bridge connection faulted.",
                    terminal: true);
            }

            PendingRequest[] pending;
            lock (_gate)
            {
                if (_lifecycleState
                    == Ros2BridgeSessionLifecycleState.Stopped
                    || _lifecycleState
                    == Ros2BridgeSessionLifecycleState.Stopping
                    || _stopRequested)
                {
                    return;
                }
                _lastFault ??= exception;
                _lifecycleState =
                    Ros2BridgeSessionLifecycleState.Faulted;
                _stopRequested = true;
                _snapshot = null;
                pending = new PendingRequest[_pending.Count];
                _pending.Values.CopyTo(pending, 0);
                _pending.Clear();
                _writerQueue.Clear();
            }

            foreach (var request in pending)
                request.TryFail(_lastFault);
            _writerSignal.Set();
            try
            {
                _transport.Close();
            }
            catch (Exception closeException)
            {
                lock (_gate)
                    _lastFault ??= closeException;
            }
        }

        private void OnWorkerExited()
        {
            if (Interlocked.Decrement(ref _workersRemaining) != 0)
                return;

            lock (_gate)
            {
                if (_disposeRequested)
                {
                    _lifecycleState =
                        Ros2BridgeSessionLifecycleState.Stopped;
                }
            }
            TryDisposeResources();
        }

        public void Dispose()
        {
            PendingRequest[] pending;
            Thread reader;
            Thread writer;
            lock (_gate)
            {
                if (_disposeRequested)
                    return;
                _disposeRequested = true;
                if (_lifecycleState
                    != Ros2BridgeSessionLifecycleState.Stopped
                    && _lifecycleState
                    != Ros2BridgeSessionLifecycleState.Faulted)
                {
                    _lifecycleState =
                        Ros2BridgeSessionLifecycleState.Stopping;
                }
                _stopRequested = true;
                _snapshot = null;
                pending = new PendingRequest[_pending.Count];
                _pending.Values.CopyTo(pending, 0);
                _pending.Clear();
                _writerQueue.Clear();
                reader = _reader;
                writer = _writer;
            }

            var disposed = new ObjectDisposedException(
                nameof(Ros2BridgeConnection));
            foreach (var request in pending)
                request.TryFail(disposed);
            _writerSignal.Set();
            try
            {
                _transport.Close();
            }
            catch (Exception exception)
            {
                lock (_gate)
                    _lastFault ??= exception;
            }

            var joinTimeout = EffectiveTimeout(
                _timeoutMs,
                _limits.JoinTimeoutMs);
            var readerJoined = JoinUnlessCurrent(
                reader,
                joinTimeout);
            var writerJoined = JoinUnlessCurrent(
                writer,
                joinTimeout);
            if (!readerJoined || !writerJoined)
            {
                lock (_gate)
                {
                    _lastFault ??= new U2R2ProtocolException(
                        "timeout",
                        "The Bridge connection workers did not stop within the join deadline.",
                        terminal: true);
                    _lifecycleState =
                        Ros2BridgeSessionLifecycleState.Faulted;
                }
            }
            else
            {
                lock (_gate)
                {
                    _lifecycleState =
                        Ros2BridgeSessionLifecycleState.Stopped;
                }
            }
            TryDisposeResources();
        }

        private void TryDisposeResources()
        {
            if (!_disposeRequested
                || Volatile.Read(ref _workersRemaining) != 0
                || Interlocked.Exchange(
                    ref _resourcesDisposed,
                    1) != 0)
            {
                return;
            }

            Exception first = null;
            try
            {
                _transport.Dispose();
            }
            catch (Exception exception)
            {
                first = exception;
            }
            try
            {
                _writerSignal.Dispose();
            }
            catch (Exception exception)
            {
                first ??= exception;
            }
            if (first != null)
            {
                lock (_gate)
                    _lastFault ??= first;
            }
        }

        private void ThrowIfDisposedLocked()
        {
            if (_disposeRequested)
                throw new ObjectDisposedException(
                    nameof(Ros2BridgeConnection));
        }

        private static bool JoinUnlessCurrent(
            Thread thread,
            int timeoutMs)
            => thread == null
               || !thread.IsAlive
               || ReferenceEquals(
                   thread,
                   Thread.CurrentThread)
               || thread.Join(timeoutMs);

        private static int EffectiveTimeout(
            int configuredTimeoutMs,
            ulong protocolTimeoutMs)
            => checked((int)Math.Min(
                checked((ulong)configuredTimeoutMs),
                Math.Min(
                    protocolTimeoutMs,
                    checked((ulong)int.MaxValue))));

        private static Exception NormalizeTransportFault(
            Exception exception,
            string context)
        {
            if (exception is U2R2ProtocolException)
                return exception;
            if (exception is EndOfStreamException
                || exception is ObjectDisposedException
                || exception is IOException)
            {
                return new U2R2ProtocolException(
                    "peer_closed",
                    context + " " + exception.Message,
                    terminal: true,
                    innerException: exception);
            }
            return exception;
        }

        private sealed class PendingRequest : IDisposable
        {
            private readonly object _gate = new object();
            private readonly ManualResetEventSlim _completed =
                new ManualResetEventSlim(false);
            private byte[] _response;
            private Exception _error;
            private bool _settled;

            internal PendingRequest(Ros2BridgeV2Request request)
            {
                Request = request
                    ?? throw new ArgumentNullException(nameof(request));
            }

            internal Ros2BridgeV2Request Request { get; }

            internal bool Wait(int timeoutMs)
                => _completed.Wait(timeoutMs);

            internal bool TryComplete(byte[] response)
            {
                lock (_gate)
                {
                    if (_settled)
                        return false;
                    _response = response
                        ?? throw new ArgumentNullException(
                            nameof(response));
                    _settled = true;
                }
                _completed.Set();
                return true;
            }

            internal bool TryFail(Exception error)
            {
                lock (_gate)
                {
                    if (_settled)
                        return false;
                    _error = error
                        ?? throw new ArgumentNullException(
                            nameof(error));
                    _settled = true;
                }
                _completed.Set();
                return true;
            }

            internal byte[] GetResponse()
            {
                Exception error;
                byte[] response;
                lock (_gate)
                {
                    if (!_settled)
                    {
                        throw new InvalidOperationException(
                            "The Bridge request has not completed.");
                    }
                    error = _error;
                    response = _response;
                }
                if (error != null)
                    ExceptionDispatchInfo.Capture(error).Throw();
                return response;
            }

            public void Dispose() => _completed.Dispose();
        }
    }
}
