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

    /// <summary>Aggregate result from one typed local-bus publication.</summary>
    public readonly struct FoxTopicPublishResult
    {
        internal FoxTopicPublishResult(int matched, int succeeded, int failed)
        {
            Matched = matched;
            Succeeded = succeeded;
            Failed = failed;
        }

        public int Matched { get; }
        public int Succeeded { get; }
        public int Failed { get; }
        public bool AllSucceeded => Matched > 0 && Succeeded == Matched && Failed == 0;
    }

    /// <summary>Process-local typed bus for FoxRun topic envelopes.</summary>
    /// <remarks>Not thread-safe. Register, subscribe, publish, and unsubscribe from the Unity main thread only.</remarks>
    public sealed class FoxTopicBus
    {
        private readonly Dictionary<string, Registration> _registrations = new Dictionary<string, Registration>(StringComparer.Ordinal);
        // Subscription arrays are replaced on every add/remove so each publish
        // observes the exact main-thread subscriber snapshot present at entry.
        private readonly Dictionary<string, ISubscription[]> _subscriptions = new Dictionary<string, ISubscription[]>(StringComparer.Ordinal);
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

            if (existing.HasOrigin(origin))
            {
                return ContractsMatch(existing.Contract, contract)
                    ? new FoxTopicRegistrationResult(true, string.Empty)
                    : new FoxTopicRegistrationResult(
                        false,
                        "Topic '" + contract.Topic
                        + "' registration from the existing origin does not match its accepted contract.");
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

            if (existing.Contract.WriterPolicy == FoxTopicWriterPolicy.MultiWriter)
            {
                return new FoxTopicRegistrationResult(
                    false,
                    "Topic '" + contract.Topic
                    + "' writer policy conflicts with the existing multi-writer registration.");
            }

            return new FoxTopicRegistrationResult(
                false,
                "Topic '" + contract.Topic + "' already has a single writer.");
        }

        /// <summary>
        /// Whether the exact contract and origin pair currently owns this
        /// topic.  This is the admission gate used by transport endpoint hubs;
        /// knowing only the topic or only the primary origin is insufficient.
        /// </summary>
        public bool IsRegistered(FoxTopicContract contract, string origin)
            => contract != null
               && _registrations.TryGetValue(contract.Topic, out var registration)
               && registration.HasOrigin(origin)
               && ContractsMatch(registration.Contract, contract);

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

            AddSubscription(
                topic,
                new Subscription<T>(++_nextSubscriptionId, callback));
        }

        /// <summary>
        /// Subscribe a result-bearing transport callback. Returning false
        /// reports a normal target rejection to the generated fanout path.
        /// </summary>
        public void SubscribeResult<T>(
            string topic,
            string origin,
            Func<FoxTopicEnvelope<T>, bool> callback)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic is required.", nameof(topic));
            if (string.IsNullOrWhiteSpace(origin))
                throw new ArgumentException("Origin is required.", nameof(origin));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            AddSubscription(
                topic,
                new ResultSubscription<T>(
                    ++_nextSubscriptionId,
                    origin,
                    callback));
        }

        public bool Unsubscribe<T>(string topic, Action<FoxTopicEnvelope<T>> callback)
        {
            if (string.IsNullOrWhiteSpace(topic) || callback == null)
                return false;
            if (!_subscriptions.TryGetValue(topic, out var snapshot))
                return false;

            for (var i = snapshot.Length - 1; i >= 0; i--)
            {
                if (snapshot[i] is Subscription<T> typedSubscription
                    && typedSubscription.Matches(callback))
                {
                    var subscriptionId = typedSubscription.Id;
                    RemoveSubscriptionAt(topic, snapshot, i);
                    RemoveReportedFaults(subscriptionId, topic);
                    return true;
                }
            }

            return false;
        }

        public bool UnsubscribeResult<T>(
            string topic,
            string origin,
            Func<FoxTopicEnvelope<T>, bool> callback)
        {
            if (string.IsNullOrWhiteSpace(topic)
                || string.IsNullOrWhiteSpace(origin)
                || callback == null)
                return false;
            if (!_subscriptions.TryGetValue(topic, out var snapshot))
                return false;

            for (var i = snapshot.Length - 1; i >= 0; i--)
            {
                if (snapshot[i] is ResultSubscription<T> typedSubscription
                    && typedSubscription.Matches(origin, callback))
                {
                    var subscriptionId = typedSubscription.Id;
                    RemoveSubscriptionAt(topic, snapshot, i);
                    RemoveReportedFaults(subscriptionId, topic);
                    return true;
                }
            }

            return false;
        }

        public bool HasSubscribers(string topic)
            => topic != null
               && _subscriptions.TryGetValue(topic, out var snapshot)
               && snapshot.Length > 0;

        /// <summary>
        /// Whether an exact ordinary observer is subscribed for the requested
        /// payload type. Result-bearing transport endpoints are deliberately
        /// excluded from this side-channel demand.
        /// </summary>
        public bool HasObservers<T>(string topic)
        {
            if (topic == null
                || !_subscriptions.TryGetValue(topic, out var snapshot))
                return false;
            for (var index = 0; index < snapshot.Length; index++)
            {
                if (snapshot[index] is Subscription<T>)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether an exact result-bearing transport endpoint is subscribed for
        /// the requested payload type.
        /// </summary>
        public bool HasResultSubscribers<T>(string topic, string origin)
        {
            if (topic == null
                || string.IsNullOrWhiteSpace(origin)
                || !_subscriptions.TryGetValue(topic, out var snapshot))
                return false;
            for (var index = 0; index < snapshot.Length; index++)
            {
                if (snapshot[index] is ResultSubscription<T> subscription
                    && subscription.AcceptsOrigin(origin))
                    return true;
            }
            return false;
        }

        public void Publish<T>(FoxTopicContract contract, ulong timestampNs, in T payload, string origin)
            => _ = PublishWithResult(contract, timestampNs, in payload, origin);

        private FoxTopicPublishResult PublishWithResult<T>(
            FoxTopicContract contract,
            ulong timestampNs,
            in T payload,
            string origin,
            ulong sequence = 0)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            if (!_subscriptions.TryGetValue(contract.Topic, out var snapshot) || snapshot.Length == 0)
                return default;

            var envelope = new FoxTopicEnvelope<T>(
                contract,
                timestampNs,
                payload,
                origin,
                sequence);
            var matched = 0;
            var succeeded = 0;
            var failed = 0;
            for (var i = 0; i < snapshot.Length; i++)
            {
                var subscription = snapshot[i];
                if (subscription is Subscription<T> typedSubscription)
                {
                    matched++;
                    if (typedSubscription.TryInvoke(envelope, out var exception))
                        succeeded++;
                    else
                    {
                        failed++;
                        ReportSubscriberFault(typedSubscription.Id, contract.Topic, origin, exception);
                    }
                }
                else if (subscription is ResultSubscription<T> resultSubscription)
                {
                    if (!resultSubscription.AcceptsOrigin(origin))
                        continue;
                    matched++;
                    if (resultSubscription.TryInvoke(envelope, out var exception))
                        succeeded++;
                    else
                    {
                        failed++;
                        if (exception != null)
                            ReportSubscriberFault(resultSubscription.Id, contract.Topic, origin, exception);
                    }
                }
                else
                {
                    if (subscription is IResultSubscription scoped
                        && !scoped.AcceptsOrigin(origin))
                    {
                        continue;
                    }
                    matched++;
                    failed++;
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

            return new FoxTopicPublishResult(matched, succeeded, failed);
        }

        /// <summary>
        /// Publish only to ordinary observers. Result-bearing transport
        /// endpoints are excluded so this independent side-channel can run
        /// exactly once per logical capture without changing a target verdict.
        /// </summary>
        public void PublishToObservers<T>(
            FoxTopicContract contract,
            ulong timestampNs,
            in T payload,
            string origin,
            ulong sequence = 0)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (!_subscriptions.TryGetValue(contract.Topic, out var snapshot)
                || snapshot.Length == 0)
            {
                return;
            }

            var envelope = new FoxTopicEnvelope<T>(
                contract,
                timestampNs,
                payload,
                origin,
                sequence);
            for (var index = 0; index < snapshot.Length; index++)
            {
                if (snapshot[index] is Subscription<T> observer)
                {
                    if (!observer.TryInvoke(envelope, out var exception))
                    {
                        ReportSubscriberFault(
                            observer.Id,
                            contract.Topic,
                            origin,
                            exception);
                    }
                    continue;
                }

                // Result-bearing callbacks describe a selected transport and
                // run only through PublishToResultSubscribers.
                if (snapshot[index] is IResultSubscription)
                    continue;

                var subscription = snapshot[index];
                if (HasReportedFault(
                        subscription.Id,
                        contract.Topic,
                        typeof(InvalidOperationException)))
                {
                    continue;
                }

                ReportSubscriberFault(
                    subscription.Id,
                    contract.Topic,
                    origin,
                    new InvalidOperationException(
                        "FoxRun topic '" + contract.Topic
                        + "' published payload type '" + typeof(T).FullName
                        + "' to incompatible subscriber type '"
                        + subscription.PayloadType.FullName + "'."));
            }
        }

        /// <summary>
        /// Publish only to result-bearing transport subscribers and aggregate
        /// only those callbacks. Ordinary observers stay on their independent
        /// side-channel and cannot change a transport target verdict.
        /// </summary>
        public FoxTopicPublishResult PublishToResultSubscribers<T>(
            FoxTopicContract contract,
            ulong timestampNs,
            in T payload,
            string origin,
            ulong sequence = 0)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (!_subscriptions.TryGetValue(contract.Topic, out var snapshot)
                || snapshot.Length == 0)
            {
                return default;
            }

            var envelope = new FoxTopicEnvelope<T>(
                contract,
                timestampNs,
                payload,
                origin,
                sequence);
            var matched = 0;
            var succeeded = 0;
            var failed = 0;
            for (var index = 0; index < snapshot.Length; index++)
            {
                if (!(snapshot[index] is ResultSubscription<T> subscription))
                    continue;
                if (!subscription.AcceptsOrigin(origin))
                    continue;
                matched++;
                if (subscription.TryInvoke(envelope, out var exception))
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                    if (exception != null)
                    {
                        ReportSubscriberFault(
                            subscription.Id,
                            contract.Topic,
                            origin,
                            exception);
                    }
                }
            }

            return new FoxTopicPublishResult(matched, succeeded, failed);
        }

        private void AddSubscription(string topic, ISubscription subscription)
        {
            if (!_subscriptions.TryGetValue(topic, out var snapshot))
            {
                _subscriptions.Add(topic, new[] { subscription });
                return;
            }

            var replacement = new ISubscription[snapshot.Length + 1];
            Array.Copy(snapshot, replacement, snapshot.Length);
            replacement[snapshot.Length] = subscription;
            _subscriptions[topic] = replacement;
        }

        private void RemoveSubscriptionAt(string topic, ISubscription[] snapshot, int index)
        {
            if (snapshot.Length == 1)
            {
                _subscriptions.Remove(topic);
                return;
            }

            var replacement = new ISubscription[snapshot.Length - 1];
            if (index > 0)
                Array.Copy(snapshot, 0, replacement, 0, index);
            if (index < snapshot.Length - 1)
            {
                Array.Copy(
                    snapshot,
                    index + 1,
                    replacement,
                    index,
                    snapshot.Length - index - 1);
            }
            _subscriptions[topic] = replacement;
        }

        private void ReportSubscriberFault(int subscriptionId, string topic, string origin, Exception exception)
        {
            var key = new SubscriberFaultKey(subscriptionId, topic, exception.GetType());
            if (!_reportedSubscriberFaults.Add(key))
                return;

            var handlers = SubscriberFaulted;
            if (handlers == null)
                return;

            var fault = new FoxTopicSubscriberFault(topic, origin, exception);
            foreach (Action<FoxTopicSubscriberFault> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(fault);
                }
                catch (Exception diagnosticException) when (
                    FoxRunExceptionPolicy.IsRecoverable(diagnosticException))
                {
                    // A diagnostic observer is not part of topic delivery.
                    // Keep notifying later observers and subscribers.
                }
            }
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
                   && string.Equals(existing.CanonicalType, candidate.CanonicalType, StringComparison.Ordinal)
                   && existing.Visibility == candidate.Visibility
                   && existing.WriterPolicy == candidate.WriterPolicy;
        }

        private interface ISubscription
        {
            int Id { get; }
            Type PayloadType { get; }
        }

        private interface IResultSubscription : ISubscription
        {
            bool AcceptsOrigin(string origin);
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
                catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                {
                    exception = ex;
                    return false;
                }
            }
        }

        private sealed class ResultSubscription<T> : IResultSubscription
        {
            private readonly string _origin;
            private readonly Func<FoxTopicEnvelope<T>, bool> _callback;

            public ResultSubscription(
                int id,
                string origin,
                Func<FoxTopicEnvelope<T>, bool> callback)
            {
                Id = id;
                _origin = origin;
                _callback = callback;
            }

            public int Id { get; }
            public Type PayloadType => typeof(T);

            public bool AcceptsOrigin(string origin)
                => string.Equals(_origin, origin, StringComparison.Ordinal);

            public bool Matches(
                string origin,
                Func<FoxTopicEnvelope<T>, bool> callback)
                => AcceptsOrigin(origin) && _callback == callback;

            public bool TryInvoke(FoxTopicEnvelope<T> envelope, out Exception exception)
            {
                try
                {
                    var accepted = _callback(envelope);
                    exception = null;
                    return accepted;
                }
                catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
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
