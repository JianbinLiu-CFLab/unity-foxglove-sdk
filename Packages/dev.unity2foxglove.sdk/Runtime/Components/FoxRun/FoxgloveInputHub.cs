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
        private readonly List<IFoxgloveInputSource> _scanSources = new();
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

            _router.MaxPayloadBytes = _manager.FoxRunSubscriptionMaxPayloadBytes;
            _router.MaxMessagesPerSecondPerTopic = _manager.FoxRunSubscriptionMaxMessagesPerSecondPerTopic;
            _router.DefaultSubscriptionWireEncoding = _manager.ActiveFoxRunSubscriptionEncoding;

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
                _manager.OnClientMessageWithEncoding -= OnClientMessage;
            _manager = manager;
            if (_manager != null)
                _manager.OnClientMessageWithEncoding += OnClientMessage;
        }

        private void Scan()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            _scanSources.Clear();
            foreach (var behaviour in behaviours)
            {
                if (behaviour is IFoxgloveInputSource source)
                    _scanSources.Add(source);
            }

            _scanSources.Sort(CompareInputSourceOrder);
            foreach (var source in _scanSources)
            {
                if (_sources.Add(source))
                    _router.Register(source);
            }
        }

        private static int CompareInputSourceOrder(IFoxgloveInputSource left, IFoxgloveInputSource right)
        {
            var leftBehaviour = left as MonoBehaviour;
            var rightBehaviour = right as MonoBehaviour;
            var typeOrder = string.CompareOrdinal(
                leftBehaviour != null ? leftBehaviour.GetType().FullName : string.Empty,
                rightBehaviour != null ? rightBehaviour.GetType().FullName : string.Empty);
            if (typeOrder != 0)
                return typeOrder;
            return (leftBehaviour != null ? leftBehaviour.GetInstanceID() : 0)
                .CompareTo(rightBehaviour != null ? rightBehaviour.GetInstanceID() : 0);
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
            _scanSources.Clear();
        }

        private void OnClientMessage(uint clientId, uint channelId, string topic, string encoding, byte[] payload)
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
                encoding,
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
