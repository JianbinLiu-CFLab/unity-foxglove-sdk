// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Constrained local typed bus for FoxRun topic envelopes.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    public readonly struct FoxTopicRegistrationResult
    {
        public FoxTopicRegistrationResult(bool accepted, string diagnostic)
        {
            Accepted = accepted;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Accepted { get; }
        public string Diagnostic { get; }
    }

    public sealed class FoxTopicSubscriberFault
    {
        public FoxTopicSubscriberFault(string topic, string origin, Exception exception)
        {
            Topic = topic ?? string.Empty;
            Origin = origin ?? string.Empty;
            Exception = exception;
        }

        public string Topic { get; }
        public string Origin { get; }
        public Exception Exception { get; }
    }

    /// <summary>Process-local typed bus for FoxRun topic envelopes.</summary>
    /// <remarks>Not thread-safe. Register, subscribe, publish, and unsubscribe from the Unity main thread only.</remarks>
    public sealed class FoxTopicBus
    {
        private readonly Dictionary<string, Registration> _registrations = new Dictionary<string, Registration>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ISubscription>> _subscriptions = new Dictionary<string, List<ISubscription>>(StringComparer.Ordinal);
        private readonly HashSet<SubscriberFaultKey> _reportedSubscriberFaults = new HashSet<SubscriberFaultKey>();
        private int _nextSubscriptionId;

        public event Action<FoxTopicSubscriberFault> SubscriberFaulted;

        public FoxTopicRegistrationResult Register(FoxTopicContract contract, string origin)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            origin ??= string.Empty;
            if (!_registrations.TryGetValue(contract.Topic, out var existing))
            {
                _registrations.Add(contract.Topic, new Registration(contract, origin));
                return new FoxTopicRegistrationResult(true, string.Empty);
            }

            if (existing.Contract.WriterPolicy == FoxTopicWriterPolicy.MultiWriter
                && contract.WriterPolicy == FoxTopicWriterPolicy.MultiWriter)
            {
                if (!ContractsMatch(existing.Contract, contract))
                {
                    return new FoxTopicRegistrationResult(
                        false,
                        "Topic '" + contract.Topic + "' multi-writer contract does not match the existing registration.");
                }

                existing.AddOrigin(origin);
                return new FoxTopicRegistrationResult(true, string.Empty);
            }

            if (existing.HasOrigin(origin))
                return new FoxTopicRegistrationResult(true, string.Empty);

            return new FoxTopicRegistrationResult(
                false,
                "Topic '" + contract.Topic + "' already has a single writer.");
        }

        public string GetRegisteredOrigin(string topic)
            => topic != null && _registrations.TryGetValue(topic, out var registration)
                ? registration.PrimaryOrigin
                : string.Empty;

        public bool Unregister(string topic, string origin)
        {
            if (string.IsNullOrWhiteSpace(topic))
                return false;

            if (!_registrations.TryGetValue(topic, out var registration))
                return false;

            if (!registration.RemoveOrigin(origin))
                return false;

            if (registration.IsEmpty)
                _registrations.Remove(topic);

            return true;
        }

        public void Subscribe<T>(string topic, Action<FoxTopicEnvelope<T>> callback)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic is required.", nameof(topic));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            if (!_subscriptions.TryGetValue(topic, out var list))
            {
                list = new List<ISubscription>();
                _subscriptions.Add(topic, list);
            }

            list.Add(new Subscription<T>(++_nextSubscriptionId, callback));
        }

        public bool Unsubscribe<T>(string topic, Action<FoxTopicEnvelope<T>> callback)
        {
            if (string.IsNullOrWhiteSpace(topic) || callback == null)
                return false;
            if (!_subscriptions.TryGetValue(topic, out var list))
                return false;

            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] is Subscription<T> typedSubscription
                    && typedSubscription.Matches(callback))
                {
                    var subscriptionId = typedSubscription.Id;
                    list.RemoveAt(i);
                    if (list.Count == 0)
                        _subscriptions.Remove(topic);
                    RemoveReportedFaults(subscriptionId, topic);
                    return true;
                }
            }

            return false;
        }

        public bool HasSubscribers(string topic)
            => topic != null
               && _subscriptions.TryGetValue(topic, out var list)
               && list.Count > 0;

        public void Publish<T>(FoxTopicContract contract, ulong timestampNs, in T payload, string origin)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            if (!_subscriptions.TryGetValue(contract.Topic, out var list) || list.Count == 0)
                return;

            var envelope = new FoxTopicEnvelope<T>(contract, timestampNs, payload, origin);
            for (var i = 0; i < list.Count; i++)
            {
                var subscription = list[i];
                if (subscription is Subscription<T> typedSubscription
                    && !typedSubscription.TryInvoke(envelope, out var exception))
                    ReportSubscriberFault(typedSubscription.Id, contract.Topic, origin, exception);
                else if (!(subscription is Subscription<T>))
                {
                    if (HasReportedFault(subscription.Id, contract.Topic, typeof(InvalidOperationException)))
                        continue;

                    ReportSubscriberFault(
                        subscription.Id,
                        contract.Topic,
                        origin,
                        new InvalidOperationException(
                            "FoxRun topic '" + contract.Topic + "' published payload type '"
                            + typeof(T).FullName + "' to incompatible subscriber type '"
                            + subscription.PayloadType.FullName + "'."));
                }
            }
        }

        private void ReportSubscriberFault(int subscriptionId, string topic, string origin, Exception exception)
        {
            var key = new SubscriberFaultKey(subscriptionId, topic, exception.GetType());
            if (!_reportedSubscriberFaults.Add(key))
                return;

            SubscriberFaulted?.Invoke(new FoxTopicSubscriberFault(topic, origin, exception));
        }

        private bool HasReportedFault(int subscriptionId, string topic, Type exceptionType)
            => _reportedSubscriberFaults.Contains(new SubscriberFaultKey(subscriptionId, topic, exceptionType));

        private void RemoveReportedFaults(int subscriptionId, string topic)
            => _reportedSubscriberFaults.RemoveWhere(key => key.SubscriptionId == subscriptionId
                                                            && string.Equals(key.Topic, topic, StringComparison.Ordinal));

        private static bool ContractsMatch(FoxTopicContract existing, FoxTopicContract candidate)
        {
            return string.Equals(existing.StableFingerprint, candidate.StableFingerprint, StringComparison.Ordinal)
                   && string.Equals(existing.SchemaName, candidate.SchemaName, StringComparison.Ordinal)
                   && string.Equals(existing.Encoding, candidate.Encoding, StringComparison.Ordinal)
                   && string.Equals(existing.CanonicalType, candidate.CanonicalType, StringComparison.Ordinal);
        }

        private interface ISubscription
        {
            int Id { get; }
            Type PayloadType { get; }
        }

        private sealed class Subscription<T> : ISubscription
        {
            private readonly Action<FoxTopicEnvelope<T>> _callback;

            public Subscription(int id, Action<FoxTopicEnvelope<T>> callback)
            {
                Id = id;
                _callback = callback;
            }

            public int Id { get; }
            public Type PayloadType => typeof(T);

            public bool Matches(Action<FoxTopicEnvelope<T>> callback)
                => _callback == callback;

            public bool TryInvoke(FoxTopicEnvelope<T> envelope, out Exception exception)
            {
                try
                {
                    _callback(envelope);
                    exception = null;
                    return true;
                }
                catch (Exception ex)
                {
                    exception = ex;
                    return false;
                }
            }
        }

        private readonly struct SubscriberFaultKey : IEquatable<SubscriberFaultKey>
        {
            public SubscriberFaultKey(int subscriptionId, string topic, Type exceptionType)
            {
                SubscriptionId = subscriptionId;
                Topic = topic ?? string.Empty;
                ExceptionType = exceptionType;
            }

            public int SubscriptionId { get; }
            public string Topic { get; }
            public Type ExceptionType { get; }

            public bool Equals(SubscriberFaultKey other)
            {
                return SubscriptionId == other.SubscriptionId
                       && string.Equals(Topic, other.Topic, StringComparison.Ordinal)
                       && ExceptionType == other.ExceptionType;
            }

            public override bool Equals(object obj)
                => obj is SubscriberFaultKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 31 + SubscriptionId;
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Topic);
                    hash = hash * 31 + (ExceptionType == null ? 0 : ExceptionType.GetHashCode());
                    return hash;
                }
            }
        }

        private sealed class Registration
        {
            private readonly HashSet<string> _origins = new HashSet<string>(StringComparer.Ordinal);

            public Registration(FoxTopicContract contract, string origin)
            {
                Contract = contract;
                PrimaryOrigin = origin ?? string.Empty;
                _origins.Add(PrimaryOrigin);
            }

            public FoxTopicContract Contract { get; }
            public string PrimaryOrigin { get; private set; }
            public bool IsEmpty => _origins.Count == 0;

            public void AddOrigin(string origin)
            {
                _origins.Add(origin ?? string.Empty);
            }

            public bool RemoveOrigin(string origin)
            {
                var normalized = origin ?? string.Empty;
                if (!_origins.Remove(normalized))
                    return false;

                if (PrimaryOrigin == normalized)
                    PrimaryOrigin = FirstOriginOrEmpty();
                return true;
            }

            public bool HasOrigin(string origin)
                => _origins.Contains(origin ?? string.Empty);

            private string FirstOriginOrEmpty()
            {
                foreach (var origin in _origins)
                    return origin;
                return string.Empty;
            }
        }
    }
}
