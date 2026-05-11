// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Unity
// Purpose: Runtime hub for [FoxRun] attribute-based auto-publishing.
// FoxRunCodeGenerator produces the IFoxgloveLogSource implementations;
// this hub acts as their registry, relaying value updates to Foxglove
// topics through FoxgloveManager.

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
        /// <summary>Cached reference to the FoxgloveManager.</summary>
        private FoxgloveManager _mgr;
        /// <summary>Per-source countdown timers for rate throttling.</summary>
        private readonly Dictionary<IFoxgloveLogSource, float[]> _timers = new();
        /// <summary>List of destroyed sources to clean up this frame.</summary>
        private readonly List<IFoxgloveLogSource> _stale = new();
        /// <summary>Countdown until the next Scan for new sources.</summary>
        private float _scanTimer;
        /// <summary>Cooldown between FoxgloveManager search attempts.</summary>
        private float _mgrSearchCooldown;

        /// <summary>Reset static state when Unity enters Play Mode without domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
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
                    return;
                }
            }

            var go = new GameObject("[FoxRunHub]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<FoxgloveLogHub>();
        }

        /// <summary>
        /// Each frame: resolve the FoxgloveManager (with a 3-second retry cooldown),
        /// periodically scan for new log sources, and fire publishes for every source
        /// whose per-topic countdown timer has elapsed. Event-driven sources can
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
                    _mgrSearchCooldown = 3f;
                    _mgr = FindFirstObjectByType<FoxgloveManager>();
                }
                if (_mgr == null) return;
            }
            if (!_mgr.IsRunning) return;
            if (_mgr.SuppressLivePublishersForReplay) return;

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = 2f;
                Scan();
            }

            var nowNs = _mgr.NowNs;
            var dt = Time.deltaTime;

            var nowSec = (double)Time.realtimeSinceStartup;
            _stale.Clear();
            foreach (var kv in _timers)
            {
                if (kv.Key is MonoBehaviour mb && mb == null) { _stale.Add(kv.Key); continue; }
                if (kv.Key is MonoBehaviour mb2 && !mb2.isActiveAndEnabled) continue;
                var t = kv.Value;
                var policySource = kv.Key as IFoxgloveLogPolicySource;
                for (int i = 0; i < t.Length; i++)
                {
                    t[i] -= dt;
                    if (t[i] <= 0f)
                    {
                        var info = kv.Key.FoxgloveLog_GetTopic(i);
                        t[i] = info.RateHz > 0 ? 1f / info.RateHz : 1f;
                        bool shouldPublish = policySource == null
                            || policySource.FoxgloveLog_ShouldPublish(i, nowSec);
                        if (shouldPublish)
                        {
                            kv.Key.FoxgloveLog_Publish(i, _mgr, nowNs);
                            policySource?.FoxgloveLog_MarkPublished(i, nowSec);
                        }
                    }
                }
            }
            foreach (var s in _stale) _timers.Remove(s);
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
                if (mb is IFoxgloveLogSource src && !_timers.ContainsKey(src))
                {
                    var count = src.FoxgloveLog_TopicCount;
                    if (count > 0)
                        _timers[src] = new float[count];
                }
            }
        }

        /// <summary>Clears all timers and nulls the singleton reference.</summary>
        private void OnDestroy()
        {
            _timers.Clear();
            if (_instance == this) _instance = null;
        }
    }
}
