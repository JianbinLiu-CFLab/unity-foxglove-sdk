// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Deterministic allowlisted dispatch for generated FoxRun inputs.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

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
        private readonly List<RegistrationLifetime> _registrationLifetimes = new();
        private IFoxgloveInputSource[] _sourceSnapshot = Array.Empty<IFoxgloveInputSource>();
        private FoxRunEncoding _defaultSubscriptionEncoding = FoxRunEncoding.Protobuf;
        private FoxRunEndpoint _defaultSubscriptionSource =
            FoxRunEndpoint.Foxglove;
        private FoxRunEndpoint _defaultPublishTargets =
            FoxRunEndpoint.Foxglove;

        public FoxRunInputRouter(int maxPayloadBytes = 64 * 1024, int maxMessagesPerSecondPerTopic = 60)
        {
            MaxPayloadBytes = Math.Max(1, maxPayloadBytes);
            MaxMessagesPerSecondPerTopic = Math.Max(1, maxMessagesPerSecondPerTopic);
        }

        public int MaxPayloadBytes { get; set; }
        public int MaxMessagesPerSecondPerTopic { get; set; }

        /// <summary>Manager-resolved targets used to validate inherited full-duplex constraints.</summary>
        public FoxRunEndpoint DefaultPublishTargets
        {
            get
            {
                lock (_gate)
                    return _defaultPublishTargets;
            }
            set
            {
                value = FoxRunEndpointResolver.ValidateProfileTargets(value);
                lock (_gate)
                    _defaultPublishTargets = value;
            }
        }

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

        public void Register(
            IFoxgloveInputSource source,
            Action<string> reportUnavailable = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            RegistrationLifetime lifetime;
            while (true)
            {
                RegistrationLifetime existing;
                lock (_gate)
                {
                    existing = FindRegistrationLifetimeUnderLock(source);
                    if (existing == null)
                    {
                        lifetime = new RegistrationLifetime(source);
                        _registrationLifetimes.Add(lifetime);
                        break;
                    }
                    if (existing.IsOpen)
                        return;
                }

                existing.WaitUntilInitialized();
                if (existing.IsOpen)
                    return;
                existing.WaitForCleanup();
            }

            var ownedInputSource = source as IFoxgloveOwnedInputSource;
            var firstUnavailableDiagnostic = string.Empty;
            var registrationError = string.Empty;
            var pending = new List<PendingRegistration>();
            var registrationOpened = false;
            ExceptionDispatchInfo failure = null;
            try
            {
                var topicCount = Math.Max(0, source.FoxgloveInput_TopicCount);
                var topics = new FoxgloveInputTopicInfo[topicCount];
                for (var index = 0; index < topicCount; index++)
                    topics[index] = source.FoxgloveInput_GetTopic(index);

                lock (_gate)
                {
                    if (lifetime.IsInitializing)
                    {
                        for (var index = 0; index < topics.Length; index++)
                        {
                            var info = topics[index];
                            if (string.IsNullOrWhiteSpace(info.Topic))
                                continue;
                            var topology = FoxRunEndpointResolver.Resolve(
                                info.Mode,
                                info.DeclaredSource,
                                info.HasExplicitSource,
                                info.DeclaredTargets,
                                info.HasExplicitTargets,
                                info.DeclaredEncoding,
                                info.HasExplicitEncoding,
                                _defaultSubscriptionSource,
                                _defaultPublishTargets,
                                publishDefaultEncoding: _defaultSubscriptionEncoding,
                                subscribeDefaultEncoding: _defaultSubscriptionEncoding,
                                info.HasExplicitQos);
                            if (!topology.Success
                                || topology.Topology.Source != FoxRunEndpoint.Foxglove
                                || !info.SupportsWebSocket)
                            {
                                if (topology.DiagnosticCode == FoxRunEndpointDiagnosticCode.QosRequiresRos2
                                    && string.IsNullOrEmpty(firstUnavailableDiagnostic))
                                {
                                    firstUnavailableDiagnostic = topology.DiagnosticMessage;
                                }
                                continue;
                            }
                            if (!FoxRunSchemaInfoRegistry.TryResolveSessionContract(
                                    source.GetType().FullName,
                                    info.Topic,
                                    FoxRunFlow.Subscribe,
                                    topology.Topology.SubscribeEncoding,
                                    out _,
                                    out var sessionDiagnostic))
                            {
                                if (string.IsNullOrEmpty(firstUnavailableDiagnostic))
                                    firstUnavailableDiagnostic = sessionDiagnostic;
                                continue;
                            }
                            pending.Add(new PendingRegistration(
                                info.Topic,
                                new Registration(
                                    source,
                                    index,
                                    info.DeclaredEncoding,
                                    info.HasExplicitEncoding,
                                    info.Mode,
                                    _defaultSubscriptionEncoding,
                                    info.IsStream,
                                    lifetime)));
                        }
                    }
                }

                for (var index = 0; index < pending.Count; index++)
                {
                    var registration = pending[index].Registration;
                    if (!registration.IsStream)
                        continue;
                    if (ownedInputSource == null)
                    {
                        registrationError =
                            "FoxRunStream input source does not implement owned-input cleanup.";
                        break;
                    }
                    if (!ownedInputSource.FoxgloveInput_TryAcquireOwned(
                            registration.TopicIndex,
                            out var ownedError))
                    {
                        registrationError = string.IsNullOrWhiteSpace(ownedError)
                            ? "FoxRun owned input source is not ready for registration."
                            : ownedError;
                        break;
                    }
                    lifetime.RecordOwnedTopicIndex(registration.TopicIndex);
                }

                if (string.IsNullOrEmpty(registrationError))
                {
                    lock (_gate)
                    {
                        if (lifetime.IsInitializing)
                        {
                            for (var index = 0; index < pending.Count; index++)
                            {
                                var item = pending[index];
                                if (!_registrations.TryGetValue(item.Topic, out var registrations))
                                    _registrations[item.Topic] = registrations = new List<Registration>();
                                if (registrations.Exists(existing =>
                                        ReferenceEquals(existing.Source, source)
                                        && existing.TopicIndex == item.Registration.TopicIndex))
                                {
                                    continue;
                                }
                                registrations.Add(item.Registration);
                                _registrationSnapshots[item.Topic] = registrations.ToArray();
                            }

                            if (pending.Count > 0)
                            {
                                AddSourceSnapshotEntry(source);
                                lifetime.Open();
                                registrationOpened = true;
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                if (!registrationOpened)
                {
                    lock (_gate)
                    {
                        lifetime.Close();
                        RemoveSourceRegistrationsUnderLock(source);
                    }
                }

                lifetime.EndInitialization();
                if (!registrationOpened)
                {
                    try
                    {
                        TeardownRegistrationLifetime(lifetime, ownedInputSource);
                    }
                    catch (Exception exception)
                    {
                        failure ??= ExceptionDispatchInfo.Capture(exception);
                    }
                }
            }

            failure?.Throw();

            var unavailableDiagnostic = string.IsNullOrEmpty(registrationError)
                ? firstUnavailableDiagnostic
                : registrationError;
            if (!string.IsNullOrEmpty(unavailableDiagnostic))
                reportUnavailable?.Invoke(unavailableDiagnostic);
        }

        public void Unregister(IFoxgloveInputSource source)
        {
            if (source == null)
                return;

            RegistrationLifetime lifetime;
            lock (_gate)
            {
                lifetime = FindRegistrationLifetimeUnderLock(source);
                if (lifetime == null)
                    return;
                lifetime.Close();
                RemoveSourceRegistrationsUnderLock(source);
            }

            TeardownRegistrationLifetime(
                lifetime,
                source as IFoxgloveOwnedInputSource);
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
            var ordinaryRateAccepted = true;
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
                var hasMatchingOrdinary = false;
                var hasMatchingStream = false;
                foreach (var registration in registrations)
                {
                    if (string.Equals(registration.Encoding, advertisedEncoding, StringComparison.OrdinalIgnoreCase))
                    {
                        hasMatchingEncoding = true;
                        if (registration.IsStream)
                            hasMatchingStream = true;
                        else
                            hasMatchingOrdinary = true;
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

                ordinaryRateAccepted = !hasMatchingOrdinary || AcceptRate(topic, nowSeconds);
                if (!ordinaryRateAccepted && !hasMatchingStream)
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
                if (!registration.IsStream && !ordinaryRateAccepted)
                {
                    if (string.IsNullOrEmpty(firstError))
                        firstError = "Ordinary FoxRun input exceeded the per-topic rate limit.";
                    continue;
                }

                try
                {
                    if (!registration.Lifetime.TryEnter())
                        continue;
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
                    finally
                    {
                        registration.Lifetime.Exit();
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

        private void RemoveSourceRegistrationsUnderLock(IFoxgloveInputSource source)
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

        private RegistrationLifetime FindRegistrationLifetimeUnderLock(
            IFoxgloveInputSource source)
        {
            for (var index = 0; index < _registrationLifetimes.Count; index++)
            {
                if (ReferenceEquals(_registrationLifetimes[index].Source, source))
                    return _registrationLifetimes[index];
            }
            return null;
        }

        private void TeardownRegistrationLifetime(
            RegistrationLifetime lifetime,
            IFoxgloveOwnedInputSource ownedInputSource)
        {
            if (!lifetime.TryClaimCleanup())
            {
                lifetime.WaitForCleanup();
                return;
            }

            try
            {
                lifetime.WaitForIdle();
                ExceptionDispatchInfo failure = null;
                if (ownedInputSource != null)
                {
                    foreach (var topicIndex in lifetime.OwnedTopicIndices)
                    {
                        try
                        {
                            ownedInputSource.FoxgloveInput_ClearOwned(topicIndex);
                        }
                        catch (Exception exception)
                        {
                            failure ??= ExceptionDispatchInfo.Capture(exception);
                        }
                    }
                }
                failure?.Throw();
            }
            finally
            {
                lifetime.CompleteCleanup();
                lock (_gate)
                    _registrationLifetimes.Remove(lifetime);
            }
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
                FoxRunEncoding subscriptionDefault,
                bool isStream,
                RegistrationLifetime lifetime)
            {
                Source = source;
                TopicIndex = topicIndex;
                DeclaredEncoding = declaredEncoding;
                HasExplicitEncoding = hasExplicitEncoding;
                Mode = mode;
                IsStream = isStream;
                Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
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
            public bool IsStream { get; }
            public RegistrationLifetime Lifetime { get; }
            public string Encoding { get; }

            public Registration Resolve(FoxRunEncoding subscriptionDefault)
                => new(
                    Source,
                    TopicIndex,
                    DeclaredEncoding,
                    HasExplicitEncoding,
                    Mode,
                    subscriptionDefault,
                    IsStream,
                    Lifetime);
        }

        private readonly struct PendingRegistration
        {
            internal PendingRegistration(string topic, Registration registration)
            {
                Topic = topic ?? string.Empty;
                Registration = registration;
            }

            internal string Topic { get; }
            internal Registration Registration { get; }
        }

        private sealed class RegistrationLifetime
        {
            private const int InitializingState = 0;
            private const int OpenState = 1;
            private const int ClosedState = 2;
            private const int CleanupCompleteState = 3;

            private readonly ManualResetEventSlim _initialized = new ManualResetEventSlim();
            private readonly ManualResetEventSlim _cleanupComplete = new ManualResetEventSlim();
            private readonly List<int> _ownedTopicIndices = new List<int>();
            private int _state = InitializingState;
            private int _inFlight = 1;
            private int _cleanupClaimed;

            internal RegistrationLifetime(IFoxgloveInputSource source)
                => Source = source ?? throw new ArgumentNullException(nameof(source));

            internal IFoxgloveInputSource Source { get; }
            internal IReadOnlyList<int> OwnedTopicIndices => _ownedTopicIndices;
            internal bool IsInitializing => Volatile.Read(ref _state) == InitializingState;
            internal bool IsOpen => Volatile.Read(ref _state) == OpenState;

            internal void Open()
            {
                if (Interlocked.CompareExchange(ref _state, OpenState, InitializingState) == InitializingState)
                    _initialized.Set();
            }

            internal void Close()
            {
                while (true)
                {
                    var state = Volatile.Read(ref _state);
                    if (state == ClosedState || state == CleanupCompleteState)
                        break;
                    if (Interlocked.CompareExchange(ref _state, ClosedState, state) == state)
                        break;
                }
                _initialized.Set();
            }

            internal void EndInitialization() => Exit();

            internal void RecordOwnedTopicIndex(int topicIndex)
            {
                if (!_ownedTopicIndices.Contains(topicIndex))
                    _ownedTopicIndices.Add(topicIndex);
            }

            internal bool TryEnter()
            {
                if (!IsOpen)
                    return false;
                Interlocked.Increment(ref _inFlight);
                if (IsOpen)
                    return true;
                Interlocked.Decrement(ref _inFlight);
                return false;
            }

            internal void Exit() => Interlocked.Decrement(ref _inFlight);
            internal bool TryClaimCleanup()
                => Interlocked.CompareExchange(ref _cleanupClaimed, 1, 0) == 0;
            internal void WaitUntilInitialized() => _initialized.Wait();
            internal void WaitForCleanup() => _cleanupComplete.Wait();

            internal void WaitForIdle()
            {
                var spinner = new SpinWait();
                while (Volatile.Read(ref _inFlight) != 0)
                    spinner.SpinOnce();
            }

            internal void CompleteCleanup()
            {
                Volatile.Write(ref _state, CleanupCompleteState);
                _initialized.Set();
                _cleanupComplete.Set();
            }
        }
    }
}
