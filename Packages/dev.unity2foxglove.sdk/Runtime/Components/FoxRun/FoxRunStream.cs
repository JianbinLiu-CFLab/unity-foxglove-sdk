// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Bounded thread-safe owned input queue for high-rate FoxRun consumers.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Bounded, non-blocking producer queue with main-thread drain and explicit
    /// exactly-once ownership transfer.
    /// </summary>
    public sealed class FoxRunStream<T> : IDisposable
    {
        private const int MaximumDisposalDiagnosticCharacters = 512;
        private readonly object _gate = new object();
        private readonly Queue<OwnedSample> _queue;
        private readonly Func<long> _getTimestamp;
        private readonly long _minimumAdmissionTicks;
        private long _lastAdmissionTimestamp;
        private bool _hasAdmissionTimestamp;
        private bool _disposed;

        private long _received;
        private long _admitted;
        private long _drained;
        private long _taken;
        private long _droppedOldest;
        private long _droppedNewest;
        private long _rateDropped;
        private long _cleared;
        private long _highWater;
        private long _disposalFailures;
        private string _lastDisposalError = string.Empty;

        public FoxRunStream()
            : this(new FoxRunStreamOptions())
        {
        }

        public FoxRunStream(FoxRunStreamOptions options)
            : this(options, Stopwatch.GetTimestamp, Stopwatch.Frequency)
        {
        }

        internal FoxRunStream(
            FoxRunStreamOptions options,
            Func<long> getTimestamp,
            long timestampFrequency)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _getTimestamp = getTimestamp ?? throw new ArgumentNullException(nameof(getTimestamp));
            if (timestampFrequency <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(timestampFrequency),
                    "Timestamp frequency must be positive.");
            _queue = new Queue<OwnedSample>(Math.Min(options.Capacity, 4096));
            var minimumAdmissionTicks = Math.Ceiling(timestampFrequency / options.MaxInputHz);
            _minimumAdmissionTicks = minimumAdmissionTicks >= long.MaxValue
                ? long.MaxValue
                : Math.Max(1L, (long)minimumAdmissionTicks);
        }

        public FoxRunStreamOptions Options { get; }

        public int Count
        {
            get
            {
                lock (_gate)
                    return _queue.Count;
            }
        }

        public bool IsDisposed
        {
            get
            {
                lock (_gate)
                    return _disposed;
            }
        }

        public FoxRunStreamStats Stats
            => new FoxRunStreamStats(
                Volatile.Read(ref _received),
                Volatile.Read(ref _admitted),
                Volatile.Read(ref _drained),
                Volatile.Read(ref _taken),
                Volatile.Read(ref _droppedOldest),
                Volatile.Read(ref _droppedNewest),
                Volatile.Read(ref _rateDropped),
                Volatile.Read(ref _cleared),
                Volatile.Read(ref _highWater),
                Volatile.Read(ref _disposalFailures),
                Volatile.Read(ref _lastDisposalError));

        /// <summary>
        /// Applies the stream's finite monotonic admission ceiling before the
        /// provider performs avoidable decode, allocation, or deep-copy work.
        /// </summary>
        public bool TryAdmitInput()
        {
            SaturatingIncrement(ref _received);
            var now = _getTimestamp();
            lock (_gate)
            {
                if (_disposed)
                {
                    SaturatingIncrement(ref _rateDropped);
                    return false;
                }

                if (_hasAdmissionTimestamp
                    && now - _lastAdmissionTimestamp < _minimumAdmissionTicks)
                {
                    SaturatingIncrement(ref _rateDropped);
                    return false;
                }

                _lastAdmissionTimestamp = now;
                _hasAdmissionTimestamp = true;
                SaturatingIncrement(ref _admitted);
                return true;
            }
        }

        /// <summary>
        /// Unconditionally takes ownership at the call boundary. A false result
        /// means the value was rejected and already disposed by this stream.
        /// </summary>
        public bool TryEnqueueOwned(T value, Action<T> disposer)
        {
            if (disposer == null)
                throw new ArgumentNullException(nameof(disposer));

            var owned = new DirectOwnedSample(value, disposer);
            return TryEnqueueOwnedCore(owned);
        }

        /// <summary>
        /// Takes ownership of a provider-safe state object without invoking the
        /// materializer. Materialization is deferred until a consumer drains or
        /// takes the sample, so native producer callbacks never have to construct
        /// or mutate the user-facing <typeparamref name="T"/> value.
        /// </summary>
        public bool TryEnqueueDeferredOwned<TState>(
            TState state,
            Func<TState, T> materializer,
            Action<TState> stateDisposer,
            Action<T> disposer)
        {
            if (materializer == null)
                throw new ArgumentNullException(nameof(materializer));
            if (stateDisposer == null)
                throw new ArgumentNullException(nameof(stateDisposer));
            if (disposer == null)
                throw new ArgumentNullException(nameof(disposer));

            return TryEnqueueOwnedCore(
                new DeferredOwnedSample<TState>(state, materializer, stateDisposer, disposer));
        }

        private bool TryEnqueueOwnedCore(OwnedSample owned)
        {
            OwnedSample displaced = null;
            var accepted = false;
            lock (_gate)
            {
                if (_disposed)
                {
                    displaced = owned;
                }
                else if (_queue.Count >= Options.Capacity
                         && Options.Overflow == FoxRunStreamOverflowPolicy.DropNewest)
                {
                    SaturatingIncrement(ref _droppedNewest);
                    displaced = owned;
                }
                else
                {
                    if (_queue.Count >= Options.Capacity)
                    {
                        displaced = _queue.Dequeue();
                        SaturatingIncrement(ref _droppedOldest);
                    }
                    _queue.Enqueue(owned);
                    accepted = true;
                    UpdateHighWater(_queue.Count);
                }
            }

            DisposeOwned(displaced);
            return accepted;
        }

        /// <summary>
        /// Invokes at most <see cref="FoxRunStreamOptions.MaxBatch"/> callbacks.
        /// The stream retains ownership and disposes each current sample in a
        /// finally block. Consumer exceptions propagate and stop this drain.
        /// </summary>
        public int Drain(Action<T> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            var drained = 0;
            while (drained < Options.MaxBatch)
            {
                OwnedSample owned;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                        break;
                    owned = _queue.Dequeue();
                }

                var materialized = MaterializeOwned(owned);
                var callbackCompleted = false;
                try
                {
                    callback(materialized.Value);
                    callbackCompleted = true;
                }
                finally
                {
                    DisposeMaterialized(materialized);
                    if (callbackCompleted)
                    {
                        drained++;
                        SaturatingIncrement(ref _drained);
                    }
                }
            }
            return drained;
        }

        public bool TryTake(out FoxRunStreamSample<T> sample)
        {
            OwnedSample owned;
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    sample = null;
                    return false;
                }
                owned = _queue.Dequeue();
            }

            sample = CreateLease(MaterializeOwned(owned));
            SaturatingIncrement(ref _taken);
            return true;
        }

        public bool TryTakeLatest(out FoxRunStreamSample<T> sample)
        {
            OwnedSample latest;
            List<OwnedSample> older = null;
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    sample = null;
                    return false;
                }

                while (_queue.Count > 1)
                {
                    older ??= new List<OwnedSample>();
                    older.Add(_queue.Dequeue());
                }
                latest = _queue.Dequeue();
                SaturatingAdd(ref _cleared, older?.Count ?? 0);
            }

            DisposeAll(older);
            sample = CreateLease(MaterializeOwned(latest));
            SaturatingIncrement(ref _taken);
            return true;
        }

        public int Clear()
        {
            List<OwnedSample> owned;
            lock (_gate)
            {
                if (_queue.Count == 0)
                    return 0;
                owned = new List<OwnedSample>(_queue.Count);
                while (_queue.Count != 0)
                    owned.Add(_queue.Dequeue());
                SaturatingAdd(ref _cleared, owned.Count);
            }

            DisposeAll(owned);
            return owned.Count;
        }

        public void Dispose()
        {
            List<OwnedSample> owned = null;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                if (_queue.Count != 0)
                {
                    owned = new List<OwnedSample>(_queue.Count);
                    while (_queue.Count != 0)
                        owned.Add(_queue.Dequeue());
                    SaturatingAdd(ref _cleared, owned.Count);
                }
            }
            DisposeAll(owned);
        }

        private FoxRunStreamSample<T> CreateLease(MaterializedOwnedSample owned)
            => new FoxRunStreamSample<T>(
                owned.Value,
                owned.Disposer,
                RecordDisposalFailure);

        private MaterializedOwnedSample MaterializeOwned(OwnedSample owned)
            => owned.Materialize(RecordDisposalFailure);

        private void DisposeAll(IReadOnlyList<OwnedSample> owned)
        {
            if (owned == null)
                return;
            for (var index = 0; index < owned.Count; index++)
                DisposeOwned(owned[index]);
        }

        private void DisposeOwned(OwnedSample owned)
        {
            if (owned == null)
                return;
            try
            {
                owned.DisposeUnmaterialized();
            }
            catch (Exception exception)
            {
                RecordDisposalFailure(exception);
            }
        }

        private void DisposeMaterialized(MaterializedOwnedSample owned)
        {
            if (owned == null)
                return;
            try
            {
                owned.Disposer(owned.Value);
            }
            catch (Exception exception)
            {
                RecordDisposalFailure(exception);
            }
        }

        private void RecordDisposalFailure(Exception exception)
        {
            SaturatingIncrement(ref _disposalFailures);
            var diagnostic = exception == null
                ? "Unknown disposer failure."
                : exception.GetType().Name + ": " + exception.Message;
            if (diagnostic.Length > MaximumDisposalDiagnosticCharacters)
                diagnostic = diagnostic.Substring(0, MaximumDisposalDiagnosticCharacters);
            Volatile.Write(ref _lastDisposalError, diagnostic);
        }

        private void UpdateHighWater(int count)
        {
            var current = Volatile.Read(ref _highWater);
            if (current == long.MaxValue || count <= current)
                return;
            Volatile.Write(ref _highWater, count);
        }

        private static void SaturatingIncrement(ref long counter)
            => SaturatingAdd(ref counter, 1);

        private static void SaturatingAdd(ref long counter, int amount)
        {
            if (amount <= 0)
                return;
            while (true)
            {
                var current = Volatile.Read(ref counter);
                if (current == long.MaxValue)
                    return;
                var remaining = long.MaxValue - current;
                var next = remaining <= amount ? long.MaxValue : current + amount;
                if (Interlocked.CompareExchange(ref counter, next, current) == current)
                    return;
            }
        }

        private abstract class OwnedSample
        {
            internal abstract MaterializedOwnedSample Materialize(
                Action<Exception> reportDisposalFailure);

            internal abstract void DisposeUnmaterialized();
        }

        private sealed class DirectOwnedSample : OwnedSample
        {
            private readonly T _value;
            private readonly Action<T> _disposer;

            internal DirectOwnedSample(T value, Action<T> disposer)
            {
                _value = value;
                _disposer = disposer;
            }

            internal override MaterializedOwnedSample Materialize(
                Action<Exception> reportDisposalFailure)
                => new MaterializedOwnedSample(_value, _disposer);

            internal override void DisposeUnmaterialized()
                => _disposer(_value);
        }

        private sealed class DeferredOwnedSample<TState> : OwnedSample
        {
            private readonly TState _state;
            private readonly Func<TState, T> _materializer;
            private readonly Action<TState> _stateDisposer;
            private readonly Action<T> _disposer;

            internal DeferredOwnedSample(
                TState state,
                Func<TState, T> materializer,
                Action<TState> stateDisposer,
                Action<T> disposer)
            {
                _state = state;
                _materializer = materializer;
                _stateDisposer = stateDisposer;
                _disposer = disposer;
            }

            internal override MaterializedOwnedSample Materialize(
                Action<Exception> reportDisposalFailure)
            {
                T value;
                try
                {
                    value = _materializer(_state);
                }
                catch
                {
                    DisposeState(reportDisposalFailure);
                    throw;
                }

                DisposeState(reportDisposalFailure);
                return new MaterializedOwnedSample(value, _disposer);
            }

            internal override void DisposeUnmaterialized()
                => _stateDisposer(_state);

            private void DisposeState(Action<Exception> reportDisposalFailure)
            {
                try
                {
                    _stateDisposer(_state);
                }
                catch (Exception exception)
                {
                    reportDisposalFailure(exception);
                }
            }
        }

        private sealed class MaterializedOwnedSample
        {
            internal MaterializedOwnedSample(T value, Action<T> disposer)
            {
                Value = value;
                Disposer = disposer;
            }

            internal T Value { get; }
            internal Action<T> Disposer { get; }
        }
    }
}
