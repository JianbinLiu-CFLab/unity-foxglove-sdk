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
using System.Runtime.ExceptionServices;
using UnityEngine;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
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
    /// Phase184 side-channel for ordinary process-local observers. Generated
    /// target-aware sources publish their already-captured payload through
    /// this path exactly once, independently from selected transport targets.
    /// </summary>
    public interface IFoxgloveTopicObserverSource
    {
        bool FoxgloveLog_HasObservers(int topicIndex, FoxTopicBus bus);
        void FoxgloveLog_PublishCapturedToObservers(
            int topicIndex,
            FoxTopicBus bus,
            ulong nowNs);
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
    /// Optional Phase184 capture surface. A generated source captures every
    /// member for one logical publication exactly once, then all selected
    /// transports consume that immutable capture.
    /// </summary>
    public interface IFoxglovePublishCaptureSource
    {
        bool FoxgloveLog_BeginCapture(int topicIndex);
        void FoxgloveLog_EndCapture(int topicIndex);
    }

    /// <summary>
    /// Optional Phase184 target-aware publish surface. Readiness and publish
    /// outcomes stay independent so one unavailable target cannot reroute or
    /// block another selected target.
    /// </summary>
    public interface IFoxglovePublishTargetSource
    {
        bool FoxgloveLog_IsTargetReady(
            int topicIndex,
            FoxRunEndpoint target,
            FoxRunResolvedPublishContract contract,
            FoxgloveManager manager,
            FoxTopicBus bus,
            FoxTopicSinkRouter router,
            out string reason);

        bool FoxgloveLog_PublishCaptured(
            int topicIndex,
            FoxRunEndpoint target,
            FoxRunResolvedPublishContract contract,
            FoxgloveManager manager,
            FoxTopicBus bus,
            FoxTopicSinkRouter router,
            ulong nowNs,
            out string reason);
    }

    /// <summary>
    /// Optional external-boundary MCAP surface for declarations that do not
    /// select the live Foxglove target.
    /// </summary>
    public interface IFoxglovePublishRecordingSource
    {
        bool FoxgloveLog_IsRecordingReady(
            int topicIndex,
            FoxRunResolvedPublishContract contract,
            FoxgloveManager manager,
            out string reason);

        bool FoxgloveLog_RecordCaptured(
            int topicIndex,
            FoxRunResolvedPublishContract contract,
            FoxgloveManager manager,
            ulong nowNs,
            out string reason);
    }

    /// <summary>
    /// Optional origin gate for full-duplex declarations. Scheduled work must
    /// remain suppressed while the current value still equals the last remote
    /// apply; an explicit trigger is allowed to bypass this gate.
    /// </summary>
    public interface IFoxglovePublishOriginSource
    {
        bool FoxgloveLog_CanPublishOrigin(int topicIndex, bool explicitTrigger);
    }

    /// <summary>
    /// Optional interface for event-driven FoxRun sources.
    /// Sources that implement this interface can suppress unchanged values
    /// and publish Change-policy heartbeat frames. Sources that do not implement it
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
        private readonly List<IFoxgloveLogSource> _sourceRegistrationOrder = new();
        private readonly FoxTopicBus _topicBus = new();
        private readonly FoxTopicSinkRouter _sinkRouter = new();
        /// <summary>List of destroyed sources to clean up this frame.</summary>
        private readonly List<IFoxgloveLogSource> _stale = new();
        private readonly List<IFoxgloveLogSource> _registrationDrainBuffer = new();
        private readonly List<IFoxgloveLogSource> _pendingAdds = new();
        private readonly List<IFoxgloveLogSource> _pendingRemoves = new();
        private readonly HashSet<IFoxgloveLogSource> _pendingAddSet = new();
        private readonly HashSet<IFoxgloveLogSource> _pendingRemoveSet = new();
        private readonly Dictionary<SourceFailureKey, SourceFailureWarningState> _warnedSourceFailures = new();
        private static readonly long SourceFailureWarningIntervalTicks =
            Math.Max(1L, System.Diagnostics.Stopwatch.Frequency * 5L);
        private readonly Dictionary<SourceTopicKey, FoxRunPublishDispatchResult> _publishTargetStatuses = new();
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

        /// <summary>Try to read target health from the active hidden hub.</summary>
        public static bool TryGetActivePublishTargetStatus(
            IFoxgloveLogSource source,
            int topicIndex,
            out FoxRunPublishDispatchResult result)
        {
            result = default;
            var instance = _instance;
            return instance != null
                && instance.TryGetPublishTargetStatus(source, topicIndex, out result);
        }

        /// <summary>Try to read the latest target health for one declaration.</summary>
        public bool TryGetPublishTargetStatus(
            IFoxgloveLogSource source,
            int topicIndex,
            out FoxRunPublishDispatchResult result)
            => _publishTargetStatuses.TryGetValue(
                new SourceTopicKey(source, topicIndex),
                out result);

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
                    SetManager(FindFirstObjectByType<FoxgloveManager>());
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
                if (info.Policy != FoxRunPolicy.FixedRate
                    && info.Policy != FoxRunPolicy.Change)
                {
                    // Trigger is explicit-only. Unknown serialized policy
                    // values fail closed rather than becoming fixed-rate.
                    return false;
                }
                if (!IsRegisteredForDispatch(source, topicIndex))
                    return false;

                var targetAware = IsTargetAware(source);
                var publishLive = false;
                var publishBus = false;
                if (!targetAware
                    && !TryResolvePublishRoutes(source, topicIndex, "scheduled publish", out publishLive, out publishBus))
                    return false;

                switch (info.Policy)
                {
                    case FoxRunPolicy.FixedRate:
                        var publishRateHz = info.HasExplicitHz
                            ? info.Hz
                            : _mgr.ActiveFoxRunDefaultPublishRateHz;
                        if (!FixedRatePublishScheduler.ShouldPublish(
                                nowSec,
                                publishRateHz,
                                ref timer,
                                nonPositivePublishesEveryFrame: false))
                            return false;
                        break;

                    case FoxRunPolicy.Change:
                        // Change detection and its optional Hz-derived heartbeat
                        // must be evaluated every frame so a local mutation is
                        // not delayed by the heartbeat cadence.
                        timer = default;
                        break;

                    default:
                        return false;
                }

                if (!CanPublishSourceTopic(source, topicIndex, "scheduled publish"))
                    return false;
                if (source is IFoxglovePublishOriginSource originSource
                    && !originSource.FoxgloveLog_CanPublishOrigin(topicIndex, explicitTrigger: false))
                    return false;

                var policySource = source as IFoxgloveLogPolicySource;
                if (info.Policy == FoxRunPolicy.Change && policySource == null)
                    return false;
                if (policySource != null && !policySource.FoxgloveLog_ShouldPublish(topicIndex, nowSec))
                    return false;

                var published = targetAware
                    ? DispatchTargetAwareTopic(source, topicIndex, nowNs, "scheduled publish")
                    : DispatchTopic(source, topicIndex, nowNs, "scheduled publish", publishLive, publishBus);
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
                if (source.FoxgloveLog_GetTopic(topicIndex).Policy != FoxRunPolicy.Trigger)
                    return false;
                if (!IsRegisteredForDispatch(source, topicIndex))
                    return false;

                var targetAware = IsTargetAware(source);
                var publishLive = false;
                var publishBus = false;
                if (!targetAware
                    && !TryResolvePublishRoutes(source, topicIndex, "trigger publish", out publishLive, out publishBus))
                    return false;

                if (!CanPublishSourceTopic(source, topicIndex, "trigger publish"))
                    return false;
                if (source is IFoxglovePublishOriginSource originSource
                    && !originSource.FoxgloveLog_CanPublishOrigin(topicIndex, explicitTrigger: true))
                    return false;

                var published = targetAware
                    ? DispatchTargetAwareTopic(source, topicIndex, nowNs, "trigger publish")
                    : DispatchTopic(source, topicIndex, nowNs, "trigger publish", publishLive, publishBus);
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
            var failureIdentity =
                (ex.GetType().FullName ?? ex.GetType().Name) + ": " + ex.Message;
            var nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_warnedSourceFailures.TryGetValue(key, out var previous) &&
                !WarningDebouncer.ShouldEmitKeyedCooldown(
                    failureIdentity,
                    previous.FailureIdentity,
                    previous.WarningTicks,
                    nowTicks,
                    SourceFailureWarningIntervalTicks))
                return;

            _warnedSourceFailures[key] =
                new SourceFailureWarningState(failureIdentity, nowTicks);
            var sourceName = sourceType?.FullName ?? "<null>";
            Debug.LogWarning($"[FoxRun] {operation} failed for {sourceName}[{topicIndex}]: {ex.Message}");
        }

        private bool TryResolvePublishRoutes(
            IFoxgloveLogSource source,
            int topicIndex,
            string operation,
            out bool publishLive,
            out bool publishBus)
        {
            publishLive = false;
            publishBus = false;
            if (!TryResolvePublishEndpointConstraint(source, topicIndex, operation))
                return false;

            // Replay must not emit a second real-time external stream. Native
            // custom output is independent of WebSocket availability, but not
            // of the replay-output suppression boundary.
            var suppressExternalOutputForReplay = _mgr != null
                                                  && _mgr.SuppressLivePublishersForReplay;
            publishLive = _mgr != null
                          && _mgr.IsRunning
                          && !suppressExternalOutputForReplay;
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

        private bool TryResolvePublishEndpointConstraint(
            IFoxgloveLogSource source,
            int topicIndex,
            string operation)
        {
            var info = source.FoxgloveLog_GetTopic(topicIndex);
            if (!info.HasExplicitQos)
                return true;

            var subscriptionPolicy = _mgr?.ActiveFoxRunSubscriptionSessionPolicy;
            var defaultSource = subscriptionPolicy != null
                                && subscriptionPolicy.SubscriptionsEnabled
                ? subscriptionPolicy.DefaultSource
                : FoxRunEndpoint.Foxglove;
            var resolution = FoxRunEndpointResolver.Resolve(
                info.Flow,
                info.DeclaredSource,
                info.HasExplicitSource,
                info.DeclaredTargets,
                info.HasExplicitTargets,
                declaredEncoding: 0,
                hasExplicitEncoding: false,
                defaultSource,
                defaultTargets: _mgr != null
                    ? _mgr.ActiveFoxRunPublishTargets
                    : FoxRunEndpoint.Foxglove,
                publishDefaultEncoding: FoxRunEncoding.JSON,
                subscribeDefaultEncoding: FoxRunEncoding.JSON,
                hasExplicitQos: true);
            if (resolution.Success)
                return true;

            LogSourceFailure(
                source,
                topicIndex,
                operation + " endpoint resolution",
                new InvalidOperationException(resolution.DiagnosticMessage));
            return false;
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

        private static bool IsTargetAware(IFoxgloveLogSource source)
            => source is IFoxglovePublishCaptureSource
               && source is IFoxglovePublishTargetSource;

        private bool DispatchTargetAwareTopic(
            IFoxgloveLogSource source,
            int topicIndex,
            ulong nowNs,
            string operation)
        {
            if (!(source is IFoxglovePublishCaptureSource captureSource)
                || !(source is IFoxglovePublishTargetSource targetSource)
                || !TryGetResolvedPublishContract(
                    source,
                    topicIndex,
                    operation,
                    out var contract))
            {
                return false;
            }

            var captureAttempted = false;
            var captureStarted = false;
            var observerSource = source as IFoxgloveTopicObserverSource;
            var observerReady = false;
            if (observerSource != null
                && _mgr != null
                && !_mgr.SuppressLivePublishersForReplay)
            {
                try
                {
                    observerReady = observerSource.FoxgloveLog_HasObservers(
                        topicIndex,
                        _topicBus);
                }
                catch (Exception ex) when (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(
                        source,
                        topicIndex,
                        operation + " observer demand",
                        ex);
                }
            }

            var recordingSource = source as IFoxglovePublishRecordingSource;
            var recordingReady = false;
            if (recordingSource != null && !contract.Selects(FoxRunEndpoint.Foxglove))
            {
                try
                {
                    recordingReady = recordingSource.FoxgloveLog_IsRecordingReady(
                        topicIndex,
                        contract,
                        _mgr,
                        out var recordingReason);
                    if (!recordingReady && !string.IsNullOrWhiteSpace(recordingReason))
                    {
                        LogSourceFailure(
                            source,
                            topicIndex,
                            operation + " MCAP readiness",
                            new InvalidOperationException(recordingReason));
                    }
                }
                catch (Exception ex) when (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(source, topicIndex, operation + " MCAP readiness", ex);
                }
            }

            var recorded = false;
            var result = default(FoxRunPublishDispatchResult);
            ExceptionDispatchInfo fatal = null;
            try
            {
                try
                {
                    result = FoxRunPublishFanout.Dispatch(
                        contract,
                        nowNs,
                        capture: () =>
                        {
                            captureAttempted = true;
                            if (!captureSource.FoxgloveLog_BeginCapture(topicIndex))
                                throw new InvalidOperationException("Generated source rejected a nested publish capture.");
                            captureStarted = true;
                            return true;
                        },
                        isReady: target =>
                        {
                            // Readiness is an observable target state, not an
                            // exception. Startup, shutdown, and deliberate
                            // degraded routes must remain fail-closed without
                            // turning every unavailable poll into a warning.
                            // Actual readiness exceptions are still reported
                            // by FoxRunPublishFanout through onTargetFault.
                            return targetSource.FoxgloveLog_IsTargetReady(
                                topicIndex,
                                target,
                                contract,
                                _mgr,
                                _topicBus,
                                _sinkRouter,
                                out _);
                        },
                        publish: (target, _, timestamp) =>
                        {
                            var published = targetSource.FoxgloveLog_PublishCaptured(
                                topicIndex,
                                target,
                                contract,
                                _mgr,
                                _topicBus,
                                _sinkRouter,
                                timestamp,
                                out var reason);
                            if (!published && !string.IsNullOrWhiteSpace(reason))
                            {
                                LogSourceFailure(
                                    source,
                                    topicIndex,
                                    operation + " " + target + " publish",
                                    new InvalidOperationException(reason));
                            }
                            return published;
                        },
                        onTargetFault: (target, targetOperation, exception) =>
                            LogSourceFailure(
                                source,
                                topicIndex,
                                operation + " " + target + " " + targetOperation,
                                exception));
                }
                catch (Exception ex) when (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(source, topicIndex, operation + " capture", ex);
                    result = new FoxRunPublishDispatchResult(
                        FoxRunPublishTargetStatus.Unavailable,
                        0,
                        contract.Targets);
                }

                if (recordingReady)
                {
                    try
                    {
                        if (!captureAttempted)
                        {
                            captureAttempted = true;
                            if (!captureSource.FoxgloveLog_BeginCapture(topicIndex))
                                throw new InvalidOperationException("Generated source rejected a nested MCAP capture.");
                            captureStarted = true;
                        }

                        if (captureStarted)
                        {
                            recorded = recordingSource.FoxgloveLog_RecordCaptured(
                                topicIndex,
                                contract,
                                _mgr,
                                nowNs,
                                out var recordingReason);
                            if (!recorded && !string.IsNullOrWhiteSpace(recordingReason))
                            {
                                LogSourceFailure(
                                    source,
                                    topicIndex,
                                    operation + " MCAP publish",
                                    new InvalidOperationException(recordingReason));
                            }
                        }
                    }
                    catch (Exception ex) when (IsRecoverableSourceException(ex))
                    {
                        LogSourceFailure(source, topicIndex, operation + " MCAP publish", ex);
                    }
                }

                // Ordinary observers run only after every selected transport
                // and MCAP have synchronously consumed the frozen capture.
                // User callbacks may mutate reference-typed DTOs and must not
                // alter any external representation of this logical sample.
                if (observerReady)
                {
                    try
                    {
                        if (!captureAttempted)
                        {
                            captureAttempted = true;
                            if (!captureSource.FoxgloveLog_BeginCapture(topicIndex))
                            {
                                throw new InvalidOperationException(
                                    "Generated source rejected a nested observer capture.");
                            }
                            captureStarted = true;
                        }

                        if (captureStarted)
                        {
                            observerSource.FoxgloveLog_PublishCapturedToObservers(
                                topicIndex,
                                _topicBus,
                                nowNs);
                        }
                    }
                    catch (Exception ex) when (IsRecoverableSourceException(ex))
                    {
                        LogSourceFailure(
                            source,
                            topicIndex,
                            operation + " observer side-channel",
                            ex);
                    }
                }

                // Additive byte sinks synchronously consume the same frozen
                // payload after transports, MCAP, and ordinary observers, but
                // before EndCapture releases the generated payload cache.
                if (captureStarted)
                    PublishTopicSinkSideChannel(source, topicIndex, nowNs, operation);
            }
            catch (Exception ex)
            {
                fatal = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                if (captureStarted)
                {
                    try
                    {
                        captureSource.FoxgloveLog_EndCapture(topicIndex);
                    }
                    catch (Exception ex) when (IsRecoverableSourceException(ex))
                    {
                        LogSourceFailure(
                            source,
                            topicIndex,
                            operation + " capture cleanup",
                            ex);
                    }
                    catch (Exception ex)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(ex);
                    }
                }
            }

            var statusKey = new SourceTopicKey(source, topicIndex);
            if (fatal != null)
            {
                _publishTargetStatuses.Remove(statusKey);
                fatal.Throw();
            }
            _publishTargetStatuses[statusKey] = result;
            // Recording is an additive hidden sink, not evidence that a
            // selected live target accepted the sample.  In particular,
            // Change policy must retain its dirty sample when every selected
            // live target fails so target recovery can retry it.  Keep the
            // zero-target branch explicit for a recording-only contract seam
            // without ever projecting that success into the live status.
            return contract.Targets != 0
                ? result.Published
                : recorded;
        }

        private bool TryGetResolvedPublishContract(
            IFoxgloveLogSource source,
            int topicIndex,
            string operation,
            out FoxRunResolvedPublishContract contract)
        {
            contract = null;
            if (_timers.TryGetValue(source, out var state)
                && topicIndex >= 0
                && topicIndex < state.Contracts.Length)
            {
                contract = state.Contracts[topicIndex];
                if (contract != null)
                    return true;
            }

            if (TryResolvePublishContract(
                    source,
                    source.FoxgloveLog_GetTopic(topicIndex),
                    out contract,
                    out var diagnostic))
            {
                return true;
            }

            LogSourceFailure(
                source,
                topicIndex,
                operation + " contract resolution",
                new InvalidOperationException(diagnostic));
            return false;
        }

        private bool IsRegisteredForDispatch(
            IFoxgloveLogSource source,
            int topicIndex)
        {
            if (!_timers.TryGetValue(source, out var state)
                || topicIndex < 0
                || topicIndex >= state.Contracts.Length
                || !state.TopicRegistrationsAccepted[topicIndex]
                || state.Contracts[topicIndex] == null)
            {
                return false;
            }

            var externalTargets = state.Contracts[topicIndex].Targets
                                  & (FoxRunEndpoint.Ros2Native
                                     | FoxRunEndpoint.Ros2Bridge);
            return externalTargets == 0
                   || state.SinkRegistrationsAccepted[topicIndex];
        }

        private bool TryResolvePublishContract(
            IFoxgloveLogSource source,
            FoxgloveLogTopicInfo info,
            out FoxRunResolvedPublishContract contract,
            out string diagnostic)
        {
            var subscriptionPolicy = _mgr?.ActiveFoxRunSubscriptionSessionPolicy;
            var defaultSource = subscriptionPolicy != null
                                && subscriptionPolicy.SubscriptionsEnabled
                ? subscriptionPolicy.DefaultSource
                : _mgr != null
                    ? _mgr.ActiveFoxRunSubscriptionSource
                    : FoxRunEndpoint.Foxglove;
            var subscribeEncoding = subscriptionPolicy != null
                                    && subscriptionPolicy.SubscriptionsEnabled
                ? subscriptionPolicy.FoxgloveEncoding
                : _mgr != null
                    ? _mgr.ActiveFoxRunSubscriptionEncoding
                    : FoxRunEncoding.JSON;

            return FoxRunResolvedPublishContract.TryResolveForDeclaringType(
                info,
                source?.GetType(),
                _mgr != null
                    ? _mgr.ActiveFoxRunPublishTargets
                    : FoxRunEndpoint.Foxglove,
                _mgr != null
                    ? _mgr.ActiveFoxRunPublishEncoding
                    : FoxRunEncoding.JSON,
                _mgr != null
                    ? _mgr.ActiveFoxRunNativePublishQos
                    : FoxRunResolvedQos.Default,
                _mgr != null
                    ? _mgr.ActiveFoxRunBridgePublishQos
                    : FoxRunResolvedQos.Default,
                defaultSource,
                subscribeEncoding,
                out contract,
                out diagnostic);
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

                var contracts = new FoxRunResolvedPublishContract[count];
                for (var i = 0; i < count; i++)
                {
                    if (!TryResolvePublishContract(source, topics[i], out contracts[i], out var diagnostic))
                    {
                        LogSourceFailure(
                            source,
                            i,
                            "session contract resolution",
                            new InvalidOperationException(diagnostic));
                    }
                }

                var state = new FoxgloveLogSourceState(
                    new FixedRatePublishState[count],
                    topics,
                    contracts);
                _timers[source] = state;
                _sourceRegistrationOrder.Add(source);
                try
                {
                    RegisterSourceContracts(source, count);
                    return true;
                }
                catch (Exception ex)
                {
                    var primary = ExceptionDispatchInfo.Capture(ex);
                    try
                    {
                        UnregisterSourceContracts(source, count);
                    }
                    catch
                    {
                        // A later cleanup failure cannot replace the fatal
                        // admission failure that initiated whole-source rollback.
                    }
                    finally
                    {
                        _timers.Remove(source);
                        _sourceRegistrationOrder.Remove(source);
                        for (var index = 0; index < count; index++)
                        {
                            _publishTargetStatuses.Remove(
                                new SourceTopicKey(source, index));
                        }
                    }

                    primary.Throw();
                    throw;
                }
            }

            return false;
        }

        private void RegisterSourceContracts(IFoxgloveLogSource source, int count)
        {
            var contractSource = source as IFoxgloveTopicContractSource;
            var origin = contractSource?.FoxgloveLog_Origin ?? source.GetType().FullName ?? string.Empty;
            if (!_timers.TryGetValue(source, out var state))
                return;
            for (var i = 0; i < count; i++)
            {
                if (i >= state.Contracts.Length || state.Contracts[i] == null)
                {
                    ClearRegistrationState(source, state, i);
                    continue;
                }

                TryRegisterSourceContract(
                    source,
                    state,
                    i,
                    contractSource,
                    origin,
                    "topic contract registration");
            }
        }

        private bool TryRegisterSourceContract(
            IFoxgloveLogSource source,
            FoxgloveLogSourceState state,
            int topicIndex,
            IFoxgloveTopicContractSource contractSource,
            string origin,
            string operation)
        {
            if (topicIndex < 0
                || topicIndex >= state.Contracts.Length
                || state.Contracts[topicIndex] == null)
            {
                ClearRegistrationState(source, state, topicIndex);
                return false;
            }

            var busAccepted = false;
            var sinkRegistrationAttempted = false;
            FoxTopicContract contract = null;
            try
            {
                contract = contractSource != null
                    ? contractSource.FoxgloveLog_GetContract(topicIndex)
                    : FallbackContract(state.Topics[topicIndex]);
                if (contract == null)
                    return false;

                var result = _topicBus.Register(contract, origin);
                if (!result.Accepted)
                {
                    ClearRegistrationState(source, state, topicIndex);
                    LogSourceFailure(
                        source,
                        topicIndex,
                        operation,
                        new InvalidOperationException(result.Diagnostic));
                    return false;
                }

                busAccepted = true;
                state.RegisteredTopicContracts[topicIndex] = contract;
                var externalTargets = ExternalTargets(state.Contracts[topicIndex]);
                var sinkAccepted = externalTargets == 0;
                if (!sinkAccepted)
                {
                    sinkRegistrationAttempted = true;
                    sinkAccepted = _sinkRouter.RegisterTargets(
                        state.Contracts[topicIndex],
                        contract);
                }

                if (!sinkAccepted)
                {
                    _topicBus.Unregister(contract.Topic, origin);
                    ClearRegistrationState(source, state, topicIndex);
                    LogSourceFailure(
                        source,
                        topicIndex,
                        operation + " sink",
                        new InvalidOperationException(
                            "Topic '" + contract.Topic
                            + "' conflicts with an existing resolved target or QoS contract."));
                    return false;
                }

                state.TopicRegistrationsAccepted[topicIndex] = true;
                state.SinkRegistrationsAccepted[topicIndex] = true;
                return true;
            }
            catch (Exception ex)
            {
                var primary = ExceptionDispatchInfo.Capture(ex);
                try
                {
                    if (sinkRegistrationAttempted && contract != null)
                        TryRollbackSinkRoute(contract.Topic);
                }
                catch
                {
                    // Rollback is mandatory but a cleanup failure cannot mask
                    // the original registration exception.
                }
                try
                {
                    if (busAccepted && contract != null)
                        _topicBus.Unregister(contract.Topic, origin);
                }
                catch
                {
                    // Preserve the primary registration failure after every
                    // mandatory cleanup step has been attempted.
                }
                finally
                {
                    ClearRegistrationState(source, state, topicIndex);
                }
                if (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(source, topicIndex, operation, ex);
                    return false;
                }

                primary.Throw();
                throw;
            }
        }

        private void SetManager(FoxgloveManager manager)
        {
            if (ReferenceEquals(_mgr, manager))
                return;

            if (_mgr != null)
            {
                _mgr.FoxRunPublishSessionChanged -= OnFoxRunPublishSessionChanged;
                _mgr.FoxRunSubscriptionSessionChanged -= OnFoxRunSubscriptionSessionChanged;
            }

            _mgr = manager;
            if (_mgr != null)
            {
                _mgr.FoxRunPublishSessionChanged += OnFoxRunPublishSessionChanged;
                _mgr.FoxRunSubscriptionSessionChanged += OnFoxRunSubscriptionSessionChanged;
            }

            RefreshResolvedPublishContracts();
            ReconcileSinkContracts();
        }

        private void OnFoxRunPublishSessionChanged(FoxRunPublishSessionPolicy policy)
        {
            _warnedSourceFailures.Clear();
            RefreshResolvedPublishContracts();
            if (policy != null && !policy.SessionActive)
            {
                try
                {
                    UnregisterSinkContracts();
                }
                finally
                {
                    _publishTargetStatuses.Clear();
                }
                return;
            }

            ReconcileSinkContracts();
        }

        private void OnFoxRunSubscriptionSessionChanged(FoxRunSubscriptionSessionPolicy policy)
        {
            _ = policy;
            RefreshResolvedPublishContracts();
            ReconcileSinkContracts();
        }

        private void RefreshResolvedPublishContracts()
        {
            ExceptionDispatchInfo fatal = null;
            foreach (var entry in _timers)
            {
                var source = entry.Key;
                var state = entry.Value;
                for (var index = 0; index < state.Topics.Length; index++)
                {
                    var resolved = TryResolvePublishContract(
                        source,
                        state.Topics[index],
                        out var nextContract,
                        out var diagnostic);
                    var previousContract = state.Contracts[index];
                    if (!resolved
                        || !ResolvedPublishContractsMatch(
                            previousContract,
                            nextContract))
                    {
                        try
                        {
                            ReleaseSourceRegistration(
                                source,
                                state,
                                index,
                                "session contract invalidation");
                        }
                        catch (Exception ex)
                        {
                            fatal ??= ExceptionDispatchInfo.Capture(ex);
                        }
                    }

                    state.Contracts[index] = resolved ? nextContract : null;
                    if (!resolved)
                    {
                        ClearRegistrationState(source, state, index);
                        LogSourceFailure(
                            source,
                            index,
                            "session contract resolution",
                            new InvalidOperationException(diagnostic));
                    }
                }
            }

            fatal?.Throw();
        }

        private void ReconcileSinkContracts()
        {
            RetryRejectedTopicRegistrations();
        }

        private void RetryRejectedTopicRegistrations()
        {
            for (var sourceIndex = 0;
                 sourceIndex < _sourceRegistrationOrder.Count;
                 sourceIndex++)
            {
                var source = _sourceRegistrationOrder[sourceIndex];
                if (!_timers.TryGetValue(source, out var state))
                    continue;
                var contractSource = source as IFoxgloveTopicContractSource;
                var origin = contractSource?.FoxgloveLog_Origin
                             ?? source.GetType().FullName
                             ?? string.Empty;
                for (var topicIndex = 0;
                     topicIndex < state.TopicRegistrationsAccepted.Length;
                     topicIndex++)
                {
                    if (topicIndex >= state.Contracts.Length
                        || state.Contracts[topicIndex] == null)
                    {
                        continue;
                    }

                    if (state.TopicRegistrationsAccepted[topicIndex]
                        && state.SinkRegistrationsAccepted[topicIndex])
                        continue;

                    TryRegisterSourceContract(
                        source,
                        state,
                        topicIndex,
                        contractSource,
                        origin,
                        "topic contract reconciliation");
                }
            }
        }

        private void UnregisterSinkContracts()
        {
            ExceptionDispatchInfo fatal = null;
            foreach (var entry in _timers)
            {
                var source = entry.Key;
                var state = entry.Value;
                var count = state.Timers.Length;
                for (var i = 0; i < count; i++)
                {
                    if (!state.SinkRegistrationsAccepted[i])
                        continue;
                    try
                    {
                        var contract = state.RegisteredTopicContracts[i];
                        if (contract != null
                            && ExternalTargets(state.Contracts[i]) != 0)
                            _sinkRouter.Unregister(contract.Topic);
                    }
                    catch (Exception ex) when (IsRecoverableSourceException(ex))
                    {
                        LogSourceFailure(source, i, "topic sink reconciliation unregister", ex);
                    }
                    catch (Exception ex)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(ex);
                    }
                    finally
                    {
                        state.SinkRegistrationsAccepted[i] = false;
                    }
                }
            }

            fatal?.Throw();
        }

        private void UnregisterSourceContracts(IFoxgloveLogSource source, int count)
        {
            if (!_timers.TryGetValue(source, out var state))
                return;
            ExceptionDispatchInfo fatal = null;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    ReleaseSourceRegistration(
                        source,
                        state,
                        i,
                        "topic contract unregister");
                }
                catch (Exception ex)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(ex);
                }
            }

            fatal?.Throw();
        }

        private void ReleaseSourceRegistration(
            IFoxgloveLogSource source,
            FoxgloveLogSourceState state,
            int topicIndex,
            string operation)
        {
            if (state == null
                || topicIndex < 0
                || topicIndex >= state.TopicRegistrationsAccepted.Length)
                return;

            var contract = state.RegisteredTopicContracts[topicIndex];
            var origin = (source as IFoxgloveTopicContractSource)?.FoxgloveLog_Origin
                         ?? source?.GetType().FullName
                         ?? string.Empty;
            ExceptionDispatchInfo fatal = null;
            if (contract != null
                && state.SinkRegistrationsAccepted[topicIndex]
                && ExternalTargets(state.Contracts[topicIndex]) != 0)
            {
                try
                {
                    _sinkRouter.Unregister(contract.Topic);
                }
                catch (Exception ex) when (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(source, topicIndex, operation + " sink", ex);
                }
                catch (Exception ex)
                {
                    fatal = ExceptionDispatchInfo.Capture(ex);
                }
            }

            if (contract != null && state.TopicRegistrationsAccepted[topicIndex])
            {
                try
                {
                    _topicBus.Unregister(contract.Topic, origin);
                }
                catch (Exception ex) when (IsRecoverableSourceException(ex))
                {
                    LogSourceFailure(source, topicIndex, operation + " bus", ex);
                }
                catch (Exception ex)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(ex);
                }
            }

            ClearRegistrationState(source, state, topicIndex);
            fatal?.Throw();
        }

        private void ClearRegistrationState(
            IFoxgloveLogSource source,
            FoxgloveLogSourceState state,
            int topicIndex)
        {
            if (state != null
                && topicIndex >= 0
                && topicIndex < state.TopicRegistrationsAccepted.Length)
            {
                state.TopicRegistrationsAccepted[topicIndex] = false;
                state.SinkRegistrationsAccepted[topicIndex] = false;
                state.RegisteredTopicContracts[topicIndex] = null;
            }

            if (source != null && topicIndex >= 0)
                _publishTargetStatuses.Remove(new SourceTopicKey(source, topicIndex));
        }

        private void TryRollbackSinkRoute(string topic)
        {
            if (!string.IsNullOrWhiteSpace(topic))
                _sinkRouter.Unregister(topic);
        }

        private static FoxRunEndpoint ExternalTargets(
            FoxRunResolvedPublishContract contract)
            => contract == null
                ? 0
                : contract.Targets
                  & (FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge);

        private static bool ResolvedPublishContractsMatch(
            FoxRunResolvedPublishContract left,
            FoxRunResolvedPublishContract right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            return left.Targets == right.Targets
                   && left.FoxgloveEncoding == right.FoxgloveEncoding
                   && left.NativeQos == right.NativeQos
                   && left.BridgeQos == right.BridgeQos;
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

            ExceptionDispatchInfo fatal = null;
            try
            {
                UnregisterSourceContracts(source, state.Timers.Length);
            }
            catch (Exception ex)
            {
                fatal = ExceptionDispatchInfo.Capture(ex);
            }

            _timers.Remove(source);
            _sourceRegistrationOrder.Remove(source);
            for (var index = 0; index < state.Timers.Length; index++)
                _publishTargetStatuses.Remove(new SourceTopicKey(source, index));

            try
            {
                RetryRejectedTopicRegistrations();
            }
            catch (Exception ex)
            {
                fatal ??= ExceptionDispatchInfo.Capture(ex);
            }

            fatal?.Throw();
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
            // Generated explicit triggers can run before the next pending
            // registration drain (for example from an early Unity callback).
            // Admit the source through the same contract/ownership gates used
            // by the normal lifecycle before allowing the trigger to dispatch.
            if (!_timers.ContainsKey(source) && !AddSource(source))
                return false;

            return TryPublishTriggeredTopic(source, topicIndex, _mgr.NowNs, Time.realtimeSinceStartupAsDouble);
        }

        private void TryRefreshManagerForTrigger()
        {
            var now = Time.realtimeSinceStartup;
            if (now < _nextTriggerManagerSearchTime)
                return;

            _nextTriggerManagerSearchTime = now + ManagerSearchIntervalSeconds;
            SetManager(FindFirstObjectByType<FoxgloveManager>());
        }

        private static bool IsRecoverableSourceException(Exception ex)
            => FoxRunExceptionPolicy.IsRecoverable(ex);

        private static void OnSinkFaulted(FoxTopicSinkFault fault)
        {
            var message = "[FoxRun] Topic sink '" + fault.SinkName + "' failed during "
                          + fault.Operation + " for topic '" + fault.Topic + "': "
                          + fault.Exception.Message;
            Debug.LogWarning(message);
        }

        private sealed class FoxgloveLogSourceState
        {
            public FoxgloveLogSourceState(
                FixedRatePublishState[] timers,
                FoxgloveLogTopicInfo[] topics,
                FoxRunResolvedPublishContract[] contracts)
            {
                Timers = timers;
                Topics = topics;
                Contracts = contracts;
                TopicRegistrationsAccepted = new bool[contracts.Length];
                SinkRegistrationsAccepted = new bool[contracts.Length];
                RegisteredTopicContracts = new FoxTopicContract[contracts.Length];
            }

            public FixedRatePublishState[] Timers { get; }
            public FoxgloveLogTopicInfo[] Topics { get; }
            public FoxRunResolvedPublishContract[] Contracts { get; }
            public bool[] TopicRegistrationsAccepted { get; }
            public bool[] SinkRegistrationsAccepted { get; }
            public FoxTopicContract[] RegisteredTopicContracts { get; }
        }

        private readonly struct SourceTopicKey : IEquatable<SourceTopicKey>
        {
            private readonly IFoxgloveLogSource _source;
            private readonly int _topicIndex;

            public SourceTopicKey(IFoxgloveLogSource source, int topicIndex)
            {
                _source = source;
                _topicIndex = topicIndex;
            }

            public bool Equals(SourceTopicKey other)
                => ReferenceEquals(_source, other._source)
                   && _topicIndex == other._topicIndex;

            public override bool Equals(object obj)
                => obj is SourceTopicKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((_source != null
                                ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_source)
                                : 0) * 397)
                           ^ _topicIndex;
                }
            }
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

        private readonly struct SourceFailureWarningState
        {
            public SourceFailureWarningState(string failureIdentity, long warningTicks)
            {
                FailureIdentity = failureIdentity ?? string.Empty;
                WarningTicks = warningTicks;
            }

            public string FailureIdentity { get; }
            public long WarningTicks { get; }
        }

        /// <summary>Clears all timers and nulls the singleton reference.</summary>
        private void OnDestroy()
        {
            if (_mgr != null)
            {
                _mgr.FoxRunPublishSessionChanged -= OnFoxRunPublishSessionChanged;
                _mgr.FoxRunSubscriptionSessionChanged -= OnFoxRunSubscriptionSessionChanged;
                _mgr = null;
            }
            _sinkRouter.SinkFaulted -= OnSinkFaulted;
            _timers.Clear();
            _sourceRegistrationOrder.Clear();
            _pendingAdds.Clear();
            _pendingRemoves.Clear();
            _pendingAddSet.Clear();
            _pendingRemoveSet.Clear();
            _warnedSourceFailures.Clear();
            _publishTargetStatuses.Clear();
            _sinkRouter.Dispose();
            if (_instance == this) _instance = null;
        }
    }
}
