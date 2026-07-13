// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Deterministic allowlisted dispatch for generated FoxRun inputs.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    public enum FoxRunInputDispatchStatus
    {
        Applied,
        UnknownTopic,
        PayloadTooLarge,
        RateLimited,
        DecodeRejected
    }

    public readonly struct FoxRunInputDispatchResult
    {
        public FoxRunInputDispatchResult(FoxRunInputDispatchStatus status, string diagnostic, int appliedCount)
        {
            Status = status;
            Diagnostic = diagnostic ?? string.Empty;
            AppliedCount = appliedCount;
        }

        public FoxRunInputDispatchStatus Status { get; }
        public string Diagnostic { get; }
        public int AppliedCount { get; }
    }

    public sealed class FoxRunInputRouter
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, List<Registration>> _registrations =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, Registration[]> _registrationSnapshots =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, Queue<double>> _arrivalTimes =
            new(StringComparer.Ordinal);
        private FoxRunWireEncoding _defaultSubscriptionWireEncoding = FoxRunWireEncoding.Protobuf;

        public FoxRunInputRouter(int maxPayloadBytes = 64 * 1024, int maxMessagesPerSecondPerTopic = 60)
        {
            MaxPayloadBytes = Math.Max(1, maxPayloadBytes);
            MaxMessagesPerSecondPerTopic = Math.Max(1, maxMessagesPerSecondPerTopic);
        }

        public int MaxPayloadBytes { get; set; }
        public int MaxMessagesPerSecondPerTopic { get; set; }

        /// <summary>Manager-resolved default used only for inherited subscription topics.</summary>
        public FoxRunWireEncoding DefaultSubscriptionWireEncoding
        {
            get
            {
                lock (_gate)
                    return _defaultSubscriptionWireEncoding;
            }
            set
            {
                value = FoxRunWireEncodingResolver.ValidateManagerDefault(value);
                lock (_gate)
                {
                    if (_defaultSubscriptionWireEncoding == value)
                        return;

                    _defaultSubscriptionWireEncoding = value;
                    foreach (var pair in _registrations)
                    {
                        var registrations = pair.Value;
                        for (var index = 0; index < registrations.Count; index++)
                            registrations[index] = registrations[index].Resolve(value);
                        _registrationSnapshots[pair.Key] = registrations.ToArray();
                    }
                }
            }
        }

        /// <summary>Compatibility alias for the pre-Phase176 input router policy name.</summary>
        [Obsolete("Use DefaultSubscriptionWireEncoding.")]
        public FoxRunWireEncoding DefaultWireEncoding
        {
            get => DefaultSubscriptionWireEncoding;
            set => DefaultSubscriptionWireEncoding = value;
        }

        public void Register(IFoxgloveInputSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            lock (_gate)
            {
                for (var index = 0; index < source.FoxgloveInput_TopicCount; index++)
                {
                    var info = source.FoxgloveInput_GetTopic(index);
                    if (string.IsNullOrWhiteSpace(info.Topic))
                        continue;
                    if (!_registrations.TryGetValue(info.Topic, out var registrations))
                        _registrations[info.Topic] = registrations = new List<Registration>();
                    if (registrations.Exists(item => ReferenceEquals(item.Source, source) && item.TopicIndex == index))
                        continue;
                    registrations.Add(new Registration(
                        source,
                        index,
                        info.DeclaredWireEncoding,
                        info.Mode,
                        _defaultSubscriptionWireEncoding));
                    _registrationSnapshots[info.Topic] = registrations.ToArray();
                }
            }
        }

        public void Unregister(IFoxgloveInputSource source)
        {
            if (source == null)
                return;

            lock (_gate)
            {
                var emptyTopics = new List<string>();
                foreach (var pair in _registrations)
                {
                    if (pair.Value.RemoveAll(item => ReferenceEquals(item.Source, source)) == 0)
                        continue;

                    if (pair.Value.Count == 0)
                    {
                        emptyTopics.Add(pair.Key);
                    }
                    else
                    {
                        _registrationSnapshots[pair.Key] = pair.Value.ToArray();
                    }
                }

                foreach (var topic in emptyTopics)
                {
                    _registrations.Remove(topic);
                    _registrationSnapshots.Remove(topic);
                    _arrivalTimes.Remove(topic);
                }
            }
        }

        public FoxRunInputDispatchResult Dispatch(
            string topic,
            byte[] payload,
            string encoding,
            double nowSeconds)
        {
            Registration[] registrations;
            lock (_gate)
            {
                if (string.IsNullOrEmpty(topic)
                    || !_registrationSnapshots.TryGetValue(topic, out registrations)
                    || registrations.Length == 0)
                {
                    return new FoxRunInputDispatchResult(
                        FoxRunInputDispatchStatus.UnknownTopic,
                        "Topic is not in the generated FoxRun inbound allowlist.",
                        0);
                }

                payload ??= Array.Empty<byte>();
                if (payload.Length > Math.Max(1, MaxPayloadBytes))
                {
                    return new FoxRunInputDispatchResult(
                        FoxRunInputDispatchStatus.PayloadTooLarge,
                        "Payload exceeds the FoxRun inbound byte limit.",
                        0);
                }

                if (!AcceptRate(topic, nowSeconds))
                {
                    return new FoxRunInputDispatchResult(
                        FoxRunInputDispatchStatus.RateLimited,
                        "Topic exceeded the FoxRun inbound rate limit.",
                        0);
                }
            }

            if (registrations.Length == 0)
            {
                return new FoxRunInputDispatchResult(
                    FoxRunInputDispatchStatus.UnknownTopic,
                    "Topic is not in the generated FoxRun inbound allowlist.",
                    0);
            }

            var applied = 0;
            var firstError = string.Empty;
            foreach (var registration in registrations)
            {
                if (!string.Equals(registration.Encoding, encoding ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(firstError))
                        firstError = "Inbound encoding does not match the generated FoxRun contract: expected \""
                            + registration.Encoding
                            + "\" but client advertised \""
                            + (encoding ?? string.Empty)
                            + "\".";
                    continue;
                }

                try
                {
                    if (registration.Source.FoxgloveInput_TryApply(
                            registration.TopicIndex,
                            payload,
                            encoding,
                            out var error))
                    {
                        applied++;
                    }
                    else if (string.IsNullOrEmpty(firstError))
                    {
                        firstError = error;
                    }
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException)
                                           && !(ex is StackOverflowException)
                                           && !(ex is AccessViolationException))
                {
                    if (string.IsNullOrEmpty(firstError))
                        firstError = ex.Message;
                }
            }

            return applied > 0
                ? new FoxRunInputDispatchResult(FoxRunInputDispatchStatus.Applied, firstError, applied)
                : new FoxRunInputDispatchResult(FoxRunInputDispatchStatus.DecodeRejected, firstError, 0);
        }

        private bool AcceptRate(string topic, double nowSeconds)
        {
            if (!_arrivalTimes.TryGetValue(topic, out var arrivals))
                _arrivalTimes[topic] = arrivals = new Queue<double>();

            while (arrivals.Count > 0 && nowSeconds - arrivals.Peek() >= 1d)
                arrivals.Dequeue();
            if (arrivals.Count >= Math.Max(1, MaxMessagesPerSecondPerTopic))
                return false;
            arrivals.Enqueue(nowSeconds);
            return true;
        }

        private readonly struct Registration
        {
            public Registration(
                IFoxgloveInputSource source,
                int topicIndex,
                FoxRunWireEncoding declaredWireEncoding,
                FoxRunMode mode,
                FoxRunWireEncoding subscriptionDefault)
            {
                Source = source;
                TopicIndex = topicIndex;
                DeclaredWireEncoding = declaredWireEncoding;
                Mode = mode;
                Encoding = FoxRunWireEncodingResolver.ToProtocolEncoding(
                    FoxRunWireEncodingResolver.Resolve(
                        declaredWireEncoding,
                        mode,
                        subscriptionDefault,
                        subscriptionDefault));
            }

            public IFoxgloveInputSource Source { get; }
            public int TopicIndex { get; }
            public FoxRunWireEncoding DeclaredWireEncoding { get; }
            public FoxRunMode Mode { get; }
            public string Encoding { get; }

            public Registration Resolve(FoxRunWireEncoding subscriptionDefault)
                => new(Source, TopicIndex, DeclaredWireEncoding, Mode, subscriptionDefault);
        }
    }
}
