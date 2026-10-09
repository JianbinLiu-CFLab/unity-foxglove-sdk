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

    }
}
