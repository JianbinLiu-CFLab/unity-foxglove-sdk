// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime hub for [FoxRun] attribute-based auto-publishing.
// FoxRunCodeGenerator produces the IFoxgloveLogSource implementations;
// this hub acts as their registry, relaying value updates to Foxglove
// topics through FoxgloveManager.

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Metadata for a FoxRun-published topic.</summary>
    public readonly struct FoxgloveLogTopicInfo
    {
        public readonly string Topic;
        public readonly float RateHz;
        public readonly FoxRunPublishMode PublishMode;
        public readonly float ChangeEpsilon;
        public readonly float ForceIntervalSeconds;

        public FoxgloveLogTopicInfo(string topic, float rateHz)
        {
            Topic = topic;
            RateHz = rateHz;
            PublishMode = FoxRunPublishMode.FixedRate;
            ChangeEpsilon = 0f;
            ForceIntervalSeconds = 0f;
        }

        public FoxgloveLogTopicInfo(string topic, float rateHz, FoxRunPublishMode publishMode,
            float changeEpsilon, float forceIntervalSeconds)
        {
            Topic = topic;
            RateHz = rateHz;
            PublishMode = publishMode;
            ChangeEpsilon = changeEpsilon < 0 ? 0 : changeEpsilon;
            ForceIntervalSeconds = forceIntervalSeconds;
        }
    }

    /// <summary>
    /// Interface implemented by code-generated <c>[FoxRun]</c> log sources.
    /// Provides topic metadata and per-topic publish dispatch.
    /// </summary>
    public interface IFoxgloveLogSource
    {
        /// <summary>Number of Foxglove topics published by this source.</summary>
        int FoxgloveLog_TopicCount { get; }
        /// <summary>Retrieve topic metadata by index.</summary>
        FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index);
        /// <summary>Publish the value for the given topic index through the manager.</summary>
        void FoxgloveLog_Publish(int topicIndex, FoxgloveManager mgr, ulong nowNs);
    }

    /// <summary>
    /// Optional FoxRun contract surface implemented by generated sources that
    /// expose stable topic identity for local routing and future fanout phases.
    /// </summary>
    public interface IFoxgloveTopicContractSource
    {
        /// <summary>Stable source identity used by the local topic bus.</summary>
        string FoxgloveLog_Origin { get; }
        /// <summary>Retrieve the topic contract by generated topic index.</summary>
        FoxTopicContract FoxgloveLog_GetContract(int index);
    }

    /// <summary>
    /// Optional side-channel implemented by generated sources that can publish
    /// typed envelopes to the process-local topic bus. The hub evaluates this
    /// route independently from the live WebSocket route so native output does
    /// not depend on a running WebSocket server.
    /// </summary>
    public interface IFoxgloveTopicBusSource
    {
        /// <summary>Publish one topic value to the local topic bus.</summary>
        void FoxgloveLog_PublishToBus(int topicIndex, FoxTopicBus bus, ulong nowNs);
    }

    /// <summary>
    /// Optional demand probe for generated typed-bus sources. It lets the hub
    /// avoid asking a source to build a payload when no local consumer is
    /// subscribed. Legacy generated sources that predate this probe retain their
    /// established side-channel behavior for binary compatibility.
    /// </summary>
    public interface IFoxgloveTopicBusDemandSource
    {
        /// <summary>Returns whether the specified topic has an interested local bus consumer.</summary>
        bool FoxgloveLog_HasBusSubscribers(int topicIndex, FoxTopicBus bus);
    }

    /// <summary>
    /// Optional side-channel implemented by generated sources that can fan one
    /// already-serialized topic payload out to additional sinks after live
    /// publish succeeds. The live Foxglove and MCAP paths remain primary; this
    /// side-channel only runs when at least one sink is registered.
    /// </summary>
    public interface IFoxgloveTopicSinkSource
    {
        /// <summary>Fan one serialized topic payload out to the sink router.</summary>
        void FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs);
    }

    /// <summary>
    /// Optional interface for event-driven FoxRun sources.
    /// Sources that implement this interface can suppress unchanged values
    /// and publish heartbeat frames. Sources that do not implement it
    /// continue to publish at fixed rate.
    /// </summary>
    public interface IFoxgloveLogPolicySource
    {
        /// <summary>Return true if the value for this topic should be published.</summary>
        bool FoxgloveLog_ShouldPublish(int topicIndex, double nowSeconds);
        /// <summary>Called after a successful publish to update last-value state.</summary>
        void FoxgloveLog_MarkPublished(int topicIndex, double nowSeconds);
    }

    /// <summary>
    /// Singleton hub that discovers <see cref="IFoxgloveLogSource"/> implementations
    /// at runtime, throttles them to their configured rates, and relays publishes
    /// through <see cref="FoxgloveManager"/>.
    /// </summary>
    [AddComponentMenu("")]
    public class FoxgloveLogHub : MonoBehaviour
    {
        // Internal state
        /// <summary>Singleton instance.</summary>
        private static FoxgloveLogHub _instance;
        private static readonly object PendingRegistrationsGate = new();
        private static readonly List<IFoxgloveLogSource> PendingRegistrations = new();
        private static readonly HashSet<IFoxgloveLogSource> PendingRegistrationSet = new();
        /// <summary>Cached reference to the FoxgloveManager.</summary>
        private FoxgloveManager _mgr;
        [SerializeField] private bool _enableFallbackSceneScan = true;
        /// <summary>Per-source scheduler state for rate throttling.</summary>
        private readonly Dictionary<IFoxgloveLogSource, FoxgloveLogSourceState> _timers = new();
        private readonly FoxTopicBus _topicBus = new();
        private readonly FoxTopicSinkRouter _sinkRouter = new();
        /// <summary>List of destroyed sources to clean up this frame.</summary>
        private readonly List<IFoxgloveLogSource> _stale = new();
        private readonly List<IFoxgloveLogSource> _registrationDrainBuffer = new();
        private readonly List<IFoxgloveLogSource> _pendingAdds = new();
        private readonly List<IFoxgloveLogSource> _pendingRemoves = new();
        private readonly HashSet<IFoxgloveLogSource> _pendingAddSet = new();
        private readonly HashSet<IFoxgloveLogSource> _pendingRemoveSet = new();
        private readonly HashSet<SourceFailureKey> _warnedSourceFailures = new();
        private bool _iteratingTimers;
        /// <summary>Countdown until the next Scan for new sources.</summary>
        private float _scanTimer;
        private float _scanInterval = ScanIntervalSeconds;
        private float _nextTriggerManagerSearchTime;
        /// <summary>Cooldown between FoxgloveManager search attempts.</summary>
        private float _mgrSearchCooldown;
        /// <summary>Cooldown between fallback FoxgloveManager search attempts.</summary>
        private const float ManagerSearchIntervalSeconds = 3f;
        /// <summary>Fallback scene scan interval used when generated sources did not self-register.</summary>
        private const float ScanIntervalSeconds = 2f;
        private const float MaxScanIntervalSeconds = 30f;

        /// <summary>Process-local FoxRun topic bus. Publish remains Unity main-thread only.</summary>
        public FoxTopicBus TopicBus => _topicBus;

        /// <summary>
        /// Additive multi-sink fanout for exported FoxRun topics. Add sinks to
        /// receive serialized payloads alongside the primary live/MCAP paths.
        /// Main-thread only.
        /// </summary>
        public FoxTopicSinkRouter TopicSinkRouter => _sinkRouter;

        private void Awake()
        {
            _sinkRouter.SinkFaulted += OnSinkFaulted;
        }

        /// <summary>
        /// Register a generated FoxRun source without waiting for the fallback scene scan.
        /// Call from Unity's main thread; the hub may create or touch Unity objects while
        /// Play Mode is active.
        /// </summary>
        public static void RegisterSource(IFoxgloveLogSource source)
        {
            if (source == null)
                return;

            lock (PendingRegistrationsGate)
            {
                if (PendingRegistrationSet.Add(source))
                    PendingRegistrations.Add(source);
            }

            if (Application.isPlaying)
                EnsureInstance();
        }

        /// <summary>Unregister a generated FoxRun source from the hub cache.</summary>
        public static void UnregisterSource(IFoxgloveLogSource source)
        {
            if (source == null)
                return;

            lock (PendingRegistrationsGate)
            {
                PendingRegistrationSet.Remove(source);
                PendingRegistrations.Remove(source);
            }

            if (_instance != null)
                _instance.RemoveSource(source);
        }

        /// <summary>
        /// Publish one generated FoxRun topic immediately from user code.
        /// Intended for Unity main-thread callbacks; external callbacks should
        /// marshal data back to the main thread before calling generated trigger
        /// methods because generated publishers may read Unity-owned state.
        /// </summary>
        /// <returns>
        /// True only after publish dispatch succeeds. Normal unavailable states
        /// return false instead of throwing.
        /// </returns>
        public static bool Trigger(IFoxgloveLogSource source, int topicIndex)
        {
            if (_instance == null || source == null)
                return false;
            return _instance.TriggerSource(source, topicIndex);
        }

        /// <summary>Try to get the process-local topic bus, creating the hidden hub in Play Mode when needed.</summary>
        public static bool TryGetTopicBus(out FoxTopicBus bus)
        {
            var instance = Application.isPlaying ? EnsureInstance() : _instance;
            bus = instance?._topicBus;
            return bus != null;
        }

        /// <summary>Try to get the additive FoxRun sink router, creating the hidden hub in Play Mode when needed.</summary>
        public static bool TryGetTopicSinkRouter(out FoxTopicSinkRouter router)
        {
            var instance = Application.isPlaying ? EnsureInstance() : _instance;
            router = instance?._sinkRouter;
            return router != null;
        }

        /// <summary>Reset static state when Unity enters Play Mode without domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            lock (PendingRegistrationsGate)
            {
                PendingRegistrations.Clear();
                PendingRegistrationSet.Clear();
            }
        }

        /// <summary>
        /// Ensures exactly one hub exists after scene load.
        /// Reuses a user-placed scene hub if present, otherwise creates a hidden
        /// <c>DontDestroyOnLoad</c> singleton.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            EnsureInstance();
        }

        private static FoxgloveLogHub EnsureInstance()
        {
            if (_instance != null)
                return _instance;

            var existing = FindFirstObjectByType<FoxgloveLogHub>();
            if (existing != null)
            {
                var isStale = existing.name == "[FoxRunHub]"
                    && (existing.hideFlags & HideFlags.HideAndDontSave) != 0;
                if (isStale)
                    DestroyImmediate(existing.gameObject);
                else
                {
                    _instance = existing;
                    _instance.DrainPendingRegistrations();
                    return _instance;
                }
            }

            var go = new GameObject("[FoxRunHub]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<FoxgloveLogHub>();
            _instance.DrainPendingRegistrations();
            return _instance;
        }

        /// <summary>
        /// Each frame: resolve the FoxgloveManager (with a 3-second retry cooldown),
        /// periodically scan for new log sources, and fire publishes for every source
        /// whose per-topic cadence is due. Event-driven sources can
        /// veto a timer tick when the generated last-value policy says nothing
        /// changed and no heartbeat is due.
        /// </summary>
        private void Update()
        {
            if (_mgr == null)
            {
                _mgrSearchCooldown -= Time.deltaTime;
                if (_mgrSearchCooldown <= 0f)
                {
                    _mgrSearchCooldown = ManagerSearchIntervalSeconds;
                    _mgr = FindFirstObjectByType<FoxgloveManager>();
                }
                if (_mgr == null) return;
            }
            DrainPendingRegistrations();
            if (_enableFallbackSceneScan)
            {
                _scanTimer -= Time.deltaTime;
                if (_scanTimer <= 0f)
                {
                    var added = Scan();
                    _scanInterval = added > 0
                        ? ScanIntervalSeconds
                        : Math.Min(_scanInterval * 2f, MaxScanIntervalSeconds);
                    _scanTimer = _scanInterval;
                }
            }

            var nowNs = _mgr.NowNs;
            var nowSec = Time.realtimeSinceStartupAsDouble;
            ApplyPendingTimerMutations();
            _stale.Clear();
            _iteratingTimers = true;
            try
            {
                foreach (var kv in _timers)
                {
                    if (kv.Key is MonoBehaviour mb)
                    {
                        if (mb == null) { _stale.Add(kv.Key); continue; }
                        if (!mb.isActiveAndEnabled) continue;
                    }

                    var state = kv.Value;
                    for (int i = 0; i < state.Timers.Length; i++)
                        TryPublishScheduledTopic(kv.Key, state.Topics[i], i, ref state.Timers[i], nowNs, nowSec);
                }
            }
            finally
            {
                _iteratingTimers = false;
            }
            foreach (var s in _stale) RemoveSource(s);
            ApplyPendingTimerMutations();
        }

        private bool TryPublishScheduledTopic(
            IFoxgloveLogSource source,
            FoxgloveLogTopicInfo info,
            int topicIndex,
            ref FixedRatePublishState timer,
            ulong nowNs,
            double nowSec)
        {
            try
            {
                if (info.PublishMode == FoxRunPublishMode.OnTrigger)
                    return false;

                var rateHz = info.RateHz;
                if (!TryResolvePublishRoutes(source, topicIndex, "scheduled publish", out var publishLive, out var publishBus))
                    return false;

                if (!FixedRatePublishScheduler.ShouldPublish(
                        nowSec,
                        rateHz,
                        ref timer,
                        nonPositivePublishesEveryFrame: false))
                    return false;

                if (!CanPublishSourceTopic(source, topicIndex, "scheduled publish"))
                    return false;

                var policySource = source as IFoxgloveLogPolicySource;
                if (policySource != null && !policySource.FoxgloveLog_ShouldPublish(topicIndex, nowSec))
                    return false;

                var published = DispatchTopic(source, topicIndex, nowNs, "scheduled publish", publishLive, publishBus);
                if (published)
                    policySource?.FoxgloveLog_MarkPublished(topicIndex, nowSec);
                return published;
            }
            catch (Exception ex) when (IsRecoverableSourceException(ex))
            {
                LogSourceFailure(source, topicIndex, "scheduled publish", ex);
                return false;
            }
        }

        private bool TryPublishTriggeredTopic(IFoxgloveLogSource source, int topicIndex, ulong nowNs, double nowSec)
        {
            try
            {
                if (!TryResolvePublishRoutes(source, topicIndex, "trigger publish", out var publishLive, out var publishBus))
                    return false;

                if (!CanPublishSourceTopic(source, topicIndex, "trigger publish"))
                    return false;

                var published = DispatchTopic(source, topicIndex, nowNs, "trigger publish", publishLive, publishBus);
                if (published && source is IFoxgloveLogPolicySource policySource)
                    policySource.FoxgloveLog_MarkPublished(topicIndex, nowSec);
                return published;
            }
            catch (Exception ex) when (IsRecoverableSourceException(ex))
            {
                LogSourceFailure(source, topicIndex, "trigger publish", ex);
                return false;
            }
        }

        private bool CanPublishSourceTopic(IFoxgloveLogSource source, int topicIndex, string operation)
        {
            var conditionSource = source as IFoxgloveLogConditionSource;
            if (conditionSource == null)
                return true;

            try
            {
                return conditionSource.FoxgloveLog_CanPublish(topicIndex);
            }
            catch (Exception ex) when (IsRecoverableSourceException(ex))
            {
                LogSourceFailure(source, topicIndex, operation + " condition", ex);
                return false;
            }
        }

        private void LogSourceFailure(IFoxgloveLogSource source, int topicIndex, string operation, Exception ex)
        {
            var sourceType = source?.GetType();
            var key = new SourceFailureKey(sourceType, topicIndex, operation);
            if (_warnedSourceFailures.Add(key))
            {
                var sourceName = sourceType?.FullName ?? "<null>";
                Debug.LogWarning($"[FoxRun] {operation} failed for {sourceName}[{topicIndex}]: {ex.Message}");
            }
        }

        private bool TryResolvePublishRoutes(
            IFoxgloveLogSource source,
            int topicIndex,
            string operation,
            out bool publishLive,
            out bool publishBus)
        {
            // Replay must not emit a second real-time external stream. Native
            // custom output is independent of WebSocket availability, but not
            // of the replay-output suppression boundary.
            var suppressExternalOutputForReplay = _mgr != null
                                                  && _mgr.SuppressLivePublishersForReplay;
            publishLive = _mgr != null
                          && _mgr.IsRunning
                          && !suppressExternalOutputForReplay;
            publishBus = false;
            if (!suppressExternalOutputForReplay && source is IFoxgloveTopicBusSource)
            {
                if (source is IFoxgloveTopicBusDemandSource demandSource)
                {
                    try
                    {
                        publishBus = demandSource.FoxgloveLog_HasBusSubscribers(topicIndex, _topicBus);
                    }
                    catch (Exception ex) when (IsRecoverableSourceException(ex))
                    {
                        LogSourceFailure(source, topicIndex, operation + " bus demand", ex);
                    }
                }
                else
                {
                    // Keep the legacy bus fanout coupled to its successful live
                    // publish. Only Phase181 custom sources opt into a native
                    // demand probe that can keep the bus route alive by itself.
                    publishBus = publishLive;
                }

                // Existing live fanout remains intact even for a source that
                // also owns one or more custom native topics. The demand probe
                // only adds the stopped-WebSocket custom-native path.
                if (!publishBus && publishLive)
                    publishBus = true;
            }

            return publishLive || publishBus;
        }

        private bool DispatchTopic(
            IFoxgloveLogSource source,
            int topicIndex,
            ulong nowNs,
            string operation,
            bool publishLive,
            bool publishBus)
        {
            var emitted = false;
            if (publishLive)
            {
                try
                {
                    source.FoxgloveLog_Publish(topicIndex, _mgr, nowNs);
                    emitted = true;
                    PublishTopicSinkSideChannel(source, topicIndex, nowNs, operation);
                }
                catch (Exception ex) when (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(source, topicIndex, operation, ex);
                }
            }

            if (publishBus && source is IFoxgloveTopicBusSource busSource)
            {
                try
                {
                    busSource.FoxgloveLog_PublishToBus(topicIndex, _topicBus, nowNs);
                    emitted = true;
                }
                catch (Exception ex) when (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(source, topicIndex, operation + " bus side-channel", ex);
                }
            }

            return emitted;
        }

        private void PublishTopicSinkSideChannel(
            IFoxgloveLogSource source,
            int topicIndex,
            ulong nowNs,
            string operation)
        {
            if (_sinkRouter.HasSinks && source is IFoxgloveTopicSinkSource sinkSource)
            {
                try
                {
                    sinkSource.FoxgloveLog_PublishToSinks(topicIndex, _sinkRouter, nowNs);
                }
                catch (Exception ex) when (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(source, topicIndex, operation + " sink side-channel", ex);
                }
            }
        }

        /// <summary>
        /// Finds every active MonoBehaviour implementing <see cref="IFoxgloveLogSource"/>
        /// and registers new sources in the timer dictionary.
        /// Uses a bounded backoff when no legacy sources are found.
        /// </summary>
        private int Scan()
        {
            var added = 0;
            var all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var mb in all)
            {
                if (mb is IFoxgloveLogSource src)
                    added += AddSource(src) ? 1 : 0;
            }

            return added;
        }

        private bool AddSource(IFoxgloveLogSource source)
        {
            if (_iteratingTimers)
            {
                if (_pendingAddSet.Add(source))
                    _pendingAdds.Add(source);
                return true;
            }

            return AddSourceNow(source);
        }

        private bool AddSourceNow(IFoxgloveLogSource source)
        {
            if (source == null || _timers.ContainsKey(source))
                return false;
            var count = source.FoxgloveLog_TopicCount;
            if (count > 0)
            {
                var topics = new FoxgloveLogTopicInfo[count];
                for (var i = 0; i < count; i++)
                    topics[i] = source.FoxgloveLog_GetTopic(i);

                _timers[source] = new FoxgloveLogSourceState(
                    new FixedRatePublishState[count],
                    topics);
                RegisterSourceContracts(source, count);
                return true;
            }

            return false;
        }

        private void RegisterSourceContracts(IFoxgloveLogSource source, int count)
        {
            var contractSource = source as IFoxgloveTopicContractSource;
            var origin = contractSource?.FoxgloveLog_Origin ?? source.GetType().FullName ?? string.Empty;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var contract = contractSource != null
                        ? contractSource.FoxgloveLog_GetContract(i)
                        : FallbackContract(source.FoxgloveLog_GetTopic(i));
                    if (contract == null)
                        continue;

                    var result = _topicBus.Register(contract, origin);
                    if (!result.Accepted)
                        LogSourceFailure(source, i, "topic contract registration", new InvalidOperationException(result.Diagnostic));

                    // Additive: export the contract to sinks (LocalOnly is gated
                    // inside the router). Live/MCAP keep their primary paths.
                    _sinkRouter.Register(contract);
                }
                catch (Exception ex) when (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(source, i, "topic contract registration", ex);
                }
            }
        }

        private void UnregisterSourceContracts(IFoxgloveLogSource source, int count)
        {
            var contractSource = source as IFoxgloveTopicContractSource;
            var origin = contractSource?.FoxgloveLog_Origin ?? source.GetType().FullName ?? string.Empty;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var contract = contractSource != null
                        ? contractSource.FoxgloveLog_GetContract(i)
                        : FallbackContract(source.FoxgloveLog_GetTopic(i));
                    if (contract != null)
                    {
                        _topicBus.Unregister(contract.Topic, origin);
                        _sinkRouter.Unregister(contract.Topic);
                    }
                }
                catch (Exception ex) when (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(source, i, "topic contract unregister", ex);
                }
            }
        }

        private static FoxTopicContract FallbackContract(FoxgloveLogTopicInfo info)
            => new FoxTopicContract(
                info.Topic,
                string.Empty,
                "json",
                string.Empty,
                string.Empty,
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

        private void RemoveSource(IFoxgloveLogSource source)
        {
            if (source == null)
                return;
            if (_iteratingTimers)
            {
                if (_pendingRemoveSet.Add(source))
                    _pendingRemoves.Add(source);
                return;
            }

            RemoveSourceNow(source);
        }

        private void RemoveSourceNow(IFoxgloveLogSource source)
        {
            if (source == null)
                return;
            if (!_timers.TryGetValue(source, out var state))
                return;

            UnregisterSourceContracts(source, state.Timers.Length);
            _timers.Remove(source);
        }

        private void ApplyPendingTimerMutations()
        {
            if (_pendingRemoves.Count > 0)
            {
                foreach (var source in _pendingRemoves)
                    RemoveSourceNow(source);
                _pendingRemoves.Clear();
                _pendingRemoveSet.Clear();
            }

            if (_pendingAdds.Count > 0)
            {
                foreach (var source in _pendingAdds)
                    AddSourceNow(source);
                _pendingAdds.Clear();
                _pendingAddSet.Clear();
            }
        }

        private void DrainPendingRegistrations()
        {
            _registrationDrainBuffer.Clear();
            lock (PendingRegistrationsGate)
            {
                if (PendingRegistrations.Count == 0)
                    return;

                _registrationDrainBuffer.AddRange(PendingRegistrations);
                PendingRegistrations.Clear();
                PendingRegistrationSet.Clear();
            }

            foreach (var source in _registrationDrainBuffer)
                AddSource(source);
            _registrationDrainBuffer.Clear();
        }

        private bool TriggerSource(IFoxgloveLogSource source, int topicIndex)
        {
            if (source == null)
                return false;
            if (topicIndex < 0 || topicIndex >= source.FoxgloveLog_TopicCount)
                return false;
            if (_mgr == null)
                TryRefreshManagerForTrigger();
            if (_mgr == null)
                return false;

            return TryPublishTriggeredTopic(source, topicIndex, _mgr.NowNs, Time.realtimeSinceStartupAsDouble);
        }

        private void TryRefreshManagerForTrigger()
        {
            var now = Time.realtimeSinceStartup;
            if (now < _nextTriggerManagerSearchTime)
                return;

            _nextTriggerManagerSearchTime = now + ManagerSearchIntervalSeconds;
            _mgr = FindFirstObjectByType<FoxgloveManager>();
        }

        private static bool IsRecoverableSourceException(Exception ex)
        {
            return !(ex is OutOfMemoryException)
                   && !(ex is StackOverflowException)
                   && !(ex is AccessViolationException)
                   && !(ex is AppDomainUnloadedException);
        }

        private static void OnSinkFaulted(FoxTopicSinkFault fault)
        {
            var message = "[FoxRun] Topic sink '" + fault.SinkName + "' failed during "
                          + fault.Operation + " for topic '" + fault.Topic + "': "
                          + fault.Exception.Message;
            Debug.LogWarning(message);
        }

        private sealed class FoxgloveLogSourceState
        {
            public FoxgloveLogSourceState(FixedRatePublishState[] timers, FoxgloveLogTopicInfo[] topics)
            {
                Timers = timers;
                Topics = topics;
            }

            public FixedRatePublishState[] Timers { get; }
            public FoxgloveLogTopicInfo[] Topics { get; }
        }

        private readonly struct SourceFailureKey : IEquatable<SourceFailureKey>
        {
            private readonly Type _sourceType;
            private readonly int _topicIndex;
            private readonly string _operation;

            public SourceFailureKey(Type sourceType, int topicIndex, string operation)
            {
                _sourceType = sourceType;
                _topicIndex = topicIndex;
                _operation = operation ?? string.Empty;
            }

            public bool Equals(SourceFailureKey other)
                => _sourceType == other._sourceType
                   && _topicIndex == other._topicIndex
                   && string.Equals(_operation, other._operation, StringComparison.Ordinal);

            public override bool Equals(object obj)
                => obj is SourceFailureKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _sourceType != null ? _sourceType.GetHashCode() : 0;
                    hash = (hash * 397) ^ _topicIndex;
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_operation);
                    return hash;
                }
            }
        }

        /// <summary>Clears all timers and nulls the singleton reference.</summary>
        private void OnDestroy()
        {
            _sinkRouter.SinkFaulted -= OnSinkFaulted;
            _timers.Clear();
            _pendingAdds.Clear();
            _pendingRemoves.Clear();
            _pendingAddSet.Clear();
            _pendingRemoveSet.Clear();
            _sinkRouter.Dispose();
            if (_instance == this) _instance = null;
        }
    }
}
