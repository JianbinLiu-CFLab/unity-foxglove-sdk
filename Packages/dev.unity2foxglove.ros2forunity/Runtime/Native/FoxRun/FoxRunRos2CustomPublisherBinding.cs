// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Main-thread typed-bus binding for one custom ROS2 publisher endpoint.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Connects one generated DTO type to one generated ROS2 envelope type.
    /// The typed local bus invokes this binding only on the Unity main thread;
    /// it never participates in ROS executor callbacks or retains user DTO
    /// arrays/lists after the mapper returns.
    /// </summary>
    internal sealed class FoxRunRos2CustomPublisherBinding<TDto, TEnvelope>
        where TEnvelope : ROS2.Message, new()
    {
        private readonly FoxRunRos2CustomPublisherContract _contract;
        private readonly FoxTopicBus _bus;
        private readonly IFoxRunRos2NativePublisherBackend _backend;
        private readonly FoxRunResolvedQos _qos;
        private readonly Func<TDto, string, ulong, ulong, FoxRunRos2CustomOutboundMappingContext, TEnvelope> _map;
        private readonly Action<TEnvelope> _dispose;
        private readonly string _origin;
        private readonly FoxRunRos2CustomSequenceSource _sequence;
        private readonly Func<FoxRunRos2CustomTypesupportReadiness> _readiness;
        private readonly Action _onStopped;
        private readonly Func<FoxTopicEnvelope<TDto>, bool> _busCallback;
        private const int MaximumCleanupRetries = 8;
        private static readonly long MaximumCleanupRetryDurationTicks = Stopwatch.Frequency * 4L;
        private readonly object _cleanupGate = new object();
        private IFoxRunRos2NativePublisherToken _token;
        private bool _subscribed;
        private int _stopped;
        private int _cleanupPending;
        private int _cleanupRetryCount;
        private int _cleanupRetryExhausted;
        private long _cleanupRetryDeadlineTimestamp;
        private int _ownershipReleased;
        private int _completionNotified;

        internal FoxRunRos2CustomPublisherBinding(
            FoxRunRos2CustomPublisherContract contract,
            FoxTopicBus bus,
            IFoxRunRos2NativePublisherBackend backend,
            FoxRunResolvedQos qos,
            Func<TDto, string, ulong, ulong, FoxRunRos2CustomOutboundMappingContext, TEnvelope> map,
            Action<TEnvelope> dispose,
            string origin,
            FoxRunRos2CustomSequenceSource sequence,
            Func<FoxRunRos2CustomTypesupportReadiness> readiness,
            Action onStopped = null)
        {
            _contract = contract ?? throw new ArgumentNullException(nameof(contract));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _qos = qos;
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
            _origin = origin ?? string.Empty;
            _sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _onStopped = onStopped;
            _busCallback = OnBusEnvelope;
        }

        internal bool IsStopped => Volatile.Read(ref _stopped) != 0;
        internal bool CleanupPending => Volatile.Read(ref _cleanupPending) != 0;
        internal bool CleanupRetryExhausted => Volatile.Read(ref _cleanupRetryExhausted) != 0;
        internal int PublishedCount { get; private set; }
        internal int MapperFailureCount { get; private set; }
        internal int PublishFailureCount { get; private set; }
        internal int DisposeFailureCount { get; private set; }
        internal int BudgetRejectedCount { get; private set; }
        internal int SequenceExhaustedCount { get; private set; }

        internal FoxRunRos2RegistrationResult TryStart()
        {
            if (IsStopped)
            {
                return FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.Stopped,
                    "The custom ROS2 publisher binding is stopped.");
            }
            if (_subscribed)
                return FoxRunRos2RegistrationResult.Success();

            var readiness = _readiness();
            if (!readiness.IsReady)
            {
                return FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.RegistrationRejected,
                    "The selected custom ROS2 typesupport add-on is not ready.");
            }

            var registration = _backend.Register<TEnvelope>(_contract, _qos);
            // Own every returned token before touching any of its members.
            // Native wrapper getters are external code and may throw fatally;
            // Stop must still be able to remove the already-created endpoint.
            _token = registration.Token;
            if (!registration.Succeeded || _token == null)
            {
                Stop();
                return FoxRunRos2RegistrationResult.Failure(
                    registration.Succeeded
                        ? FoxRunRos2RegistrationError.InvalidPublisherToken
                        : registration.Error,
                    registration.FailureKind);
            }

            bool tokenUsable;
            try
            {
                tokenUsable = _token.IsUsable;
            }
            catch (Exception exception)
            {
                var primary = ExceptionDispatchInfo.Capture(exception);
                try
                {
                    Stop();
                }
                catch (Exception)
                {
                    // Stop completes all mandatory teardown stages before
                    // throwing. Preserve the token getter as the primary fault.
                }
                primary.Throw();
                throw;
            }
            if (!tokenUsable)
            {
                Stop();
                return FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.InvalidPublisherToken,
                    registration.FailureKind);
            }

            try
            {
                _bus.SubscribeResult(_contract.Topic, _origin, _busCallback);
                _subscribed = true;
                return FoxRunRos2RegistrationResult.Success();
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                Stop();
                return FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.PublisherBackendFailure,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal void Stop()
        {
            ExceptionDispatchInfo fatal = null;
            if (Interlocked.Exchange(ref _stopped, 1) == 0 && _subscribed)
            {
                try
                {
                    _bus.UnsubscribeResult(
                        _contract.Topic,
                        _origin,
                        _busCallback);
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    // Best-effort after bus shutdown.
                }
                catch (Exception exception)
                {
                    fatal = ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    _subscribed = false;
                }
            }

            try
            {
                TryRetryCleanup();
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
            }

            fatal?.Throw();
        }

        internal bool TryRetryCleanup()
            => TryRetryCleanup(force: false);

        internal bool TryForceRetryCleanup()
            => TryRetryCleanup(force: true);

        private bool TryRetryCleanup(bool force)
        {
            lock (_cleanupGate)
            {
                if (force && Volatile.Read(ref _cleanupRetryExhausted) != 0)
                {
                    Volatile.Write(ref _cleanupRetryExhausted, 0);
                    Volatile.Write(ref _cleanupRetryCount, 0);
                    Volatile.Write(ref _cleanupRetryDeadlineTimestamp, 0L);
                    Volatile.Write(ref _cleanupPending, 1);
                }
                return TryRetryCleanupCore();
            }
        }

        private bool TryRetryCleanupCore()
        {
            if (!IsStopped)
                return false;
            if (Volatile.Read(ref _cleanupRetryExhausted) != 0)
                return false;

            ExceptionDispatchInfo fatal = null;
            var token = Volatile.Read(ref _token);
            if (token != null)
            {
                try
                {
                    if (!TryRemovePublisher(token))
                    {
                        var now = Stopwatch.GetTimestamp();
                        if (Volatile.Read(ref _cleanupRetryDeadlineTimestamp) == 0)
                        {
                            Volatile.Write(
                                ref _cleanupRetryDeadlineTimestamp,
                                now + MaximumCleanupRetryDurationTicks);
                        }
                        var retryCount = Interlocked.Increment(ref _cleanupRetryCount);
                        Volatile.Write(ref _cleanupPending, 1);
                        if (retryCount >= MaximumCleanupRetries
                            || now >= Volatile.Read(ref _cleanupRetryDeadlineTimestamp))
                        {
                            Volatile.Write(ref _cleanupRetryExhausted, 1);
                        }
                    }
                    else
                    {
                        Interlocked.Exchange(ref _cleanupRetryCount, 0);
                        Volatile.Write(ref _cleanupRetryDeadlineTimestamp, 0L);
                        Volatile.Write(ref _cleanupRetryExhausted, 0);
                        Interlocked.CompareExchange(ref _token, null, token);
                        Volatile.Write(ref _cleanupPending, 0);
                    }
                }
                catch (Exception exception)
                {
                    fatal = ExceptionDispatchInfo.Capture(exception);
                    Volatile.Write(ref _cleanupRetryExhausted, 1);
                    Volatile.Write(ref _cleanupPending, 1);
                }
            }

            if (Volatile.Read(ref _token) == null
                && Interlocked.Exchange(ref _ownershipReleased, 1) == 0)
            {
                try
                {
                    _backend.ReleaseNodeOwnership();
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    // The node can already be gone during native shutdown.
                }
                catch (Exception exception)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            if (Volatile.Read(ref _token) == null
                && Interlocked.Exchange(ref _completionNotified, 1) == 0)
            {
                try
                {
                    _onStopped?.Invoke();
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    // Origin bookkeeping failure cannot block completed teardown.
                }
                catch (Exception exception)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            fatal?.Throw();
            return Volatile.Read(ref _token) == null;
        }

        private bool OnBusEnvelope(FoxTopicEnvelope<TDto> envelope)
        {
            if (IsStopped)
                return false;

            var ownsSequence = envelope.Sequence == 0;
            var candidateSequence = envelope.Sequence;
            if (ownsSequence && !_sequence.TryPeek(out candidateSequence))
            {
                SequenceExhaustedCount++;
                Stop();
                return false;
            }

            TEnvelope mapped = default;
            var mappingCompleted = false;
            ExceptionDispatchInfo fatal = null;
            try
            {
                mapped = _map(
                    envelope.Payload,
                    string.IsNullOrWhiteSpace(envelope.Origin) ? _origin : envelope.Origin,
                    candidateSequence,
                    envelope.TimestampNs,
                    FoxRunRos2CustomOutboundMappingPolicy.CreateContext());
                if (ReferenceEquals(mapped, null))
                    return false;
                mappingCompleted = true;

                if (ownsSequence
                    && (!_sequence.TryAllocate(out var allocatedSequence)
                        || allocatedSequence != candidateSequence))
                {
                    SequenceExhaustedCount++;
                    Stop();
                    return false;
                }

                var token = Volatile.Read(ref _token);
                if (token == null || !_backend.TryPublish(token, mapped))
                {
                    PublishFailureCount++;
                    return false;
                }

                PublishedCount++;
                return true;
            }
            catch (FoxRunRos2CustomOutboundBudgetExceededException)
            {
                BudgetRejectedCount++;
                return false;
            }
            catch (Exception exception) when (FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                if (mappingCompleted)
                    PublishFailureCount++;
                else
                    MapperFailureCount++;
                return false;
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                if (!ReferenceEquals(mapped, null))
                {
                    try
                    {
                        _dispose(mapped);
                    }
                    catch (Exception exception) when (
                        FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                    {
                        DisposeFailureCount++;
                    }
                    catch (Exception exception)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(exception);
                    }
                }
            }

            fatal?.Throw();
            return false;
        }

        private bool TryRemovePublisher(IFoxRunRos2NativePublisherToken token)
        {
            try
            {
                _backend.RemovePublisher(token);
                return true;
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                // Keep the token and node lease so the owner can retry after
                // the native runtime becomes available again.
                return false;
            }
        }
    }
}
#endif
