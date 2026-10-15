// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Focused FoxRun input router implementation section.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity.FoxgloveSDK.Schemas.MsgPack;

namespace Unity.FoxgloveSDK.Components
{
    public sealed partial class FoxRunInputRouter
    {
        public FoxRunInputDispatchResult Dispatch(
            string topic,
            byte[] payload,
            string encoding,
            double nowSeconds)
        {
            Registration[] registrations;
            var advertisedEncoding = encoding ?? string.Empty;
            var ordinaryRateAccepted = true;
            payload ??= Array.Empty<byte>();
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
                var hasMatchingEncoding = false;
                var hasMatchingPayloadLimit = false;
                var hasMatchingOrdinary = false;
                var hasMatchingStream = false;
                foreach (var registration in registrations)
                {
                    if (string.Equals(registration.Encoding, advertisedEncoding, StringComparison.OrdinalIgnoreCase))
                    {
                        hasMatchingEncoding = true;
                        if (payload.Length > registration.MaxPayloadBytes)
                            continue;
                        hasMatchingPayloadLimit = true;
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
                if (!hasMatchingPayloadLimit)
                {
                    return new FoxRunInputDispatchResult(
                        FoxRunInputDispatchStatus.PayloadTooLarge,
                        "Payload exceeds the frozen FoxRun session byte limit.",
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
                if (payload.Length > registration.MaxPayloadBytes)
                {
                    if (string.IsNullOrEmpty(firstError))
                        firstError =
                            "Payload exceeds the frozen FoxRun session byte limit.";
                    continue;
                }

                try
                {
                    if (!registration.Lifetime.TryEnter())
                    {
                        if (string.IsNullOrEmpty(firstError))
                            firstError = RegistrationUnavailableDiagnostic;
                        continue;
                    }
                    try
                    {
                        string error;
                        bool accepted;
                        if (registration.IsTransactional)
                        {
                            accepted = ((IFoxgloveTransactionalInputSource)registration.Source)
                                .FoxgloveInput_TryStageTransaction(
                                    registration.TopicIndex,
                                    payload,
                                    registration.ReadLimits,
                                    out error);
                        }
                        else
                        {
                            accepted = registration.Source.FoxgloveInput_TryStage(
                                registration.TopicIndex,
                                payload,
                                encoding,
                                out error);
                        }
                        if (accepted)
                        {
                            staged++;
                        }
                        else if (string.IsNullOrEmpty(firstError))
                        {
                            firstError = string.IsNullOrWhiteSpace(error)
                                ? SilentGeneratedRejectionDiagnostic
                                : error;
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
            IFoxgloveTransactionalInputSource transactionalInputSource,
            IFoxgloveOwnedInputSource ownedInputSource,
            IFoxgloveTransactionalOwnedInputSource transactionalOwnedInputSource)
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
                if (transactionalInputSource != null)
                {
                    foreach (var transactionIndex in lifetime.TransactionIndices)
                    {
                        try
                        {
                            transactionalInputSource.FoxgloveInput_ClearTransaction(
                                transactionIndex);
                        }
                        catch (Exception exception)
                        {
                            failure ??= ExceptionDispatchInfo.Capture(exception);
                        }
                    }
                }
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
                if (transactionalOwnedInputSource != null)
                {
                    foreach (var transactionIndex in lifetime.OwnedTransactionIndices)
                    {
                        try
                        {
                            transactionalOwnedInputSource
                                .FoxgloveInput_ClearTransactionalOwned(
                                    transactionIndex);
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
                lock (_gate)
                {
                    RemoveSourceRegistrationsUnderLock(lifetime.Source);
                    _registrationLifetimes.Remove(lifetime);
                }
                lifetime.CompleteCleanup();
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

    }
}
