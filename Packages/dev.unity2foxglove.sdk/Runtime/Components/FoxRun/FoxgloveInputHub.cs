// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Main-thread lifecycle and dispatch for generated FoxRun inputs.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    [AddComponentMenu("")]
    public sealed class FoxgloveInputHub : MonoBehaviour
    {
        private const float ManagerSearchIntervalSeconds = 3f;
        private const float ScanIntervalSeconds = 2f;

        private static FoxgloveInputHub _instance;
        private FoxgloveManager _manager;
        private readonly FoxRunInputRouter _router = new();
        private readonly HashSet<IFoxgloveInputSource> _sources = new();
        private readonly List<IFoxgloveInputSource> _stale = new();
        private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
        private float _managerSearchCooldown;
        private float _scanTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_instance != null)
                return;
            var existing = FindFirstObjectByType<FoxgloveInputHub>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            var go = new GameObject("[FoxRunInputHub]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<FoxgloveInputHub>();
        }

        private void Awake()
        {
            if (_instance == null)
                _instance = this;
        }

        private void Update()
        {
            ResolveManager();
            if (_manager == null)
                return;

            _router.MaxPayloadBytes = _manager.FoxRunInboundMaxPayloadBytes;
            _router.MaxMessagesPerSecondPerTopic = _manager.FoxRunInboundMaxMessagesPerSecondPerTopic;

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanIntervalSeconds;
                Scan();
                RemoveStaleSources();
            }
        }

        private void ResolveManager()
        {
            if (_manager != null)
                return;
            _managerSearchCooldown -= Time.deltaTime;
            if (_managerSearchCooldown > 0f)
                return;
            _managerSearchCooldown = ManagerSearchIntervalSeconds;
            SetManager(FindFirstObjectByType<FoxgloveManager>());
        }

        private void SetManager(FoxgloveManager manager)
        {
            if (_manager == manager)
                return;
            if (_manager != null)
                _manager.OnClientMessage -= OnClientMessage;
            _manager = manager;
            if (_manager != null)
                _manager.OnClientMessage += OnClientMessage;
        }

        private void Scan()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Array.Sort(behaviours, CompareInputSourceOrder);
            foreach (var behaviour in behaviours)
            {
                if (behaviour is IFoxgloveInputSource source && _sources.Add(source))
                    _router.Register(source);
            }
        }

        private static int CompareInputSourceOrder(MonoBehaviour left, MonoBehaviour right)
        {
            var typeOrder = string.CompareOrdinal(
                left != null ? left.GetType().FullName : string.Empty,
                right != null ? right.GetType().FullName : string.Empty);
            if (typeOrder != 0)
                return typeOrder;
            return (left != null ? left.GetInstanceID() : 0)
                .CompareTo(right != null ? right.GetInstanceID() : 0);
        }

        private void RemoveStaleSources()
        {
            _stale.Clear();
            foreach (var source in _sources)
            {
                if (source is MonoBehaviour behaviour
                    && (behaviour == null || !behaviour.isActiveAndEnabled))
                {
                    _stale.Add(source);
                }
            }
            foreach (var source in _stale)
            {
                _router.Unregister(source);
                _sources.Remove(source);
            }
        }

        private void OnClientMessage(uint clientId, uint channelId, string topic, byte[] payload)
        {
            if (_manager == null || !_manager.EnableFoxRunInbound)
                return;
            if (!_manager.IsFoxRunInboundAuthorized)
            {
                WarnOnce(_manager.FoxRunInboundAuthorizationDiagnostic);
                return;
            }

            var result = _router.Dispatch(
                topic,
                payload,
                "json",
                Time.realtimeSinceStartupAsDouble);
            if (result.Status != FoxRunInputDispatchStatus.Applied
                && result.Status != FoxRunInputDispatchStatus.UnknownTopic)
            {
                WarnOnce(topic + ": " + result.Diagnostic);
            }
        }

        private void WarnOnce(string message)
        {
            if (!string.IsNullOrEmpty(message) && _warned.Add(message))
                Debug.LogWarning("[FoxRun] " + message);
        }

        private void OnDisable()
        {
            SetManager(null);
        }

        private void OnDestroy()
        {
            SetManager(null);
            foreach (var source in _sources)
                _router.Unregister(source);
            _sources.Clear();
            if (_instance == this)
                _instance = null;
        }
    }
}
