// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Deterministic allowlisted dispatch for generated FoxRun inputs.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity.FoxgloveSDK.Schemas.MsgPack;

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

    public sealed partial class FoxRunInputRouter
    {
        /// <remarks>Phase192 source sections: Dispatch.cs, Lifetime.cs</remarks>
        private const string RegistrationUnavailableDiagnostic =
            "A FoxRun input registration became unavailable during dispatch.";
        private const string SilentGeneratedRejectionDiagnostic =
            "A generated FoxRun input rejected the payload without a diagnostic.";

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
        private string _defaultSubscribeTransportId =
            FoxgloveWebSocketTransport.Id;

        public FoxRunInputRouter(int maxPayloadBytes = 64 * 1024, int maxMessagesPerSecondPerTopic = 60)
        {
            MaxPayloadBytes = Math.Max(1, maxPayloadBytes);
            MaxMessagesPerSecondPerTopic = Math.Max(1, maxMessagesPerSecondPerTopic);
        }

        public int MaxPayloadBytes { get; set; }
        public int MaxMessagesPerSecondPerTopic { get; set; }

        /// <summary>Manager-resolved transport used when declarations inherit.</summary>
        public string DefaultSubscribeTransportId
        {
            get
            {
                lock (_gate)
                    return _defaultSubscribeTransportId;
            }
            set
            {
                var id = new FoxRunTransportId(value);
                lock (_gate)
                    _defaultSubscribeTransportId = id.Value;
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
            var transactionalSource = source as IFoxgloveTransactionalInputSource;
            var transactionalOwnedInputSource =
                source as IFoxgloveTransactionalOwnedInputSource;
            var firstUnavailableDiagnostic = string.Empty;
            var registrationError = string.Empty;
            var pending = new List<PendingRegistration>();
            var requiredTransactionTopics = new HashSet<string>(StringComparer.Ordinal);
            var frozenMaxPayloadBytes = Math.Max(1, MaxPayloadBytes);
            var frozenReadLimits =
                FoxgloveMsgPackReadLimits.ForPayloadBytes(frozenMaxPayloadBytes);
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
                            if (!TryResolveWebSocketRegistration(
                                    info,
                                    _defaultSubscribeTransportId,
                                    _defaultSubscriptionEncoding,
                                    out var resolvedEncoding))
                                continue;
                            if (!FoxRunSchemaInfoRegistry.TryResolveSessionContract(
                                    source.GetType(),
                                    info.Topic,
                                    FoxRunFlow.Subscribe,
                                    resolvedEncoding,
                                    out _,
                                    out var sessionDiagnostic))
                            {
                                if (string.IsNullOrEmpty(firstUnavailableDiagnostic))
                                    firstUnavailableDiagnostic = sessionDiagnostic;
                                continue;
                            }
                            if (resolvedEncoding == FoxRunEncoding.MessagePack)
                            {
                                requiredTransactionTopics.Add(info.Topic);
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
                                    isTransactional: false,
                                    frozenMaxPayloadBytes,
                                    frozenReadLimits,
                                    lifetime)));
                        }

                        if (transactionalSource != null)
                        {
                            var transactionCount = Math.Max(
                                0,
                                transactionalSource.FoxgloveInput_TransactionCount);
                            for (var index = 0; index < transactionCount; index++)
                            {
                                var info =
                                    transactionalSource.FoxgloveInput_GetTransaction(index);
                                if (string.IsNullOrWhiteSpace(info.Topic))
                                    continue;
                                if (!TryResolveWebSocketRegistration(
                                        info,
                                        _defaultSubscribeTransportId,
                                        _defaultSubscriptionEncoding,
                                        out var resolvedEncoding)
                                    || resolvedEncoding
                                    != FoxRunEncoding.MessagePack)
                                {
                                    continue;
                                }
                                if (!FoxRunSchemaInfoRegistry.TryResolveSessionContract(
                                        source.GetType(),
                                        info.Topic,
                                        FoxRunFlow.Subscribe,
                                        FoxRunEncoding.MessagePack,
                                        out _,
                                        out var sessionDiagnostic))
                                {
                                    if (string.IsNullOrEmpty(firstUnavailableDiagnostic))
                                        firstUnavailableDiagnostic = sessionDiagnostic;
                                    continue;
                                }

                                requiredTransactionTopics.Remove(info.Topic);
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
                                        isTransactional: true,
                                        frozenMaxPayloadBytes,
                                        frozenReadLimits,
                                        lifetime)));
                                lifetime.RecordTransactionIndex(index);
                            }
                        }

                        if (requiredTransactionTopics.Count > 0
                            && string.IsNullOrEmpty(firstUnavailableDiagnostic))
                        {
                            firstUnavailableDiagnostic =
                                "FOXRUN619: Generated MessagePack transaction implementation is missing for topic '"
                                + string.Join("', '", requiredTransactionTopics)
                                + "'.";
                        }
                    }
                }

                for (var index = 0; index < pending.Count; index++)
                {
                    var registration = pending[index].Registration;
                    if (!registration.IsStream)
                        continue;
                    if (registration.IsTransactional)
                    {
                        if (transactionalOwnedInputSource == null)
                        {
                            registrationError =
                                "MessagePack FoxRunStream transaction does not implement transactional owned-input cleanup.";
                            break;
                        }
                        if (!transactionalOwnedInputSource
                                .FoxgloveInput_TryAcquireTransactionalOwned(
                                    registration.TopicIndex,
                                    out var transactionalOwnedError))
                        {
                            registrationError =
                                string.IsNullOrWhiteSpace(transactionalOwnedError)
                                    ? "FoxRun transactional owned input source is not ready for registration."
                                    : transactionalOwnedError;
                            break;
                        }
                        lifetime.RecordOwnedTransactionIndex(
                            registration.TopicIndex);
                        continue;
                    }
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
                                        && existing.TopicIndex == item.Registration.TopicIndex
                                        && existing.IsTransactional
                                        == item.Registration.IsTransactional))
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
                        TeardownRegistrationLifetime(
                            lifetime,
                            source as IFoxgloveTransactionalInputSource,
                            ownedInputSource,
                            transactionalOwnedInputSource);
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
            }

            TeardownRegistrationLifetime(
                lifetime,
                source as IFoxgloveTransactionalInputSource,
                source as IFoxgloveOwnedInputSource,
                source as IFoxgloveTransactionalOwnedInputSource);
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

    }
}
