// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxService
// Purpose: Runtime hub for generated declarative Foxglove service registration.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Registers generated <c>[FoxService]</c> service sources with a
    /// <see cref="FoxgloveManager"/> while their components are enabled.
    /// </summary>
    [AddComponentMenu("")]
    public sealed partial class FoxgloveServiceHub : MonoBehaviour
    {
        private const float ManagerSearchIntervalSeconds = 3f;
        private const float ScanIntervalSeconds = 2f;

        private static FoxgloveServiceHub _instance;
        private static readonly object PendingGate = new();
        private static readonly List<IFoxgloveServiceSource> PendingRegistrations = new();
        private static readonly HashSet<IFoxgloveServiceSource> PendingRegistrationSet = new();

        [SerializeField] private FoxgloveManager _manager;
        [SerializeField] private bool _enableFallbackSceneScan = true;

        private float _managerSearchCooldown;
        private float _scanTimer;
        private bool _fallbackSceneScanDirty = true;
        private bool _managerWasRunning;

        /// <summary>Registers a generated service source without waiting for fallback scene scan.</summary>
        public static void RegisterSource(IFoxgloveServiceSource source)
        {
            if (SourceUnavailable(source))
                return;

            lock (PendingGate)
            {
                if (PendingRegistrationSet.Add(source))
                    PendingRegistrations.Add(source);
            }
        }

        /// <summary>Unregisters a generated service source from the active hub.</summary>
        public static void UnregisterSource(IFoxgloveServiceSource source)
        {
            if (source == null)
                return;

            lock (PendingGate)
            {
                PendingRegistrationSet.Remove(source);
                PendingRegistrations.Remove(source);
            }

            if (_instance == null)
                return;

            _instance.RemoveTemporarilyUnavailableSource(source);
            _instance.UnregisterSourceNow(source);
        }

        public static bool TryGetActive(out FoxgloveServiceHub hub)
        {
            hub = _instance;
            return hub != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            lock (PendingGate)
            {
                PendingRegistrations.Clear();
                PendingRegistrationSet.Clear();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_instance != null)
                return;

            var existing = FindFirstObjectByType<FoxgloveServiceHub>();
            if (existing != null)
            {
                _instance = existing;
                _instance.DrainPendingRegistrations();
                return;
            }

            var go = new GameObject("[FoxServiceHub]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<FoxgloveServiceHub>();
            _instance.DrainPendingRegistrations();
        }

        private void Awake()
        {
            if (_instance == null)
                _instance = this;
        }

        private void OnEnable()
        {
            MarkFallbackSceneScanDirty();
            SceneManager.sceneLoaded += OnSceneChanged;
            SceneManager.sceneUnloaded += OnSceneChanged;
        }

        private void Update()
        {
            ResolveManagerIfNeeded();
            if (_manager == null)
            {
                if (_managerWasRunning)
                    SuspendRegistrationsForRestart();
                _managerWasRunning = false;
                return;
            }

            DrainPendingRegistrations();
            RemoveDisabledOrDestroyedSources();

            if (!_manager.IsRunning)
            {
                if (_managerWasRunning)
                    SuspendRegistrationsForRestart();
                _managerWasRunning = false;
                return;
            }

            _managerWasRunning = true;
            ReregisterReenabledSources();

            if (_enableFallbackSceneScan)
            {
                _scanTimer -= Time.deltaTime;
                if (_fallbackSceneScanDirty && _scanTimer <= 0f)
                {
                    _scanTimer = ScanIntervalSeconds;
                    _fallbackSceneScanDirty = false;
                    Scan();
                }
            }
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneChanged;
            SceneManager.sceneUnloaded -= OnSceneChanged;
            SuspendRegistrationsForRestart();
        }

        private void OnDestroy()
        {
            UnregisterAll();
            if (_instance == this)
                _instance = null;
        }

        private void ResolveManagerIfNeeded()
        {
            if (_manager != null)
                return;

            _managerSearchCooldown -= Time.deltaTime;
            if (_managerSearchCooldown > 0f)
                return;

            _managerSearchCooldown = ManagerSearchIntervalSeconds;
            _manager = FindFirstObjectByType<FoxgloveManager>();
        }

        private void Scan()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var behaviour in behaviours)
            {
                if (behaviour is IFoxgloveServiceSource source)
                {
                    if (!RegisterSourceNow(source))
                        TrackTemporarilyUnavailableSource(source);
                }
            }
        }

        private void OnSceneChanged(Scene scene, LoadSceneMode mode)
        {
            MarkFallbackSceneScanDirty();
        }

        private void OnSceneChanged(Scene scene)
        {
            MarkFallbackSceneScanDirty();
        }

        private void MarkFallbackSceneScanDirty()
        {
            _fallbackSceneScanDirty = true;
            _scanTimer = 0f;
        }

    }
}
