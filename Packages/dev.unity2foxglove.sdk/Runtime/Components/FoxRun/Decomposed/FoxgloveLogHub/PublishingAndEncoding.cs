// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Provider-neutral runtime scheduler for generated FoxRun sources.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Util;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public sealed partial class FoxgloveLogHub : MonoBehaviour
    {
        private void TryPublishScheduled(
            IFoxgloveLogSource source,
            int topicIndex,
            FoxgloveLogTopicInfo info,
            ref FixedRatePublishState timer)
        {
            if (info.Policy != FoxRunPolicy.FixedRate
                && info.Policy != FoxRunPolicy.Change)
            {
                return;
            }

            var nowSeconds =
                Time.realtimeSinceStartupAsDouble;
            if (info.Policy == FoxRunPolicy.FixedRate)
            {
                var inheritedRate = _manager != null
                    ? _manager
                        .ActiveFoxRunDefaultPublishRateHz
                    : 10f;
                var rate = info.HasExplicitHz
                    ? info.Hz
                    : inheritedRate;
                if (!FixedRatePublishScheduler
                    .ShouldPublish(
                        nowSeconds,
                        rate,
                        ref timer,
                        nonPositivePublishesEveryFrame:
                        false))
                {
                    return;
                }
            }
            else
            {
                timer = default;
            }

            if (source
                    is IFoxgloveLogPolicySource policy
                && !policy.FoxgloveLog_ShouldPublish(
                    topicIndex,
                    nowSeconds))
            {
                return;
            }

            if (TryPublish(
                    source,
                    topicIndex,
                    explicitTrigger: false)
                && source
                    is IFoxgloveLogPolicySource
                    publishedPolicy)
            {
                publishedPolicy.FoxgloveLog_MarkPublished(
                    topicIndex,
                    nowSeconds);
            }
        }

        private bool TryPublish(
            IFoxgloveLogSource source,
            int topicIndex,
            bool explicitTrigger)
        {
            try
            {
                if (!_sources.TryGetValue(
                        source,
                        out var state)
                    || topicIndex < 0
                    || topicIndex
                    >= state.Topics.Length
                    || !state.Accepted[topicIndex])
                {
                    return false;
                }

                var info = state.Topics[topicIndex];
                if (explicitTrigger
                    && info.Policy != FoxRunPolicy.Trigger)
                {
                    return false;
                }

                if (source
                        is IFoxgloveLogConditionSource condition
                    && !condition.FoxgloveLog_CanPublish(
                        topicIndex))
                {
                    return false;
                }

                var suppressedTransportId = string.Empty;
                var suppressedGeneration = 0UL;
                if (source
                        is IFoxglovePublishOriginSource origin
                    && !origin.FoxgloveLog_CanPublishOrigin(
                        topicIndex,
                        explicitTrigger)
                    && (!(source
                          is IFoxRunRemoteOwnershipSource
                              remoteOwnership)
                        || !remoteOwnership
                            .FoxRunOrigin_TryGetRemoteApplied(
                                topicIndex,
                                out suppressedTransportId,
                                out suppressedGeneration)
                        || suppressedGeneration == 0
                        || string.IsNullOrWhiteSpace(
                            suppressedTransportId)))
                {
                    return false;
                }

                var publishWebSocket =
                    _manager?.ActiveFoxRunTransportSession != null
                    && SelectsWebSocket(info)
                    && !(string.Equals(
                             suppressedTransportId,
                             FoxgloveWebSocketTransport.Id,
                             StringComparison.Ordinal)
                         && _manager.IsActiveFoxRunPublishTransport(
                             suppressedTransportId,
                             suppressedGeneration));
                if (publishWebSocket
                    && source
                        is IFoxRunWebSocketCaptureSource
                            encodingSource)
                {
                    var encoding =
                        ResolveWebSocketEncoding(info);
                    encodingSource
                        .FoxgloveLog_SetWebSocketEncoding(
                            topicIndex,
                            encoding);
                }

                var recordingSource =
                    !publishWebSocket
                        ? source
                            as IFoxglovePublishRecordingSource
                        : null;
                var recordingReady = false;
                var recordingAllowed = true;
                if (recordingSource != null)
                {
                    if (source
                            is IFoxglovePublishRecordingPolicySource
                                recordingPolicy)
                    {
                        try
                        {
                            recordingAllowed =
                                recordingPolicy
                                    .FoxgloveLog_ShouldRecord(
                                        topicIndex);
                        }
                        catch (Exception exception)
                            when (FoxRunExceptionPolicy
                                .IsRecoverable(exception))
                        {
                            WarnOnce(
                                source,
                                topicIndex,
                                exception);
                            recordingAllowed = false;
                        }
                    }

                    if (recordingAllowed)
                    {
                        try
                        {
                            recordingReady =
                                recordingSource
                                    .FoxgloveLog_IsRecordingReady(
                                        topicIndex,
                                        _manager,
                                        out var recordingReason);
                            if (!recordingReady
                                && !string.IsNullOrWhiteSpace(
                                    recordingReason))
                            {
                                WarnOnce(
                                    source,
                                    topicIndex,
                                    new InvalidOperationException(
                                        recordingReason));
                            }
                        }
                        catch (Exception exception)
                            when (FoxRunExceptionPolicy
                                .IsRecoverable(exception))
                        {
                            WarnOnce(
                                source,
                                topicIndex,
                                exception);
                        }
                    }
                }

                var capture =
                    source as IFoxglovePublishCaptureSource;
                if (capture != null
                    && !capture.FoxgloveLog_BeginCapture(
                        topicIndex))
                {
                    return false;
                }

                var published = false;
                var recorded = false;
                var suppressExternalOutputs =
                    _manager?.SuppressLivePublishersForReplay == true;
                try
                {
                    var nowNs = _manager != null
                        ? _manager.NowNs
                        : checked(
                            (ulong)Math.Max(
                                0d,
                                Time.realtimeSinceStartupAsDouble
                                * 1_000_000_000d));
                    if (publishWebSocket
                        && _manager != null)
                    {
                        source.FoxgloveLog_Publish(
                            topicIndex,
                            _manager,
                            nowNs);
                        published = true;
                    }

                    if (!suppressExternalOutputs
                        && _manager != null
                        && source
                            is IFoxRunGeneratedTransportSource
                                generatedSource)
                    {
                        var providerResult =
                            _manager.PublishGeneratedTransports(
                                generatedSource,
                                topicIndex,
                                info.Topic,
                                info.PublishTransportIds,
                                nowNs,
                                suppressedTransportId,
                                suppressedGeneration);
                        if (providerResult.AnyAccepted)
                            published = true;
                        if (providerResult.Rejected > 0
                            || providerResult.Unavailable > 0
                            || providerResult.Failed > 0)
                        {
                            WarnOnce(
                                source,
                                topicIndex,
                                new InvalidOperationException(
                                    FoxRunGeneratedTransportFanout
                                        .FormatFailure(
                                            in providerResult)));
                        }
                    }

                    if (!suppressExternalOutputs
                        && source
                        is IFoxgloveTopicBusSource busSource)
                    {
                        var demanded =
                            !(source
                              is IFoxgloveTopicBusDemandSource
                                  demand)
                            || demand
                                .FoxgloveLog_HasBusSubscribers(
                                    topicIndex,
                                    _topicBus);
                        if (demanded)
                        {
                            busSource
                                .FoxgloveLog_PublishToBus(
                                    topicIndex,
                                    _topicBus,
                                    nowNs);
                            published = true;
                        }
                    }

                    if (!suppressExternalOutputs
                        && source
                            is IFoxgloveTopicObserverSource
                                observers
                        && observers.FoxgloveLog_HasObservers(
                            topicIndex,
                            _topicBus))
                    {
                        observers
                            .FoxgloveLog_PublishCapturedToObservers(
                                topicIndex,
                                _topicBus,
                                nowNs);
                        published = true;
                    }

                    if (!suppressExternalOutputs
                        && _sinkRouter.HasSinks
                        && source
                            is IFoxgloveTopicSinkSource
                                sinks)
                    {
                        sinks.FoxgloveLog_PublishToSinks(
                            topicIndex,
                            _sinkRouter,
                            nowNs);
                        published = true;
                    }

                    if (recordingReady && !suppressExternalOutputs)
                    {
                        try
                        {
                            if (recordingSource
                                .FoxgloveLog_RecordCaptured(
                                    topicIndex,
                                    _manager,
                                    nowNs,
                                    out var recordingReason))
                            {
                                recorded = true;
                                if (source
                                        is IFoxglovePublishRecordingPolicySource
                                            recordingPolicy)
                                {
                                    try
                                    {
                                        recordingPolicy
                                            .FoxgloveLog_MarkRecorded(
                                                topicIndex);
                                    }
                                    catch (Exception exception)
                                        when (FoxRunExceptionPolicy
                                            .IsRecoverable(exception))
                                    {
                                        WarnOnce(
                                            source,
                                            topicIndex,
                                            exception);
                                    }
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(
                                         recordingReason))
                            {
                                WarnOnce(
                                    source,
                                    topicIndex,
                                    new InvalidOperationException(
                                        recordingReason));
                            }
                        }
                        catch (Exception exception)
                            when (FoxRunExceptionPolicy
                                .IsRecoverable(exception))
                        {
                            WarnOnce(
                                source,
                                topicIndex,
                                exception);
                        }
                    }

                    // MCAP is an additive hidden sink. Its success must not
                    // consume a Change sample while a selected live Provider
                    // remains unavailable. A genuinely provider-less
                    // declaration is the sole recording-only exception.
                    return published
                           || (!HasSelectedPublishProviders(info)
                               && recorded);
                }
                finally
                {
                    capture?.FoxgloveLog_EndCapture(
                        topicIndex);
                }
            }
            catch (Exception exception)
                when (FoxRunExceptionPolicy
                    .IsRecoverable(exception))
            {
                WarnOnce(
                    source,
                    topicIndex,
                    exception);
                return false;
            }
        }

        private bool SelectsWebSocket(
            FoxgloveLogTopicInfo info)
        {
            var ids = info.PublishTransportIds;
            if (ids != null)
            {
                for (var index = 0;
                     index < ids.Count;
                     index++)
                {
                    if (string.Equals(
                            ids[index],
                            FoxgloveWebSocketTransport.Id,
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            var manager = _manager;
            if (manager == null)
                return false;

            var active =
                manager.ActiveFoxRunPublishSessionPolicy;
            var inherited =
                active != null && active.SessionActive
                    ? active.PublishTransportIds
                    : manager
                        .ConfiguredFoxRunPublishTransportIds;
            for (var index = 0;
                 index < inherited.Count;
                 index++)
            {
                if (inherited[index]
                    == FoxgloveWebSocketTransport.TransportId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasSelectedPublishProviders(
            FoxgloveLogTopicInfo info)
        {
            var ids = info.PublishTransportIds;
            if (ids != null)
                return ids.Count > 0;

            var manager = _manager;
            if (manager == null)
                return false;
            var active =
                manager.ActiveFoxRunPublishSessionPolicy;
            return active != null && active.SessionActive
                ? active.PublishTransportIds.Count > 0
                : manager
                    .ConfiguredFoxRunPublishTransportIds
                    .Count > 0;
        }

        private FoxRunEncoding ResolveWebSocketEncoding(
            FoxgloveLogTopicInfo info)
        {
            if (info.HasExplicitEncoding)
            {
                return FoxRunEncodingResolver
                    .ValidateProfileDefault(
                        info.DeclaredEncoding);
            }

            return _manager != null
                ? _manager.ActiveFoxRunPublishEncoding
                : FoxRunEncoding.Protobuf;
        }

        private FoxRunEncoding ResolveAdditiveSinkEncoding(
            FoxgloveLogTopicInfo info)
            => SelectsWebSocket(info)
               && ResolveWebSocketEncoding(info)
                   == FoxRunEncoding.MessagePack
                ? FoxRunEncoding.MessagePack
                : FoxRunEncoding.JSON;

    }
}
