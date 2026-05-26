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
        /// <summary>Cached reference to the FoxgloveManager.</summary>
        private FoxgloveManager _mgr;
        [SerializeField] private bool _enableFallbackSceneScan = true;
        /// <summary>Per-source scheduler state for rate throttling.</summary>
        private readonly Dictionary<IFoxgloveLogSource, FixedRatePublishState[]> _timers = new();
        /// <summary>List of destroyed sources to clean up this frame.</summary>
        private readonly List<IFoxgloveLogSource> _stale = new();
        private readonly List<IFoxgloveLogSource> _pendingAdds = new();
        private readonly List<IFoxgloveLogSource> _pendingRemoves = new();
        private readonly HashSet<string> _warnedSourceFailures = new();
        private bool _iteratingTimers;
        /// <summary>Countdown until the next Scan for new sources.</summary>
        private float _scanTimer;
        /// <summary>Cooldown between FoxgloveManager search attempts.</summary>
        private float _mgrSearchCooldown;
        /// <summary>Cooldown between fallback FoxgloveManager search attempts.</summary>
        private const float ManagerSearchIntervalSeconds = 3f;
        /// <summary>Fallback scene scan interval used when generated sources did not self-register.</summary>
        private const float ScanIntervalSeconds = 2f;

        /// <summary>Register a generated FoxRun source without waiting for the fallback scene scan.</summary>
        public static void RegisterSource(IFoxgloveLogSource source)
        {
            if (source == null)
                return;

            lock (PendingRegistrationsGate)
            {
                if (!PendingRegistrations.Contains(source))
                    PendingRegistrations.Add(source);
            }
        }

        /// <summary>Unregister a generated FoxRun source from the hub cache.</summary>
        public static void UnregisterSource(IFoxgloveLogSource source)
        {
            if (source == null)
                return;

            lock (PendingRegistrationsGate)
            {
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

        /// <summary>Reset static state when Unity enters Play Mode without domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            lock (PendingRegistrationsGate)
            {
                PendingRegistrations.Clear();
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
            if (_instance != null) return;

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
                    return;
                }
            }

            var go = new GameObject("[FoxRunHub]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<FoxgloveLogHub>();
            _instance.DrainPendingRegistrations();
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
            if (!_mgr.IsRunning) return;
            if (_mgr.SuppressLivePublishersForReplay) return;

            if (_enableFallbackSceneScan)
            {
                _scanTimer -= Time.deltaTime;
                if (_scanTimer <= 0f)
                {
                    _scanTimer = ScanIntervalSeconds;
                    Scan();
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
                    if (kv.Key is MonoBehaviour mb && mb == null) { _stale.Add(kv.Key); continue; }
                    if (kv.Key is MonoBehaviour mb2 && !mb2.isActiveAndEnabled) continue;
                    var t = kv.Value;
                    for (int i = 0; i < t.Length; i++)
                        TryPublishScheduledTopic(kv.Key, i, ref t[i], nowNs, nowSec);
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
            int topicIndex,
            ref FixedRatePublishState timer,
            ulong nowNs,
            double nowSec)
        {
            try
            {
                var info = source.FoxgloveLog_GetTopic(topicIndex);
                if (info.PublishMode == FoxRunPublishMode.OnTrigger)
                    return false;

                var rateHz = info.RateHz;
                if (!FixedRatePublishScheduler.ShouldPublish(
                        nowSec,
                        rateHz,
                        ref timer,
                        nonPositivePublishesEveryFrame: false))
                    return false;

                var policySource = source as IFoxgloveLogPolicySource;
                if (policySource != null && !policySource.FoxgloveLog_ShouldPublish(topicIndex, nowSec))
                    return false;

                source.FoxgloveLog_Publish(topicIndex, _mgr, nowNs);
                policySource?.FoxgloveLog_MarkPublished(topicIndex, nowSec);
                return true;
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
                source.FoxgloveLog_Publish(topicIndex, _mgr, nowNs);
                if (source is IFoxgloveLogPolicySource policySource)
                    policySource.FoxgloveLog_MarkPublished(topicIndex, nowSec);
                return true;
            }
            catch (Exception ex) when (IsRecoverableSourceException(ex))
            {
                LogSourceFailure(source, topicIndex, "trigger publish", ex);
                return false;
            }
        }

        private void LogSourceFailure(IFoxgloveLogSource source, int topicIndex, string operation, Exception ex)
        {
            var sourceName = source?.GetType().FullName ?? "<null>";
            var key = sourceName + ":" + topicIndex + ":" + operation;
            if (_warnedSourceFailures.Add(key))
                Debug.LogWarning($"[FoxRun] {operation} failed for {sourceName}[{topicIndex}]: {ex.Message}");
        }

        /// <summary>
        /// Finds every active MonoBehaviour implementing <see cref="IFoxgloveLogSource"/>
        /// and registers new sources in the timer dictionary.
        /// Runs on a 2-second interval.
        /// </summary>
        private void Scan()
        {
            var all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var mb in all)
            {
                if (mb is IFoxgloveLogSource src)
                    AddSource(src);
            }
        }

        private void AddSource(IFoxgloveLogSource source)
        {
            if (_iteratingTimers)
            {
                if (!_pendingAdds.Contains(source))
                    _pendingAdds.Add(source);
                return;
            }

            AddSourceNow(source);
        }

        private void AddSourceNow(IFoxgloveLogSource source)
        {
            if (source == null || _timers.ContainsKey(source))
                return;
            var count = source.FoxgloveLog_TopicCount;
            if (count > 0)
                _timers[source] = new FixedRatePublishState[count];
        }

        private void RemoveSource(IFoxgloveLogSource source)
        {
            if (source == null)
                return;
            if (_iteratingTimers)
            {
                if (!_pendingRemoves.Contains(source))
                    _pendingRemoves.Add(source);
                return;
            }

            _timers.Remove(source);
        }

        private void ApplyPendingTimerMutations()
        {
            if (_pendingRemoves.Count > 0)
            {
                foreach (var source in _pendingRemoves)
                    _timers.Remove(source);
                _pendingRemoves.Clear();
            }

            if (_pendingAdds.Count > 0)
            {
                foreach (var source in _pendingAdds)
                    AddSourceNow(source);
                _pendingAdds.Clear();
            }
        }

        private void DrainPendingRegistrations()
        {
            IFoxgloveLogSource[] pending;
            lock (PendingRegistrationsGate)
            {
                if (PendingRegistrations.Count == 0)
                    return;

                pending = PendingRegistrations.ToArray();
                PendingRegistrations.Clear();
            }

            foreach (var source in pending)
                AddSource(source);
        }

        private bool TriggerSource(IFoxgloveLogSource source, int topicIndex)
        {
            if (source == null)
                return false;
            if (topicIndex < 0 || topicIndex >= source.FoxgloveLog_TopicCount)
                return false;
            if (_mgr == null)
                _mgr = FindFirstObjectByType<FoxgloveManager>();
            if (_mgr == null || !_mgr.IsRunning)
                return false;
            if (_mgr.SuppressLivePublishersForReplay)
                return false;

            return TryPublishTriggeredTopic(source, topicIndex, _mgr.NowNs, Time.realtimeSinceStartupAsDouble);
        }

        private static bool IsRecoverableSourceException(Exception ex)
        {
            return !(ex is OutOfMemoryException)
                   && !(ex is StackOverflowException)
                   && !(ex is AccessViolationException)
                   && !(ex is AppDomainUnloadedException);
        }

        /// <summary>Clears all timers and nulls the singleton reference.</summary>
        private void OnDestroy()
        {
            _timers.Clear();
            if (_instance == this) _instance = null;
        }
    }
}
