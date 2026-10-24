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

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

namespace Unity2Foxglove.ManualAcceptance
{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    [DisallowMultipleComponent]
    public sealed partial class Phase184StreamRoute : Phase184AcceptanceRoute
    {
        public const string StreamTopic = "/foxrun/phase184/stream/state";
        public const string OriginTopic = "/foxrun/phase184/zenoh/origin";
        private const float InitialDrainDelaySeconds = 0.5f;
        private const float StreamEvidenceTimeoutSeconds = 5f;
        private const float StreamTransportSettleSeconds = 0.5f;

        [FoxRun(
            StreamTopic,
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId =
                Phase184FoxRunProfileAcceptance.NativeTransportId,
            Reliability = FoxRunDeliveryReliability.BestEffort,
            Durability = FoxRunDeliveryDurability.Volatile,
            History = FoxRunDeliveryHistory.KeepLast,
            Depth = 5)]
        private FoxRunStream<Phase181State> _inputStream =
            new FoxRunStream<Phase181State>(
                new FoxRunStreamOptions(
                    32,
                    1000d,
                    16,
                    FoxRunStreamOverflowPolicy.DropOldest));

        [FoxRun(
            OriginTopic,
            Mode = FoxRunFlow.PublishAndSubscribe,
            SubscribeTransportId =
                Phase184FoxRunProfileAcceptance.NativeTransportId,
            PublishTransportIds = new[]
            {
                Phase184FoxRunProfileAcceptance.NativeTransportId
            },
            Reliability = FoxRunDeliveryReliability.BestEffort,
            Durability = FoxRunDeliveryDurability.Volatile,
            History = FoxRunDeliveryHistory.KeepLast,
            Depth = 5,
            Policy = FoxRunPolicy.Change)]
        [SerializeField] private Phase181State _zenohOrigin;

        [Header("Read-only Stream Evidence")]
        [SerializeField] private long _received;
        [SerializeField] private long _accepted;
        [SerializeField] private long _drained;
        [SerializeField] private long _replaced;
        [SerializeField] private long _rateDropped;
        [SerializeField] private long _maximumQueueDepth;
        [SerializeField] private long _disposalFailures;
        [SerializeField] private int _lastRetainedSequence = -1;
        [SerializeField] private bool _retainedOrdered = true;
        [SerializeField] private bool _ownershipBalanced;
        [SerializeField] private bool _remoteOriginApplied;
        [SerializeField] private long _sameOriginDrops;
        [SerializeField] private bool _laterLocalOrigin;

        private float _firstSampleAt = -1f;
        private float _streamEvidenceDeadline = -1f;
        private float _lastStreamActivityAt = -1f;
        private float _producerCompletionObservedAt = -1f;
        private long _lastObservedReceived;
        private string _subscriptionState = "Unavailable";
        private long _subscriptionReceived;
        private long _subscriptionCopyFailed;
        private long _subscriptionStaleCallbacks;
        private long _subscriptionRejectedAfterStop;

        protected override string RouteCaseId =>
            Phase184FoxRunProfileAcceptance.StreamCase;

        private void OnEnable()
        {
            if (!IsArmed)
            {
                enabled = false;
                return;
            }
            _firstSampleAt = -1f;
            _streamEvidenceDeadline = -1f;
            _lastStreamActivityAt = -1f;
            _producerCompletionObservedAt = -1f;
            _lastObservedReceived = _inputStream.Stats.Received;
            _subscriptionState = "Unavailable";
            _subscriptionReceived = 0;
            _subscriptionCopyFailed = 0;
            _subscriptionStaleCallbacks = 0;
            _subscriptionRejectedAfterStop = 0;
            _zenohOrigin = State(RunToken, "origin-warmup", 18440);
            Ready("streamCapacity=32 maxInputHz=1000 maxBatch=16 overflow=DropOldest");
        }

        private void OnDisable()
        {
            _inputStream?.Clear();
        }

        private void OnDestroy()
        {
            _inputStream?.Dispose();
        }

