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
    public interface IFoxgloveLogSource
    {
        int FoxgloveLog_TopicCount { get; }
        FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index);
        void FoxgloveLog_Publish(
            int topicIndex,
            FoxgloveManager manager,
            ulong nowNs);
    }

    public interface IFoxgloveTopicContractSource
    {
        string FoxgloveLog_Origin { get; }
        FoxTopicContract FoxgloveLog_GetContract(int index);
    }

    public interface IFoxgloveTopicBusSource
    {
        void FoxgloveLog_PublishToBus(
            int topicIndex,
            FoxTopicBus bus,
            ulong nowNs);
    }

    public interface IFoxgloveTopicBusDemandSource
    {
        bool FoxgloveLog_HasBusSubscribers(
            int topicIndex,
            FoxTopicBus bus);
    }

    public interface IFoxgloveTopicObserverSource
    {
        bool FoxgloveLog_HasObservers(
            int topicIndex,
            FoxTopicBus bus);
        void FoxgloveLog_PublishCapturedToObservers(
            int topicIndex,
            FoxTopicBus bus,
            ulong nowNs);
    }

    public interface IFoxgloveTopicSinkSource
    {
        void FoxgloveLog_PublishToSinks(
            int topicIndex,
            FoxTopicSinkRouter router,
            ulong nowNs);
    }

    public interface IFoxglovePublishCaptureSource
    {
        bool FoxgloveLog_BeginCapture(int topicIndex);
        void FoxgloveLog_EndCapture(int topicIndex);
    }

    /// <summary>
    /// Optional generated MCAP seam for a topic whose selected publish
    /// transports do not include the built-in WebSocket transport. When
    /// WebSocket is selected, its ordinary Manager publish already records the
    /// same channel and this seam must not be invoked.
    /// </summary>
    public interface IFoxglovePublishRecordingSource
    {
        bool FoxgloveLog_IsRecordingReady(
            int topicIndex,
            FoxgloveManager manager,
            out string reason);

        bool FoxgloveLog_RecordCaptured(
            int topicIndex,
            FoxgloveManager manager,
            ulong nowNs,
            out string reason);
    }

    /// <summary>
    /// Optional generated policy seam for hidden recording. It lets a source
    /// remember that the current logical Change revision has already been
    /// accepted by MCAP while a selected live Provider remains pending.
    /// </summary>
    public interface IFoxglovePublishRecordingPolicySource
    {
        bool FoxgloveLog_ShouldRecord(int topicIndex);
        void FoxgloveLog_MarkRecorded(int topicIndex);
    }

    /// <summary>
    /// Optional generated seam used to freeze an inherited WebSocket encoding
    /// before capture. Other Providers own their wire encoding independently.
    /// </summary>
    public interface IFoxRunWebSocketCaptureSource
    {
        void FoxgloveLog_SetWebSocketEncoding(
            int topicIndex,
            FoxRunEncoding encoding);
    }

    public interface IFoxglovePublishOriginSource
    {
        bool FoxgloveLog_CanPublishOrigin(
            int topicIndex,
            bool explicitTrigger);
    }

    public interface IFoxgloveLogPolicySource
    {
        bool FoxgloveLog_ShouldPublish(
            int topicIndex,
            double nowSeconds);
        void FoxgloveLog_MarkPublished(
            int topicIndex,
            double nowSeconds);
    }

    [AddComponentMenu("")]
    public sealed class FoxgloveLogHub : MonoBehaviour
    {
        private const float ManagerSearchIntervalSeconds = 3f;
        private const float ScanIntervalSeconds = 2f;
        private const float MaxScanIntervalSeconds = 30f;

        private static FoxgloveLogHub _instance;
        private static readonly object PendingGate = new object();
        private static readonly List<IFoxgloveLogSource> Pending =
            new List<IFoxgloveLogSource>();
        private static readonly HashSet<IFoxgloveLogSource> PendingSet =
            new HashSet<IFoxgloveLogSource>();

        [SerializeField]
        private bool _enableFallbackSceneScan = true;

        private readonly Dictionary<IFoxgloveLogSource, SourceState>
            _sources =
                new Dictionary<IFoxgloveLogSource, SourceState>();
        private readonly List<IFoxgloveLogSource> _stale =
            new List<IFoxgloveLogSource>();
        private readonly List<IFoxgloveLogSource> _deferredAdds =
            new List<IFoxgloveLogSource>();
        private readonly List<IFoxgloveLogSource> _deferredRemoves =
            new List<IFoxgloveLogSource>();
        private readonly List<DeferredTrigger> _deferredTriggers =
            new List<DeferredTrigger>();
        private readonly FoxTopicBus _topicBus = new FoxTopicBus();
        private readonly FoxTopicSinkRouter _sinkRouter =
            new FoxTopicSinkRouter();
        private readonly HashSet<string> _reportedFailures =
            new HashSet<string>(StringComparer.Ordinal);

        private FoxgloveManager _manager;
        private bool _iterating;
        private float _managerSearchCooldown;
        private float _scanTimer;
        private float _scanInterval = ScanIntervalSeconds;

        public FoxTopicBus TopicBus => _topicBus;
        public FoxTopicSinkRouter TopicSinkRouter => _sinkRouter;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            lock (PendingGate)
            {
                Pending.Clear();
                PendingSet.Clear();
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            EnsureInstance();
        }

        public static void RegisterSource(
            IFoxgloveLogSource source)
        {
            if (source == null)
                return;
            var instance = _instance;
            if (instance != null)
            {
                instance.QueueAdd(source);
                return;
            }

            lock (PendingGate)
            {
                if (PendingSet.Add(source))
                    Pending.Add(source);
            }
        }

        public static void UnregisterSource(
            IFoxgloveLogSource source)
        {
            if (source == null)
                return;
            lock (PendingGate)
            {
                if (PendingSet.Remove(source))
                    Pending.Remove(source);
            }

            _instance?.QueueRemove(source);
        }

        public static bool Trigger(
            IFoxgloveLogSource source,
            int topicIndex)
        {
            if (source == null)
                return false;
            var instance = EnsureInstance();
            if (instance._iterating)
            {
                instance.QueueAdd(source);
                instance._deferredTriggers.Add(
                    new DeferredTrigger(source, topicIndex));
                return false;
            }

            instance.ApplyDeferred();
            if (!instance._sources.ContainsKey(source))
                instance.AddSourceNow(source);
            return instance.TryPublish(
                source,
                topicIndex,
                explicitTrigger: true);
        }

        public static bool TryGetTopicBus(
            out FoxTopicBus bus)
        {
            var instance = EnsureInstance();
            bus = instance?._topicBus;
            return bus != null;
        }

        public static bool TryGetTopicSinkRouter(
            out FoxTopicSinkRouter router)
        {
            var instance = EnsureInstance();
            router = instance?._sinkRouter;
            return router != null;
        }

        private static FoxgloveLogHub EnsureInstance()
        {
            if (_instance != null)
                return _instance;
            var existing =
                FindFirstObjectByType<FoxgloveLogHub>();
            if (existing != null)
            {
                _instance = existing;
                _instance.DrainPending();
                return _instance;
            }

            var gameObject =
                new GameObject("[FoxRunLogHub]");
            DontDestroyOnLoad(gameObject);
            gameObject.hideFlags =
                HideFlags.HideAndDontSave;
            _instance =
                gameObject.AddComponent<FoxgloveLogHub>();
            _instance.DrainPending();
            return _instance;
        }

        private void Awake()
        {
            if (_instance == null)
                _instance = this;
            _sinkRouter.SinkFaulted += OnSinkFaulted;
            DrainPending();
        }

        private void Update()
        {
            ResolveManager();
            DrainPending();
            ApplyDeferred();
            if (_enableFallbackSceneScan)
            {
                _scanTimer -= Time.deltaTime;
                if (_scanTimer <= 0f)
                {
                    var added = Scan();
                    _scanInterval = added > 0
                        ? ScanIntervalSeconds
                        : Math.Min(
                            _scanInterval * 2f,
                            MaxScanIntervalSeconds);
                    _scanTimer = _scanInterval;
                }
            }

            _stale.Clear();
            _iterating = true;
            try
            {
                foreach (var pair in _sources)
                {
                    var source = pair.Key;
                    if (source is MonoBehaviour behaviour)
                    {
                        if (behaviour == null)
                        {
                            _stale.Add(source);
                            continue;
                        }

                        if (!behaviour.isActiveAndEnabled)
                            continue;
                    }

                    var state = pair.Value;
                    for (var index = 0;
                         index < state.Topics.Length;
                         index++)
                    {
                        TryPublishScheduled(
                            source,
                            index,
                            state.Topics[index],
                            ref state.Timers[index]);
                    }
                }
            }
            finally
            {
                _iterating = false;
            }

            foreach (var source in _stale)
                RemoveSourceNow(source);
            ApplyDeferred();
        }

        private void ResolveManager()
        {
            if (_manager != null)
                return;
            _managerSearchCooldown -= Time.deltaTime;
            if (_managerSearchCooldown > 0f)
                return;
            _managerSearchCooldown =
                ManagerSearchIntervalSeconds;
            _manager =
                FindFirstObjectByType<FoxgloveManager>();
        }

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

                    if (_manager != null
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

                    if (source
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

                    if (source
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

                    if (_sinkRouter.HasSinks
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

                    if (recordingReady)
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

        private void QueueAdd(
            IFoxgloveLogSource source)
        {
            if (_iterating)
            {
                if (!_deferredAdds.Contains(source))
                    _deferredAdds.Add(source);
                _deferredRemoves.Remove(source);
                return;
            }

            AddSourceNow(source);
        }

        private void QueueRemove(
            IFoxgloveLogSource source)
        {
            if (_iterating)
            {
                if (!_deferredRemoves.Contains(source))
                    _deferredRemoves.Add(source);
                _deferredAdds.Remove(source);
                return;
            }

            RemoveSourceNow(source);
        }

        private void ApplyDeferred()
        {
            if (_iterating)
                return;

            if (_deferredRemoves.Count > 0)
            {
                var removes = _deferredRemoves.ToArray();
                _deferredRemoves.Clear();
                foreach (var source in removes)
                    RemoveSourceNow(source);
            }

            if (_deferredAdds.Count > 0)
            {
                var adds = _deferredAdds.ToArray();
                _deferredAdds.Clear();
                foreach (var source in adds)
                    AddSourceNow(source);
            }

            if (_deferredTriggers.Count > 0)
            {
                var triggers = _deferredTriggers.ToArray();
                _deferredTriggers.Clear();
                foreach (var trigger in triggers)
                {
                    TryPublish(
                        trigger.Source,
                        trigger.TopicIndex,
                        explicitTrigger: true);
                }
            }
        }

        private bool AddSourceNow(
            IFoxgloveLogSource source)
        {
            if (source == null
                || _sources.ContainsKey(source))
            {
                return false;
            }

            var count = Math.Max(
                0,
                source.FoxgloveLog_TopicCount);
            var topics =
                new FoxgloveLogTopicInfo[count];
            var accepted = new bool[count];
            var timers =
                new FixedRatePublishState[count];
            var contracts =
                new FoxTopicContract[count];
            var contractSource =
                source as IFoxgloveTopicContractSource;
            var origin =
                contractSource?.FoxgloveLog_Origin
                ?? SourceOrigin(source);
            for (var index = 0;
                 index < count;
                 index++)
            {
                try
                {
                    var info =
                        source.FoxgloveLog_GetTopic(index);
                    topics[index] = info;
                    if (string.IsNullOrWhiteSpace(
                            info.Topic))
                    {
                        continue;
                    }

                    var contract =
                        contractSource
                            ?.FoxgloveLog_GetContract(index)
                        ?? FallbackContract(info);
                    contracts[index] = contract;
                    var registration =
                        _topicBus.Register(
                            contract,
                            origin);
                    if (!registration.Accepted)
                    {
                        WarnOnce(
                            registration.Diagnostic);
                        continue;
                    }

                    if (contract.Visibility
                            == FoxTopicVisibility.Exported
                        && !_sinkRouter.Register(
                            contract,
                            ResolveAdditiveSinkEncoding(
                                info)))
                    {
                        _topicBus.Unregister(
                            contract.Topic,
                            origin);
                        WarnOnce(
                            "Topic sink contract conflict for '"
                            + contract.Topic
                            + "'.");
                        continue;
                    }

                    accepted[index] = true;
                }
                catch (Exception exception)
                    when (FoxRunExceptionPolicy
                        .IsRecoverable(exception))
                {
                    WarnOnce(
                        source,
                        index,
                        exception);
                }
            }

            _sources.Add(
                source,
                new SourceState(
                    topics,
                    timers,
                    accepted,
                    contracts,
                    origin));
            return true;
        }

        private void RemoveSourceNow(
            IFoxgloveLogSource source)
        {
            if (source == null
                || !_sources.TryGetValue(
                    source,
                    out var state))
            {
                return;
            }

            _sources.Remove(source);
            for (var index = 0;
                 index < state.Contracts.Length;
                 index++)
            {
                if (!state.Accepted[index]
                    || state.Contracts[index] == null)
                {
                    continue;
                }

                var contract =
                    state.Contracts[index];
                _topicBus.Unregister(
                    contract.Topic,
                    state.Origin);
                if (contract.Visibility
                    == FoxTopicVisibility.Exported)
                {
                    _sinkRouter.Unregister(
                        contract.Topic);
                }
            }
        }

        private int Scan()
        {
            var added = 0;
            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            foreach (var behaviour in behaviours)
            {
                if (behaviour
                        is IFoxgloveLogSource source
                    && AddSourceNow(source))
                {
                    added++;
                }
            }

            return added;
        }

        private void DrainPending()
        {
            IFoxgloveLogSource[] copy;
            lock (PendingGate)
            {
                copy = Pending.ToArray();
                Pending.Clear();
                PendingSet.Clear();
            }

            foreach (var source in copy)
                QueueAdd(source);
        }

        private static FoxTopicContract FallbackContract(
            FoxgloveLogTopicInfo info)
            => new FoxTopicContract(
                info.Topic,
                string.Empty,
                info.HasExplicitEncoding
                    ? FoxRunEncodingResolver
                        .ToProtocolEncoding(
                            info.DeclaredEncoding)
                    : "json",
                string.Empty,
                string.Empty,
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

        private static string SourceOrigin(
            IFoxgloveLogSource source)
        {
            var type = source.GetType();
            var instance = source is UnityEngine.Object value
                ? value.GetInstanceID()
                : source.GetHashCode();
            return (type.FullName ?? type.Name)
                   + ":"
                   + instance;
        }

        private void WarnOnce(
            IFoxgloveLogSource source,
            int topicIndex,
            Exception exception)
            => WarnOnce(
                (source?.GetType().FullName
                 ?? "unknown")
                + "|"
                + topicIndex
                + "|"
                + exception.GetType().FullName
                + "|"
                + exception.Message);

        private void WarnOnce(string message)
        {
            if (string.IsNullOrWhiteSpace(message)
                || !_reportedFailures.Add(message))
            {
                return;
            }

            Debug.LogWarning("[FoxRun] " + message);
        }

        private static void OnSinkFaulted(
            FoxTopicSinkFault fault)
        {
            if (fault?.Exception != null)
                Debug.LogException(fault.Exception);
        }

        private void OnDestroy()
        {
            _sinkRouter.SinkFaulted -= OnSinkFaulted;
            foreach (var source in
                     new List<IFoxgloveLogSource>(
                         _sources.Keys))
            {
                RemoveSourceNow(source);
            }

            _sinkRouter.Dispose();
            if (_instance == this)
                _instance = null;
        }

        private sealed class SourceState
        {
            internal SourceState(
                FoxgloveLogTopicInfo[] topics,
                FixedRatePublishState[] timers,
                bool[] accepted,
                FoxTopicContract[] contracts,
                string origin)
            {
                Topics = topics;
                Timers = timers;
                Accepted = accepted;
                Contracts = contracts;
                Origin = origin;
            }

            internal FoxgloveLogTopicInfo[] Topics { get; }
            internal FixedRatePublishState[] Timers { get; }
            internal bool[] Accepted { get; }
            internal FoxTopicContract[] Contracts { get; }
            internal string Origin { get; }
        }

        private readonly struct DeferredTrigger
        {
            internal DeferredTrigger(
                IFoxgloveLogSource source,
                int topicIndex)
            {
                Source = source;
                TopicIndex = topicIndex;
            }

            internal IFoxgloveLogSource Source { get; }
            internal int TopicIndex { get; }
        }
    }
}
