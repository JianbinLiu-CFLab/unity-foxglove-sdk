// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: One-reader/one-writer bounded U2R2 v2 connection owner.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Components = Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed partial class Ros2BridgeConnection :
        IDisposable,
        IRos2BridgeContractWireController
    {
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

        private void OnWorkerExited(int workerIndex)
        {
            if (Interlocked.Decrement(ref _workersRemaining) == 0)
            {
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

            // The final exclusive slot must remain occupied until the shared
            // transport and worker resources have actually been released.
            CompleteWorkerOwnershipOnExit(workerIndex);
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
            finally
            {
                Volatile.Write(
                    ref _disposeTeardownReady,
                    1);
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
            FinalizeWorkerRetirement(
                workerIndex: 0,
                readerJoined);
            FinalizeWorkerRetirement(
                workerIndex: 1,
                writerJoined);
            TryDisposeResources();
        }

        private void CompleteWorkerOwnershipOnExit(
            int workerIndex)
        {
            if (_retirement == null)
                return;

            var completeRetired = false;
            var returnActive = false;
            lock (_retirementGate)
            {
                _workerExited[workerIndex] = true;
                if (_workerRetired[workerIndex])
                {
                    completeRetired = true;
                }
                else if (_disposeRequested
                         && !_workerSlotReturned[workerIndex])
                {
                    _workerSlotReturned[workerIndex] = true;
                    returnActive = true;
                }
            }

            var reservationIndex =
                RetirementIndex(workerIndex);
            if (completeRetired)
            {
                _retirement.TryCompleteRetired(
                    reservationIndex);
            }
            else if (returnActive)
            {
                _retirement.TryReturn(
                    reservationIndex);
            }
        }

        private void FinalizeWorkerRetirement(
            int workerIndex,
            bool joined)
        {
            if (_retirement == null)
                return;

            WorkerRetirementLease lease = null;
            var returnActive = false;
            lock (_retirementGate)
            {
                if (_workerSlotReturned[workerIndex]
                    || _workerRetired[workerIndex])
                {
                    return;
                }
                if (joined || _workerExited[workerIndex])
                {
                    _workerSlotReturned[workerIndex] = true;
                    returnActive = true;
                }
                else
                {
                    lease = new WorkerRetirementLease(this);
                    var converted =
                        _retirement.TryConvertToRetired(
                            RetirementIndex(workerIndex),
                            lease,
                            _retirementIdentity
                            + (workerIndex == 0
                                ? "/reader"
                                : "/writer"),
                            retainedBytes: 0,
                            retainedResources: 3);
                    if (converted)
                    {
                        _workerRetired[workerIndex] = true;
                        lease = null;
                    }
                    else
                    {
                        _lastFault ??=
                            new InvalidOperationException(
                                "The Bridge worker retirement reservation could not be converted.");
                    }
                }
            }

            lease?.Dispose();
            if (returnActive)
            {
                _retirement.TryReturn(
                    RetirementIndex(workerIndex));
            }
        }

        private int RetirementIndex(int workerIndex)
            => workerIndex == 0
                ? _readerRetirementIndex
                : _writerRetirementIndex;

        private void TryDisposeResources()
        {
            if (!_disposeRequested
                || Volatile.Read(
                    ref _disposeTeardownReady) == 0
                || Volatile.Read(ref _workersRemaining) != 0
                || Interlocked.Exchange(
                    ref _resourcesDisposed,
                    1) != 0)
            {
                return;
            }

            Exception first = null;
            if (_disposeTransport)
            {
                try
                {
                    _transport.Dispose();
                }
                catch (Exception exception)
                {
                    first = exception;
                }
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
            => Math.Min(
                configuredTimeoutMs,
                ProtocolTimeout(protocolTimeoutMs));

        private static int ProtocolTimeout(
            ulong protocolTimeoutMs)
            => checked((int)Math.Min(
                protocolTimeoutMs,
                checked((ulong)int.MaxValue)));

        private static long HeartbeatIntervalTicks(
            ulong readTimeoutMs)
        {
            var intervalMilliseconds = Math.Max(
                1,
                ProtocolTimeout(readTimeoutMs) / 2);
            return Math.Max(
                1L,
                checked((long)Math.Ceiling(
                    intervalMilliseconds
                    * (double)Stopwatch.Frequency
                    / 1000d)));
        }

        private static int HeartbeatWaitMilliseconds(
            long lastWriteTimestamp,
            long heartbeatIntervalTicks)
        {
            var elapsedTicks =
                Stopwatch.GetTimestamp() - lastWriteTimestamp;
            if (elapsedTicks >= heartbeatIntervalTicks)
                return 0;
            var remainingMilliseconds = Math.Ceiling(
                (heartbeatIntervalTicks - elapsedTicks)
                * 1000d
                / Stopwatch.Frequency);
            return Math.Max(
                1,
                Math.Min(
                    50,
                    checked((int)remainingMilliseconds)));
        }

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

    }
}
