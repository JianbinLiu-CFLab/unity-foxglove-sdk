// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Capacity-one owned mailbox between callback and main-thread consumers.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal enum FoxRunRos2PendingDecision
    {
        Apply = 0,
        Drop = 1,
        Defer = 2
    }

    /// <summary>
    /// Owns at most one pending and one applied reference. Publishing replaces
    /// and disposes pending ownership on the producer thread; applying replaces
    /// and disposes applied ownership on the consumer thread. A Stop call made
    /// from the same operation or disposer stack only requests shutdown; final
    /// draining is performed by the main-thread consumer's apply-finally path or
    /// by a later external main-thread Stop. A non-reentrant external Stop drains
    /// synchronously before it returns.
    /// </summary>
    public sealed class FoxRunRos2OwnedLatestSlot<T>
        where T : class
    {
        private static readonly Func<Func<T>, T> s_invokeFactory = factory => factory();
        private const int StopStateRunning = 0;
        private const int StopStateInitializing = 1;
        private const int StopStateRequested = 2;
        private const int StopStateDraining = 3;
        private const int StopStateStopped = 4;

        [ThreadStatic]
        private static HashSet<FoxRunRos2OwnedLatestSlot<T>> s_currentOperations;

        private readonly Action<T> _dispose;
        private T _pending;
        private T _applied;
        private Func<T, bool> _stopClearIfOwned;
        private int _activePublishers;
        private int _activeAppliers;
        private int _drainOwnerThreadId;
        private int _stopState;
        private long _received;
        private long _replaced;
        private long _appliedCount;
        private long _rejectedAfterStop;
        private long _copyFailed;

        public FoxRunRos2OwnedLatestSlot(Action<T> dispose)
        {
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        }

        public long ReceivedCount => Interlocked.Read(ref _received);
        public long ReplacedCount => Interlocked.Read(ref _replaced);
        public long AppliedCount => Interlocked.Read(ref _appliedCount);
        public long RejectedAfterStopCount => Interlocked.Read(ref _rejectedAfterStop);
        public long CopyFailedCount => Interlocked.Read(ref _copyFailed);
        public int PendingCount => Volatile.Read(ref _pending) == null ? 0 : 1;
        public bool IsStopping => Volatile.Read(ref _stopState) != StopStateRunning;
        internal bool IsStopped => Volatile.Read(ref _stopState) == StopStateStopped;

        public bool TryPublish(Func<T> copyOwned)
            => TryPublish(copyOwned, s_invokeFactory, out _);

        public bool TryPublish(Func<T> copyOwned, out Exception copyFailure)
            => TryPublish(copyOwned, s_invokeFactory, out copyFailure);

        public bool TryPublish<TState>(TState state, Func<TState, T> copyOwned)
            => TryPublish(state, copyOwned, out _);

        public bool TryPublish<TState>(
            TState state,
            Func<TState, T> copyOwned,
            out Exception copyFailure)
            => TryPublish(state, copyOwned, out copyFailure, out _);

        public bool TryPublish<TState>(
            TState state,
            Func<TState, T> copyOwned,
            out Exception copyFailure,
            out bool replacedPending)
        {
            if (copyOwned == null)
                throw new ArgumentNullException(nameof(copyOwned));

            copyFailure = null;
            replacedPending = false;
            Interlocked.Increment(ref _received);
            if (Volatile.Read(ref _stopState) != StopStateRunning)
            {
                Interlocked.Increment(ref _rejectedAfterStop);
                return false;
            }

            Interlocked.Increment(ref _activePublishers);
            if (Volatile.Read(ref _stopState) != StopStateRunning)
            {
                Interlocked.Decrement(ref _activePublishers);
                Interlocked.Increment(ref _rejectedAfterStop);
                return false;
            }

            var operationMarker = EnterCurrentOperation();
            try
            {
                T owned;
                try
                {
                    owned = copyOwned(state);
                    if (owned == null)
                        throw new InvalidOperationException("FoxRun ROS2 owned copy must not be null.");
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref _copyFailed);
                    copyFailure = exception;
                    if (!FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                        throw;
                    return false;
                }

                if (Volatile.Read(ref _stopState) != StopStateRunning)
                {
                    Interlocked.Increment(ref _rejectedAfterStop);
                    _dispose(owned);
                    return false;
                }

                var replaced = Interlocked.Exchange(ref _pending, owned);
                if (replaced != null && !ReferenceEquals(replaced, owned))
                {
                    replacedPending = true;
                    Interlocked.Increment(ref _replaced);
                    _dispose(replaced);
                }
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _activePublishers);
                ExitCurrentOperation(operationMarker);
            }
        }

        public bool TryApplyLatest(Action<T> apply, Func<T, bool> clearIfOwned)
        {
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));
            if (clearIfOwned == null)
                throw new ArgumentNullException(nameof(clearIfOwned));

            if (Volatile.Read(ref _stopState) != StopStateRunning)
                return false;

            Interlocked.Increment(ref _activeAppliers);
            if (Volatile.Read(ref _stopState) != StopStateRunning)
            {
                Interlocked.Decrement(ref _activeAppliers);
                return false;
            }

            var operationMarker = EnterCurrentOperation();
            ExceptionDispatchInfo primaryFailure = null;
            var applied = false;
            try
            {
                var candidate = Interlocked.Exchange(ref _pending, null);
                if (candidate != null)
                {
                    try
                    {
                        apply(candidate);
                    }
                    catch (Exception exception)
                    {
                        TryClear(clearIfOwned, candidate);
                        TryDispose(candidate);
                        primaryFailure = ExceptionDispatchInfo.Capture(exception);
                    }

                    if (primaryFailure == null)
                    {
                        try
                        {
                            var previous = Interlocked.Exchange(ref _applied, candidate);
                            Interlocked.Increment(ref _appliedCount);
                            if (previous != null && !ReferenceEquals(previous, candidate))
                                _dispose(previous);
                            applied = true;
                        }
                        catch (Exception exception)
                        {
                            primaryFailure = ExceptionDispatchInfo.Capture(exception);
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeAppliers);
                ExitCurrentOperation(operationMarker);
                try
                {
                    TryCompleteDeferredStop();
                }
                catch (Exception exception)
                {
                    if (primaryFailure == null)
                        primaryFailure = ExceptionDispatchInfo.Capture(exception);
                }
            }

            if (primaryFailure != null)
                primaryFailure.Throw();
            return applied;
        }

        /// <summary>
        /// Applies a candidate only when the consumer accepts ownership. A
        /// <c>false</c> return disposes the owned candidate without placing it
        /// in the applied slot. Custom P&amp;S uses this to drop a local-origin
        /// envelope after its callback-thread copy but before DTO conversion.
        /// </summary>
        public bool TryApplyLatest(Func<T, bool> applyAndRetain, Func<T, bool> clearIfOwned)
        {
            if (applyAndRetain == null)
                throw new ArgumentNullException(nameof(applyAndRetain));
            if (clearIfOwned == null)
                throw new ArgumentNullException(nameof(clearIfOwned));

            if (Volatile.Read(ref _stopState) != StopStateRunning)
                return false;

            Interlocked.Increment(ref _activeAppliers);
            if (Volatile.Read(ref _stopState) != StopStateRunning)
            {
                Interlocked.Decrement(ref _activeAppliers);
                return false;
            }

            var operationMarker = EnterCurrentOperation();
            ExceptionDispatchInfo primaryFailure = null;
            var applied = false;
            try
            {
                var candidate = Interlocked.Exchange(ref _pending, null);
                if (candidate != null)
                {
                    var retain = false;
                    try
                    {
                        retain = applyAndRetain(candidate);
                    }
                    catch (Exception exception)
                    {
                        TryClear(clearIfOwned, candidate);
                        TryDispose(candidate);
                        primaryFailure = ExceptionDispatchInfo.Capture(exception);
                    }

                    if (primaryFailure == null && !retain)
                    {
                        try
                        {
                            _dispose(candidate);
                        }
                        catch (Exception exception)
                        {
                            primaryFailure = ExceptionDispatchInfo.Capture(exception);
                        }
                    }

                    if (primaryFailure == null && retain)
                    {
                        try
                        {
                            var previous = Interlocked.Exchange(ref _applied, candidate);
                            Interlocked.Increment(ref _appliedCount);
                            if (previous != null && !ReferenceEquals(previous, candidate))
                                _dispose(previous);
                            applied = true;
                        }
                        catch (Exception exception)
                        {
                            primaryFailure = ExceptionDispatchInfo.Capture(exception);
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeAppliers);
                ExitCurrentOperation(operationMarker);
                try
                {
                    TryCompleteDeferredStop();
                }
                catch (Exception exception)
                {
                    if (primaryFailure == null)
                        primaryFailure = ExceptionDispatchInfo.Capture(exception);
                }
            }

            if (primaryFailure != null)
                primaryFailure.Throw();
            return applied;
        }

        /// <summary>
        /// Applies, drops, or defers the newest owned candidate after comparing
        /// it with the currently applied owned value. A deferred candidate is
        /// restored only when no newer callback value arrived while the main
        /// thread was deciding; otherwise the newer pending value wins.
        /// </summary>
        internal bool TryApplyLatest(
            Func<T, T, FoxRunRos2PendingDecision> decide,
            Action<T> apply,
            Func<T, bool> clearIfOwned)
        {
            if (decide == null)
                throw new ArgumentNullException(nameof(decide));
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));
            if (clearIfOwned == null)
                throw new ArgumentNullException(nameof(clearIfOwned));

            if (Volatile.Read(ref _stopState) != StopStateRunning)
                return false;

            Interlocked.Increment(ref _activeAppliers);
            if (Volatile.Read(ref _stopState) != StopStateRunning)
            {
                Interlocked.Decrement(ref _activeAppliers);
                return false;
            }

            var operationMarker = EnterCurrentOperation();
            ExceptionDispatchInfo primaryFailure = null;
            var applied = false;
            try
            {
                var candidate = Interlocked.Exchange(ref _pending, null);
                if (candidate != null)
                {
                    FoxRunRos2PendingDecision decision;
                    try
                    {
                        decision = decide(candidate, Volatile.Read(ref _applied));
                    }
                    catch (Exception exception)
                    {
                        TryDispose(candidate);
                        primaryFailure = ExceptionDispatchInfo.Capture(exception);
                        decision = FoxRunRos2PendingDecision.Drop;
                    }

                    if (primaryFailure == null && decision == FoxRunRos2PendingDecision.Defer)
                    {
                        var newer = Interlocked.CompareExchange(ref _pending, candidate, null);
                        if (newer != null && !ReferenceEquals(newer, candidate))
                            TryDispose(candidate);
                    }
                    else if (primaryFailure == null && decision == FoxRunRos2PendingDecision.Drop)
                    {
                        TryDispose(candidate);
                    }
                    else if (primaryFailure == null && decision == FoxRunRos2PendingDecision.Apply)
                    {
                        try
                        {
                            apply(candidate);
                        }
                        catch (Exception exception)
                        {
                            TryClear(clearIfOwned, candidate);
                            TryDispose(candidate);
                            primaryFailure = ExceptionDispatchInfo.Capture(exception);
                        }

                        if (primaryFailure == null)
                        {
                            try
                            {
                                var previous = Interlocked.Exchange(ref _applied, candidate);
                                Interlocked.Increment(ref _appliedCount);
                                if (previous != null && !ReferenceEquals(previous, candidate))
                                    _dispose(previous);
                                applied = true;
                            }
                            catch (Exception exception)
                            {
                                primaryFailure = ExceptionDispatchInfo.Capture(exception);
                            }
                        }
                    }
                    else if (primaryFailure == null)
                    {
                        TryDispose(candidate);
                        primaryFailure = ExceptionDispatchInfo.Capture(
                            new InvalidOperationException("Unknown FoxRun ROS2 pending decision."));
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeAppliers);
                ExitCurrentOperation(operationMarker);
                try
                {
                    TryCompleteDeferredStop();
                }
                catch (Exception exception)
                {
                    if (primaryFailure == null)
                        primaryFailure = ExceptionDispatchInfo.Capture(exception);
                }
            }

            if (primaryFailure != null)
                primaryFailure.Throw();
            return applied;
        }

        public void Stop(Func<T, bool> clearIfOwned)
        {
            var reentrant = IsCurrentOperation()
                            || Volatile.Read(ref _drainOwnerThreadId) == Environment.CurrentManagedThreadId;
            BeginStop(clearIfOwned);
            if (reentrant)
                return;

            CompleteStopSynchronously();
        }

        /// <summary>
        /// Closes producer/consumer admission without draining ownership. This
        /// permits a transport owner to detach callbacks before synchronous
        /// cleanup while guaranteeing that late callbacks cannot enqueue.
        /// </summary>
        public void BeginStop(Func<T, bool> clearIfOwned)
        {
            if (clearIfOwned == null)
                throw new ArgumentNullException(nameof(clearIfOwned));
            RequestStopCore(clearIfOwned);
        }

        private void RequestStopCore(Func<T, bool> clearIfOwned)
        {
            var spinner = new SpinWait();
            while (true)
            {
                var state = Volatile.Read(ref _stopState);
                if (state == StopStateRunning
                    && Interlocked.CompareExchange(
                        ref _stopState,
                        StopStateInitializing,
                        StopStateRunning) == StopStateRunning)
                {
                    Volatile.Write(ref _stopClearIfOwned, clearIfOwned);
                    Volatile.Write(ref _stopState, StopStateRequested);
                    return;
                }

                if (state == StopStateInitializing)
                {
                    spinner.SpinOnce();
                    continue;
                }

                if (state != StopStateRunning)
                    return;
            }
        }

        private void CompleteStopSynchronously()
        {
            var spinner = new SpinWait();
            while (true)
            {
                var state = Volatile.Read(ref _stopState);
                if (state == StopStateInitializing)
                {
                    spinner.SpinOnce();
                    continue;
                }

                if (state == StopStateRequested
                    && Interlocked.CompareExchange(
                        ref _stopState,
                        StopStateDraining,
                        StopStateRequested) == StopStateRequested)
                {
                    DrainAsOwner();
                    return;
                }

                if (state == StopStateStopped)
                    return;

                if (state == StopStateDraining)
                {
                    if (Volatile.Read(ref _drainOwnerThreadId) == Environment.CurrentManagedThreadId)
                        return;
                    spinner.SpinOnce();
                }
            }
        }

        private void TryCompleteDeferredStop()
        {
            if (Volatile.Read(ref _activePublishers) != 0
                || Volatile.Read(ref _activeAppliers) != 0)
                return;

            if (Interlocked.CompareExchange(
                    ref _stopState,
                    StopStateDraining,
                    StopStateRequested) == StopStateRequested)
                DrainAsOwner();
        }

        private void DrainAsOwner()
        {
            Volatile.Write(ref _drainOwnerThreadId, Environment.CurrentManagedThreadId);

            Exception firstFailure = null;
            var spinner = new SpinWait();
            while (Volatile.Read(ref _activePublishers) != 0
                   || Volatile.Read(ref _activeAppliers) != 0)
                spinner.SpinOnce();

            try
            {
                var pending = Interlocked.Exchange(ref _pending, null);
                var applied = Interlocked.Exchange(ref _applied, null);
                var clearIfOwned = Volatile.Read(ref _stopClearIfOwned);
                if (applied != null)
                    CaptureCleanupFailure(ref firstFailure, () => clearIfOwned(applied));
                if (pending != null)
                    CaptureCleanupFailure(ref firstFailure, () => _dispose(pending));
                if (applied != null && !ReferenceEquals(applied, pending))
                    CaptureCleanupFailure(ref firstFailure, () => _dispose(applied));
            }
            finally
            {
                Volatile.Write(ref _stopClearIfOwned, null);
                Volatile.Write(ref _drainOwnerThreadId, 0);
                Volatile.Write(ref _stopState, StopStateStopped);
            }

            if (firstFailure != null)
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }

        private static void CaptureCleanupFailure(ref Exception firstFailure, Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                if (firstFailure == null)
                    firstFailure = exception;
            }
        }

        private bool EnterCurrentOperation()
        {
            var operations = s_currentOperations;
            if (operations == null)
            {
                operations = new HashSet<FoxRunRos2OwnedLatestSlot<T>>();
                s_currentOperations = operations;
            }
            return operations.Add(this);
        }

        private void ExitCurrentOperation(bool operationMarker)
        {
            if (operationMarker)
                s_currentOperations.Remove(this);
        }

        private bool IsCurrentOperation()
        {
            var operations = s_currentOperations;
            return operations != null && operations.Contains(this);
        }

        private void TryDispose(T owned)
        {
            try
            {
                _dispose(owned);
            }
            catch
            {
                // Preserve the apply exception that initiated candidate cleanup.
            }
        }

        private static void TryClear(Func<T, bool> clearIfOwned, T owned)
        {
            try
            {
                clearIfOwned(owned);
            }
            catch
            {
                // Clearing is best effort; ownership still has to terminate.
            }
        }
    }
}
#endif
