// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase184
// Purpose: Case-isolated Unity evidence surface for Phase184 profile acceptance.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Unity2Foxglove.Ros2Bridge;
using Debug = UnityEngine.Debug;

namespace Unity2Foxglove.ManualAcceptance
{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    internal enum Phase184TransportClientMarkerKind
    {
        None = 0,
        Normal = 1,
        Overflow = 2,
    }

    internal readonly struct Phase184TransportClientMarkerDecision
    {
        internal Phase184TransportClientMarkerDecision(
            Phase184TransportClientMarkerKind kind,
            int activeClientCount,
            long totalAcceptedClients)
        {
            Kind = kind;
            ActiveClientCount = activeClientCount;
            TotalAcceptedClients = totalAcceptedClients;
        }

        internal Phase184TransportClientMarkerKind Kind { get; }
        internal int ActiveClientCount { get; }
        internal long TotalAcceptedClients { get; }
    }

    internal sealed class Phase184TransportClientMarkerState
    {
        private readonly int[] _committedActiveCounts;
        private readonly long[] _committedAcceptedCounts;
        private int _committedPairCount;
        private int _pendingActive;
        private long _pendingAccepted;
        private bool _hasPending;
        private bool _overflowed;

        internal Phase184TransportClientMarkerState(int maximumMarkerCount)
        {
            if (maximumMarkerCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumMarkerCount));
            }

            _committedActiveCounts = new int[maximumMarkerCount];
            _committedAcceptedCounts = new long[maximumMarkerCount];
        }

        internal bool IsOverflowed => _overflowed;

        internal Phase184TransportClientMarkerDecision Observe(
            int active,
            long accepted)
        {
            if (_overflowed)
                return default;

            if (active < 0 || accepted < 0 || accepted < active)
            {
                ResetPending();
                return default;
            }

            if (IsCommitted(active, accepted))
            {
                ResetPending();
                return default;
            }

            if (!_hasPending
                || _pendingActive != active
                || _pendingAccepted != accepted)
            {
                _pendingActive = active;
                _pendingAccepted = accepted;
                _hasPending = true;
                return default;
            }

            ResetPending();
            if (_committedPairCount >= _committedActiveCounts.Length)
            {
                _overflowed = true;
                return new Phase184TransportClientMarkerDecision(
                    Phase184TransportClientMarkerKind.Overflow,
                    active,
                    accepted);
            }

            _committedActiveCounts[_committedPairCount] = active;
            _committedAcceptedCounts[_committedPairCount] = accepted;
            _committedPairCount++;
            return new Phase184TransportClientMarkerDecision(
                Phase184TransportClientMarkerKind.Normal,
                active,
                accepted);
        }

        internal void ResetPending()
        {
            _pendingActive = 0;
            _pendingAccepted = 0;
            _hasPending = false;
        }

        internal void Reset()
        {
            _committedPairCount = 0;
            _overflowed = false;
            ResetPending();
        }

        private bool IsCommitted(int active, long accepted)
        {
            for (var index = 0; index < _committedPairCount; index++)
            {
                if (_committedActiveCounts[index] == active
                    && _committedAcceptedCounts[index] == accepted)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal readonly struct Phase184AcceptanceRunContext
    {
        internal Phase184AcceptanceRunContext(
            string caseId,
            string token,
            int negativeSeconds)
        {
            CaseId = caseId;
            Token = token;
            NegativeSeconds = negativeSeconds;
            TokenDigestPrefix = Phase184AcceptanceText.TokenDigestPrefix(token);
        }

        internal string CaseId { get; }
        internal string Token { get; }
        internal int NegativeSeconds { get; }
        internal string TokenDigestPrefix { get; }
    }

    internal static class Phase184ContextDiagnostic
    {
        internal const string ManualRecoveryAction =
            " Start a fresh Phase184 helper, wait for endpoint readiness, enter Play Mode once; "
            + "each helper authorizes exactly one Play.";

        internal static string Format(string reason, bool isManual)
            => isManual ? (reason ?? string.Empty) + ManualRecoveryAction : reason;
    }

}
