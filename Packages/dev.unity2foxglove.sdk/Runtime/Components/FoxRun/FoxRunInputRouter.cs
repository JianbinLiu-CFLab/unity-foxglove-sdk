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
        Staged,
        UnknownTopic,
        PayloadTooLarge,
        RateLimited,
        DecodeRejected
    }

    public readonly struct FoxRunInputDispatchResult
    {
        public FoxRunInputDispatchResult(FoxRunInputDispatchStatus status, string diagnostic, int stagedCount)
        {
            Status = status;
            Diagnostic = diagnostic ?? string.Empty;
            StagedCount = stagedCount;
        }

        public FoxRunInputDispatchStatus Status { get; }
        public string Diagnostic { get; }
        /// <summary>Number of generated inputs that accepted and staged this payload.</summary>
        public int StagedCount { get; }
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
        private readonly List<IFoxgloveInputSource> _registeredSources = new();
        private IFoxgloveInputSource[] _sourceSnapshot = Array.Empty<IFoxgloveInputSource>();
        private FoxRunEncoding _defaultSubscriptionEncoding = FoxRunEncoding.Protobuf;
        private FoxRunEndpoint _defaultSubscriptionSource =
            FoxRunEndpoint.Foxglove;

        public FoxRunInputRouter(int maxPayloadBytes = 64 * 1024, int maxMessagesPerSecondPerTopic = 60)
        {
            MaxPayloadBytes = Math.Max(1, maxPayloadBytes);
            MaxMessagesPerSecondPerTopic = Math.Max(1, maxMessagesPerSecondPerTopic);
        }

        public int MaxPayloadBytes { get; set; }
        public int MaxMessagesPerSecondPerTopic { get; set; }

        /// <summary>Manager-resolved source used only when later registrations omit Source.</summary>
        public FoxRunEndpoint DefaultSubscriptionSource
        {
            get
            {
                lock (_gate)
                    return _defaultSubscriptionSource;
            }
            set
            {
                value = FoxRunEndpointResolver.ValidateProfileSource(value);
                lock (_gate)
                    _defaultSubscriptionSource = value;
            }
        }

        /// <summary>Manager-resolved default used only for inherited subscription topics.</summary>
        public FoxRunEncoding DefaultSubscriptionEncoding
        {
            get
            {
                lock (_gate)
                    return _defaultSubscriptionEncoding;
            }
            set
            {
                value = FoxRunEncodingResolver.ValidateProfileDefault(value);
                lock (_gate)
                {
                    if (_defaultSubscriptionEncoding == value)
                        return;

                    _defaultSubscriptionEncoding = value;
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

        public void Register(IFoxgloveInputSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            lock (_gate)
            {
                var addedRegistration = false;
                for (var index = 0; index < source.FoxgloveInput_TopicCount; index++)
                {
                    var info = source.FoxgloveInput_GetTopic(index);
                    if (string.IsNullOrWhiteSpace(info.Topic))
                        continue;
                    var topology = FoxRunEndpointResolver.Resolve(
                        info.Mode,
                        info.DeclaredSource,
                        info.HasExplicitSource,
                        declaredTargets: 0,
                        hasExplicitTargets: false,
                        info.DeclaredEncoding,
                        info.HasExplicitEncoding,
                        _defaultSubscriptionSource,
                        defaultTargets: FoxRunEndpoint.Foxglove,
                        publishDefaultEncoding: _defaultSubscriptionEncoding,
                        subscribeDefaultEncoding: _defaultSubscriptionEncoding);
                    if (!topology.Success
                        || topology.Topology.Source != FoxRunEndpoint.Foxglove
                        || !info.SupportsWebSocket)
                    {
                        continue;
                    }
                    if (!_registrations.TryGetValue(info.Topic, out var registrations))
                        _registrations[info.Topic] = registrations = new List<Registration>();
                    if (registrations.Exists(item => ReferenceEquals(item.Source, source) && item.TopicIndex == index))
                        continue;
                    registrations.Add(new Registration(
                        source,
                        index,
                        info.DeclaredEncoding,
                        info.HasExplicitEncoding,
                        info.Mode,
                        _defaultSubscriptionEncoding));
                    _registrationSnapshots[info.Topic] = registrations.ToArray();
                    addedRegistration = true;
                }

                if (addedRegistration)
                    AddSourceSnapshotEntry(source);
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

                RemoveSourceSnapshotEntry(source);
            }
        }

        /// <summary>
        /// Invokes each registered generated source once on the Unity main
        /// thread. Transport admission has already happened; sources decide
        /// whether their latest owned value is eligible for application.
        /// </summary>
        public int Flush(double nowSeconds, int inheritedSubscribeRateHz)
            => Flush(nowSeconds, inheritedSubscribeRateHz, reportApplyFailure: null);

        /// <summary>
        /// Invokes each registered generated source once and reports isolated
        /// non-fatal apply failures without preventing other sources from
        /// making main-thread progress.
        /// </summary>
        public int Flush(
            double nowSeconds,
            int inheritedSubscribeRateHz,
            Action<string> reportApplyFailure)
        {
            IFoxgloveInputSource[] sources;
            lock (_gate)
                sources = _sourceSnapshot;

            var applied = 0;
            foreach (var source in sources)
            {
                try
                {
                    applied += Math.Max(
                        0,
                        source.FoxgloveInput_Flush(nowSeconds, inheritedSubscribeRateHz));
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException)
                                           && !(ex is StackOverflowException)
                                           && !(ex is AccessViolationException))
                {
                    // Generated sources own their individual typed state. One
                    // source must not prevent other independently allowlisted
                    // contracts from making main-thread progress.
                    ReportApplyFailure(reportApplyFailure, source, ex);
                }
            }

            return applied;
        }

        private static void ReportApplyFailure(
            Action<string> reportApplyFailure,
            IFoxgloveInputSource source,
            Exception exception)
        {
            if (reportApplyFailure == null)
                return;

            var sourceType = source == null
                ? "unknown"
                : source.GetType().FullName ?? source.GetType().Name;
            var diagnostic = "FoxRun input apply failed for "
                             + sourceType
                             + " ("
                             + exception.GetType().Name
                             + ").";
            try
            {
                reportApplyFailure(diagnostic);
            }
            catch (Exception reportException) when (!(reportException is OutOfMemoryException)
                                                    && !(reportException is StackOverflowException)
                                                    && !(reportException is AccessViolationException))
            {
                // Diagnostics are best effort and must not undo source isolation.
            }
        }

        public FoxRunInputDispatchResult Dispatch(
            string topic,
            byte[] payload,
            string encoding,
            double nowSeconds)
        {
            Registration[] registrations;
            var advertisedEncoding = encoding ?? string.Empty;
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

                var hasMatchingEncoding = false;
                foreach (var registration in registrations)
                {
                    if (string.Equals(registration.Encoding, advertisedEncoding, StringComparison.OrdinalIgnoreCase))
                    {
                        hasMatchingEncoding = true;
                        break;
                    }
                }

                if (!hasMatchingEncoding)
                {
                    return new FoxRunInputDispatchResult(
                        FoxRunInputDispatchStatus.DecodeRejected,
                        "Inbound encoding does not match the generated FoxRun contract: expected \""
                            + registrations[0].Encoding
                            + "\" but client advertised \""
                            + advertisedEncoding
                            + "\".",
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

            var staged = 0;
            var firstError = string.Empty;
            foreach (var registration in registrations)
            {
                if (!string.Equals(registration.Encoding, advertisedEncoding, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(firstError))
                        firstError = "Inbound encoding does not match the generated FoxRun contract: expected \""
                            + registration.Encoding
                            + "\" but client advertised \""
                            + advertisedEncoding
                            + "\".";
                    continue;
                }

                try
                {
                    if (registration.Source.FoxgloveInput_TryStage(
                            registration.TopicIndex,
                            payload,
                            encoding,
                            out var error))
                    {
                        staged++;
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

            return staged > 0
                ? new FoxRunInputDispatchResult(FoxRunInputDispatchStatus.Staged, firstError, staged)
                : new FoxRunInputDispatchResult(FoxRunInputDispatchStatus.DecodeRejected, firstError, 0);
        }

        private void AddSourceSnapshotEntry(IFoxgloveInputSource source)
        {
            for (var index = 0; index < _registeredSources.Count; index++)
            {
                if (ReferenceEquals(_registeredSources[index], source))
                    return;
            }

            _registeredSources.Add(source);
            _sourceSnapshot = _registeredSources.ToArray();
        }

        private void RemoveSourceSnapshotEntry(IFoxgloveInputSource source)
        {
            for (var index = _registeredSources.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(_registeredSources[index], source))
                    _registeredSources.RemoveAt(index);
            }

            _sourceSnapshot = _registeredSources.ToArray();
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
                FoxRunEncoding declaredEncoding,
                bool hasExplicitEncoding,
                FoxRunFlow mode,
                FoxRunEncoding subscriptionDefault)
            {
                Source = source;
                TopicIndex = topicIndex;
                DeclaredEncoding = declaredEncoding;
                HasExplicitEncoding = hasExplicitEncoding;
                Mode = mode;
                Encoding = FoxRunEncodingResolver.ToProtocolEncoding(
                    hasExplicitEncoding
                        ? FoxRunEncodingResolver.ValidateProfileDefault(declaredEncoding)
                        : FoxRunEncodingResolver.ValidateProfileDefault(subscriptionDefault));
            }

            public IFoxgloveInputSource Source { get; }
            public int TopicIndex { get; }
            public FoxRunEncoding DeclaredEncoding { get; }
            public bool HasExplicitEncoding { get; }
            public FoxRunFlow Mode { get; }
            public string Encoding { get; }

            public Registration Resolve(FoxRunEncoding subscriptionDefault)
                => new(
                    Source,
                    TopicIndex,
                    DeclaredEncoding,
                    HasExplicitEncoding,
                    Mode,
                    subscriptionDefault);
        }
    }
}
