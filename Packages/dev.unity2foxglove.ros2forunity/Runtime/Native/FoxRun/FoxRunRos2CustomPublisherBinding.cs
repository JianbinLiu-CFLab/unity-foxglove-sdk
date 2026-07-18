// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Main-thread typed-bus binding for one custom ROS2 publisher endpoint.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
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
        private readonly Func<TDto, string, ulong, ulong, FoxRunRos2CustomOutboundMappingContext, TEnvelope> _map;
        private readonly Action<TEnvelope> _dispose;
        private readonly string _origin;
        private readonly FoxRunRos2CustomSequenceSource _sequence;
        private readonly Func<FoxRunRos2CustomTypesupportReadiness> _readiness;
        private readonly Action _onStopped;
        private readonly Action<FoxTopicEnvelope<TDto>> _busCallback;
        private IFoxRunRos2NativePublisherToken _token;
        private bool _subscribed;
        private int _stopped;

        internal FoxRunRos2CustomPublisherBinding(
            FoxRunRos2CustomPublisherContract contract,
            FoxTopicBus bus,
            IFoxRunRos2NativePublisherBackend backend,
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
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
            _origin = origin ?? string.Empty;
            _sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
            _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            _onStopped = onStopped;
            _busCallback = OnBusEnvelope;
        }

        internal bool IsStopped => Volatile.Read(ref _stopped) != 0;
        internal int PublishedCount { get; private set; }
        internal int MapperFailureCount { get; private set; }
        internal int PublishFailureCount { get; private set; }
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

            var registration = _backend.Register<TEnvelope>(_contract);
            if (!registration.Succeeded || registration.Token == null || !registration.Token.IsUsable)
            {
                if (registration.Token != null)
                    TryRemovePublisher(registration.Token);
                Stop();
                return FoxRunRos2RegistrationResult.Failure(
                    registration.Succeeded
                        ? FoxRunRos2RegistrationError.InvalidPublisherToken
                        : registration.Error,
                    registration.Diagnostic);
            }

            _token = registration.Token;
            try
            {
                _bus.Subscribe(_contract.Topic, _busCallback);
                _subscribed = true;
                return FoxRunRos2RegistrationResult.Success();
            }
            catch (Exception exception)
            {
                Stop();
                return FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.PublisherBackendFailure,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
                return;

            try
            {
                if (_subscribed)
                {
                    try
                    {
                        _bus.Unsubscribe(_contract.Topic, _busCallback);
                    }
                    finally
                    {
                        _subscribed = false;
                    }
                }

                var token = Interlocked.Exchange(ref _token, null);
                if (token != null)
                    _backend.RemovePublisher(token);
            }
            finally
            {
                try
                {
                    _backend.ReleaseNodeOwnership();
                }
                finally
                {
                    try
                    {
                        _onStopped?.Invoke();
                    }
                    catch (Exception)
                    {
                        // Origin cleanup is bookkeeping only. A user-supplied
                        // stop observer must never prevent endpoint teardown.
                    }
                }
            }
        }

        private void OnBusEnvelope(FoxTopicEnvelope<TDto> envelope)
        {
            if (IsStopped || !_sequence.TryPeek(out var candidateSequence))
            {
                if (!IsStopped)
                {
                    SequenceExhaustedCount++;
                    Stop();
                }
                return;
            }

            TEnvelope mapped = default;
            try
            {
                mapped = _map(
                    envelope.Payload,
                    _origin,
                    candidateSequence,
                    envelope.TimestampNs,
                    FoxRunRos2CustomOutboundMappingPolicy.CreateContext());
                if (ReferenceEquals(mapped, null))
                    return;

                if (!_sequence.TryAllocate(out var allocatedSequence) || allocatedSequence != candidateSequence)
                {
                    SequenceExhaustedCount++;
                    Stop();
                    return;
                }

                var token = Volatile.Read(ref _token);
                if (token == null || !_backend.TryPublish(token, mapped))
                {
                    PublishFailureCount++;
                    return;
                }

                PublishedCount++;
            }
            catch (FoxRunRos2CustomOutboundBudgetExceededException)
            {
                BudgetRejectedCount++;
            }
            catch (Exception)
            {
                MapperFailureCount++;
            }
            finally
            {
                if (!ReferenceEquals(mapped, null))
                    _dispose(mapped);
            }
        }

        private void TryRemovePublisher(IFoxRunRos2NativePublisherToken token)
        {
            try
            {
                _backend.RemovePublisher(token);
            }
            catch (Exception)
            {
                // Registration has already failed. The backend's own lease
                // release below remains mandatory; do not throw into a bus or
                // lifecycle callback while reporting the bounded failure.
            }
        }
    }
}
#endif