        private void Update()
        {
            if (!IsArmed || IsTerminal)
                return;

            var stats = _inputStream.Stats;
            if (stats.Received != _lastObservedReceived)
            {
                _lastObservedReceived = stats.Received;
                _lastStreamActivityAt = Time.realtimeSinceStartup;
            }
            if (_firstSampleAt < 0f && stats.Received > 0)
                _firstSampleAt = Time.realtimeSinceStartup;
            if (_firstSampleAt >= 0f
                && Time.realtimeSinceStartup - _firstSampleAt >= InitialDrainDelaySeconds)
            {
                _inputStream.Drain(ObserveRetainedSample);
                stats = _inputStream.Stats;
            }

            _received = stats.Received;
            _accepted = stats.Admitted;
            _drained = stats.Drained;
            _replaced = stats.DroppedOldest + stats.DroppedNewest;
            _rateDropped = stats.RateDropped;
            _maximumQueueDepth = stats.HighWater;
            _disposalFailures = stats.DisposalFailures;
            _ownershipBalanced =
                stats.Admitted
                == stats.Drained
                   + stats.DroppedOldest
                   + stats.DroppedNewest
                   + stats.Cleared
                   + _inputStream.Count;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
            if (FoxRunRos2SubscriptionAcceptanceDiagnostics.TryGet(
                    this,
                    StreamTopic,
                    out var streamSnapshot))
            {
                _subscriptionState = streamSnapshot.State.ToString();
                _subscriptionReceived = streamSnapshot.Received;
                _subscriptionCopyFailed = streamSnapshot.CopyFailed;
                _subscriptionStaleCallbacks = streamSnapshot.StaleCallbacks;
                _subscriptionRejectedAfterStop = streamSnapshot.RejectedAfterStop;
            }
            if (FoxRunRos2SubscriptionAcceptanceDiagnostics.TryGet(
                    this,
                    OriginTopic,
                    out var originSnapshot))
            {
                _sameOriginDrops = originSnapshot.SameOriginDrops;
            }
#endif

            if (!_remoteOriginApplied && IsState(_zenohOrigin, "origin-remote"))
            {
                _remoteOriginApplied = true;
                _producerCompletionObservedAt = Time.realtimeSinceStartup;
                Emit("PHASE184G_STREAM_REMOTE_ORIGIN_APPLIED", "stage=remote");
            }
            if (_remoteOriginApplied && !_laterLocalOrigin && _sameOriginDrops > 0)
            {
                _laterLocalOrigin = true;
                _zenohOrigin = State(RunToken, "origin-local", 18442);
                _streamEvidenceDeadline =
                    Time.realtimeSinceStartup + StreamEvidenceTimeoutSeconds;
                Emit("PHASE184G_STREAM_LOCAL_ORIGIN_MUTATED", "stage=local");
            }

            var streamTransportSettled =
                _producerCompletionObservedAt >= 0f
                && _lastStreamActivityAt >= 0f
                && Time.realtimeSinceStartup
                   - Mathf.Max(_producerCompletionObservedAt, _lastStreamActivityAt)
                >= StreamTransportSettleSeconds;
            if (_received > _inputStream.Options.Capacity
                && streamTransportSettled
                && _inputStream.Count == 0
                && _replaced > 0
                && _retainedOrdered
                && _lastRetainedSequence >= 0
                && _ownershipBalanced
                && _remoteOriginApplied
                && _sameOriginDrops > 0
                && _laterLocalOrigin)
            {
                Emit(
                    "PHASE184G_STREAM_SUBSCRIPTION_STATUS",
                    "state=" + _subscriptionState
                    + " received="
                    + _subscriptionReceived.ToString(CultureInfo.InvariantCulture)
                    + " copyFailed="
                    + _subscriptionCopyFailed.ToString(CultureInfo.InvariantCulture)
                    + " staleCallbacks="
                    + _subscriptionStaleCallbacks.ToString(CultureInfo.InvariantCulture)
                    + " rejectedAfterStop="
                    + _subscriptionRejectedAfterStop.ToString(CultureInfo.InvariantCulture));
                Pass(
                    "received=" + _received.ToString(CultureInfo.InvariantCulture)
                    + " accepted=" + _accepted.ToString(CultureInfo.InvariantCulture)
                    + " drained=" + _drained.ToString(CultureInfo.InvariantCulture)
                    + " replaced=" + _replaced.ToString(CultureInfo.InvariantCulture)
                    + " rateDropped=" + _rateDropped.ToString(CultureInfo.InvariantCulture)
                    + " highWater=" + _maximumQueueDepth.ToString(CultureInfo.InvariantCulture)
                     + " disposalFailures=" + _disposalFailures.ToString(CultureInfo.InvariantCulture)
                     + " lastSequence=" + _lastRetainedSequence.ToString(CultureInfo.InvariantCulture)
                     + " ordered=True ownershipBalanced=True");
                return;
            }

            if (_streamEvidenceDeadline > 0f
                && Time.realtimeSinceStartup >= _streamEvidenceDeadline)
            {
                Fail(BuildStreamFailureReason());
            }
        }

        private string BuildStreamFailureReason()
        {
            return "stream_evidence_incomplete_received_"
                   + _received.ToString(CultureInfo.InvariantCulture)
                   + "_capacity_" + _inputStream.Options.Capacity.ToString(CultureInfo.InvariantCulture)
                   + "_accepted_" + _accepted.ToString(CultureInfo.InvariantCulture)
                   + "_drained_" + _drained.ToString(CultureInfo.InvariantCulture)
                   + "_replaced_" + _replaced.ToString(CultureInfo.InvariantCulture)
                   + "_rateDropped_" + _rateDropped.ToString(CultureInfo.InvariantCulture)
                   + "_queue_" + _inputStream.Count.ToString(CultureInfo.InvariantCulture)
                   + "_highWater_" + _maximumQueueDepth.ToString(CultureInfo.InvariantCulture)
                   + "_disposalFailures_" + _disposalFailures.ToString(CultureInfo.InvariantCulture)
                   + "_lastSequence_" + _lastRetainedSequence.ToString(CultureInfo.InvariantCulture)
                   + "_ordered_" + _retainedOrdered
                   + "_ownershipBalanced_" + _ownershipBalanced
                   + "_remote_" + _remoteOriginApplied
                   + "_sameOriginDrops_" + _sameOriginDrops.ToString(CultureInfo.InvariantCulture)
                   + "_laterLocal_" + _laterLocalOrigin;
        }

        private void ObserveRetainedSample(Phase181State sample)
        {
            if (sample == null)
            {
                _retainedOrdered = false;
                return;
            }
            if (_lastRetainedSequence >= 0 && sample.Count <= _lastRetainedSequence)
                _retainedOrdered = false;
            _lastRetainedSequence = sample.Count;
        }
    }

}
