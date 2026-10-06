// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Main-thread typed-bus binding for one custom ROS2 publisher endpoint.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
using System;
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
        private readonly object _cleanupGate = new object();
        private IFoxRunRos2NativePublisherToken _token;
        private bool _subscribed;
        private int _stopped;
        private int _cleanupPending;
        private int _cleanupRetryCount;
        private int _cleanupRetryExhausted;
        private int _cleanupFatal;
        private int _ownershipReleased;
        private int _ownershipReleaseInFlight;
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

        internal string Topic => _contract.Topic;
        internal bool IsStopped => Volatile.Read(ref _stopped) != 0;
        internal bool CleanupPending => Volatile.Read(ref _cleanupPending) != 0;
        internal bool CleanupRetryExhausted => Volatile.Read(ref _cleanupRetryExhausted) != 0;
        internal bool CleanupRetryFatal => Volatile.Read(ref _cleanupFatal) != 0;
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
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                try
                {
                    Stop();
                }
                catch (Exception)
                {
                }
                var error = FoxRunRos2NativeExceptionPolicy.TryGetNativeRuntimeSurfaceFailure(
                    exception,
                    out _)
                    ? FoxRunRos2RegistrationError.NativeRuntimeSurfaceUnavailable
                    : FoxRunRos2RegistrationError.PublisherBackendFailure;
                return FoxRunRos2RegistrationResult.Failure(
                    error,
                    exception.GetType().Name + ": " + exception.Message);
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
                    Volatile.Write(ref _cleanupFatal, 0);
                    Volatile.Write(ref _cleanupRetryCount, 0);
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
                        var retryCount = Interlocked.Increment(ref _cleanupRetryCount);
                        Volatile.Write(ref _cleanupPending, 1);
                        if (retryCount >= MaximumCleanupRetries)
                            Volatile.Write(ref _cleanupRetryExhausted, 1);
                    }
                    else
                    {
                        Interlocked.Exchange(ref _cleanupRetryCount, 0);
                        Volatile.Write(ref _cleanupRetryExhausted, 0);
                        Volatile.Write(ref _cleanupFatal, 0);
                        Interlocked.CompareExchange(ref _token, null, token);
                        Volatile.Write(ref _cleanupPending, 0);
                    }
                }
                catch (Exception exception)
                {
                    fatal = ExceptionDispatchInfo.Capture(exception);
                    Volatile.Write(ref _cleanupRetryExhausted, 1);
                    Volatile.Write(ref _cleanupFatal, 1);
                    Volatile.Write(ref _cleanupPending, 1);
                }
            }

            if (Volatile.Read(ref _token) == null
                && Volatile.Read(ref _ownershipReleased) == 0)
            {
                try
                {
                    if (!TryReleaseNodeOwnership())
                    {
                        var retryCount = Interlocked.Increment(ref _cleanupRetryCount);
                        Volatile.Write(ref _cleanupPending, 1);
                        if (retryCount >= MaximumCleanupRetries)
                            Volatile.Write(ref _cleanupRetryExhausted, 1);
                    }
                }
                catch (Exception exception)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                    Volatile.Write(ref _cleanupRetryExhausted, 1);
                    Volatile.Write(ref _cleanupFatal, 1);
                    Volatile.Write(ref _cleanupPending, 1);
                }
            }

            if (Volatile.Read(ref _token) == null
                && Volatile.Read(ref _ownershipReleased) != 0
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
            return Volatile.Read(ref _token) == null
                   && Volatile.Read(ref _ownershipReleased) != 0;
        }

        private bool TryReleaseNodeOwnership()
        {
            if (Volatile.Read(ref _ownershipReleased) != 0)
                return true;
            if (Interlocked.CompareExchange(ref _ownershipReleaseInFlight, 1, 0) != 0)
                return false;
            try
            {
                bool released;
                try
                {
                    released = _backend.ReleaseNodeOwnership();
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    return false;
                }
                if (released)
                {
                    Volatile.Write(ref _ownershipReleased, 1);
                    Volatile.Write(ref _cleanupRetryCount, 0);
                    Volatile.Write(ref _cleanupRetryExhausted, 0);
                    Volatile.Write(ref _cleanupFatal, 0);
                    Volatile.Write(ref _cleanupPending, 0);
                }
                return released;
            }
            finally
            {
                Volatile.Write(ref _ownershipReleaseInFlight, 0);
            }
        }

        /// <summary>
        /// Publishes one DTO captured by the generated Provider seam. The
        /// binding owns the envelope sequence and stamps its registered origin.
        /// </summary>
        internal bool TryPublishCaptured(TDto payload, ulong timestampNs)
            => PublishPayload(payload, _origin, 0UL, timestampNs);

        private bool OnBusEnvelope(FoxTopicEnvelope<TDto> envelope)
            => PublishPayload(
                envelope.Payload,
                envelope.Origin,
                envelope.Sequence,
                envelope.TimestampNs);

        private bool PublishPayload(
            TDto payload,
            string origin,
            ulong sequence,
            ulong timestampNs)
        {
            if (IsStopped)
                return false;

            var ownsSequence = sequence == 0;
            var candidateSequence = sequence;
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
                    payload,
                    string.IsNullOrWhiteSpace(origin) ? _origin : origin,
                    candidateSequence,
                    timestampNs,
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
