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
                bool isTransactional,
                int maxPayloadBytes,
                FoxgloveMsgPackReadLimits readLimits,
                RegistrationLifetime lifetime)
            {
                Source = source;
                TopicIndex = topicIndex;
                DeclaredEncoding = declaredEncoding;
                HasExplicitEncoding = hasExplicitEncoding;
                Mode = mode;
                IsStream = isStream;
                IsTransactional = isTransactional;
                MaxPayloadBytes = Math.Max(1, maxPayloadBytes);
                ReadLimits = readLimits
                             ?? throw new ArgumentNullException(nameof(readLimits));
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
            public bool IsTransactional { get; }
            public int MaxPayloadBytes { get; }
            public FoxgloveMsgPackReadLimits ReadLimits { get; }
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
                    IsTransactional,
                    MaxPayloadBytes,
                    ReadLimits,
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
            private readonly List<int> _transactionIndices = new List<int>();
            private readonly List<int> _ownedTransactionIndices = new List<int>();
            private int _state = InitializingState;
            private int _inFlight = 1;
            private int _cleanupClaimed;

            internal RegistrationLifetime(IFoxgloveInputSource source)
                => Source = source ?? throw new ArgumentNullException(nameof(source));

            internal IFoxgloveInputSource Source { get; }
            internal IReadOnlyList<int> OwnedTopicIndices => _ownedTopicIndices;
            internal IReadOnlyList<int> TransactionIndices => _transactionIndices;
            internal IReadOnlyList<int> OwnedTransactionIndices =>
                _ownedTransactionIndices;
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

            internal void RecordTransactionIndex(int transactionIndex)
            {
                if (!_transactionIndices.Contains(transactionIndex))
                    _transactionIndices.Add(transactionIndex);
            }

            internal void RecordOwnedTransactionIndex(int transactionIndex)
            {
                if (!_ownedTransactionIndices.Contains(transactionIndex))
                    _ownedTransactionIndices.Add(transactionIndex);
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
