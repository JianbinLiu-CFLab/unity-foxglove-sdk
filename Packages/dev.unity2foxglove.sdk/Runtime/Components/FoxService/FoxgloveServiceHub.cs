// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxService
// Purpose: Runtime hub for generated declarative Foxglove service registration.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Protocol;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Registers generated <c>[FoxService]</c> service sources with a
    /// <see cref="FoxgloveManager"/> while their components are enabled.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class FoxgloveServiceHub : MonoBehaviour
    {
        private const float ManagerSearchIntervalSeconds = 3f;
        private const float ScanIntervalSeconds = 2f;
        private const string JsonMessageEncoding = "json";

        private static FoxgloveServiceHub _instance;
        private static readonly object PendingGate = new();
        private static readonly List<IFoxgloveServiceSource> PendingRegistrations = new();

        [SerializeField] private FoxgloveManager _manager;
        [SerializeField] private bool _enableFallbackSceneScan = true;

        private readonly Dictionary<IFoxgloveServiceSource, List<uint>> _serviceIdsBySource = new();
        private readonly Dictionary<string, IFoxgloveServiceSource> _ownersByServiceName = new();
        private readonly HashSet<string> _warnedFailures = new();
        private float _managerSearchCooldown;
        private float _scanTimer;
        private bool _managerWasRunning;

        /// <summary>Registers a generated service source without waiting for fallback scene scan.</summary>
        public static void RegisterSource(IFoxgloveServiceSource source)
        {
            if (SourceUnavailable(source))
                return;

            lock (PendingGate)
            {
                if (!PendingRegistrations.Contains(source))
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
                PendingRegistrations.Remove(source);
            }

            _instance?.UnregisterSourceNow(source);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            lock (PendingGate)
            {
                PendingRegistrations.Clear();
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

        private void Update()
        {
            ResolveManagerIfNeeded();
            if (_manager == null)
            {
                if (_managerWasRunning)
                    UnregisterAll();
                _managerWasRunning = false;
                return;
            }

            DrainPendingRegistrations();
            RemoveDisabledOrDestroyedSources();

            if (!_manager.IsRunning)
            {
                if (_managerWasRunning)
                    UnregisterAll();
                _managerWasRunning = false;
                return;
            }

            _managerWasRunning = true;

            if (_enableFallbackSceneScan)
            {
                _scanTimer -= Time.deltaTime;
                if (_scanTimer <= 0f)
                {
                    _scanTimer = ScanIntervalSeconds;
                    Scan();
                }
            }
        }

        private void OnDisable()
        {
            UnregisterAll();
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
                    RegisterSourceNow(source);
            }
        }

        private void DrainPendingRegistrations()
        {
            IFoxgloveServiceSource[] pending;
            lock (PendingGate)
            {
                if (PendingRegistrations.Count == 0)
                    return;

                pending = PendingRegistrations.ToArray();
                PendingRegistrations.Clear();
            }

            foreach (var source in pending)
                RegisterSourceNow(source);
        }

        private void RegisterSourceNow(IFoxgloveServiceSource source)
        {
            if (SourceUnavailable(source) || _serviceIdsBySource.ContainsKey(source))
                return;
            if (_manager == null || !_manager.IsRunning)
                return;

            var descriptors = source.FoxgloveServices;
            if (descriptors == null || descriptors.Count == 0)
                return;

            if (!TryReserveServiceNames(source, descriptors))
                return;

            var ids = new List<uint>(descriptors.Count);
            foreach (var descriptor in descriptors)
            {
                var id = _manager.RegisterService(ToServiceDescriptor(descriptor), descriptor.Handler);
                if (id == 0)
                {
                    WarnOnce(source, descriptor.Name, "manager runtime is not available");
                    ReleaseServiceNames(source, descriptors);
                    foreach (var registered in ids)
                        _manager.UnregisterService(registered);
                    return;
                }

                ids.Add(id);
            }

            _serviceIdsBySource[source] = ids;
        }

        private bool TryReserveServiceNames(
            IFoxgloveServiceSource source,
            IReadOnlyList<FoxgloveGeneratedServiceDescriptor> descriptors)
        {
            var reserved = new List<string>(descriptors.Count);
            var seenInSource = new HashSet<string>(StringComparer.Ordinal);
            foreach (var descriptor in descriptors)
            {
                var name = descriptor?.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    WarnOnce(source, "<empty>", "empty service name");
                    ReleaseServiceNames(source, reserved);
                    return false;
                }

                if (!seenInSource.Add(name))
                {
                    WarnOnce(source, name, "duplicate generated service name within source");
                    ReleaseServiceNames(source, reserved);
                    return false;
                }

                if (_ownersByServiceName.TryGetValue(name, out var owner) && !ReferenceEquals(owner, source))
                {
                    WarnOnce(source, name, "duplicate generated service name");
                    ReleaseServiceNames(source, reserved);
                    return false;
                }

                _ownersByServiceName[name] = source;
                reserved.Add(name);
            }

            return true;
        }

        private void ReleaseServiceNames(
            IFoxgloveServiceSource source,
            IReadOnlyList<FoxgloveGeneratedServiceDescriptor> descriptors)
        {
            var names = new List<string>(descriptors.Count);
            foreach (var descriptor in descriptors)
                names.Add(descriptor?.Name ?? string.Empty);
            ReleaseServiceNames(source, names);
        }

        private void ReleaseServiceNames(IFoxgloveServiceSource source, IReadOnlyList<string> names)
        {
            foreach (var name in names)
            {
                if (_ownersByServiceName.TryGetValue(name, out var owner) && ReferenceEquals(owner, source))
                    _ownersByServiceName.Remove(name);
            }
        }

        private void RemoveDisabledOrDestroyedSources()
        {
            if (_serviceIdsBySource.Count == 0)
                return;

            var stale = new List<IFoxgloveServiceSource>();
            foreach (var source in _serviceIdsBySource.Keys)
            {
                if (SourceUnavailable(source))
                    stale.Add(source);
            }

            foreach (var source in stale)
                UnregisterSourceNow(source);
        }

        private void UnregisterSourceNow(IFoxgloveServiceSource source)
        {
            if (ReferenceEquals(source, null))
                return;

            if (_serviceIdsBySource.TryGetValue(source, out var ids))
            {
                foreach (var id in ids)
                    _manager?.UnregisterService(id);
                _serviceIdsBySource.Remove(source);
            }

            if (SourceUnavailable(source))
            {
                ReleaseServiceNamesByOwner(source);
                return;
            }

            var descriptors = source.FoxgloveServices;
            if (descriptors != null)
                ReleaseServiceNames(source, descriptors);
        }

        private static bool SourceUnavailable(IFoxgloveServiceSource source)
        {
            if (ReferenceEquals(source, null))
                return true;

            if (source is MonoBehaviour behaviour)
                return behaviour == null || !behaviour.isActiveAndEnabled;

            return false;
        }

        private void UnregisterAll()
        {
            foreach (var ids in _serviceIdsBySource.Values)
            {
                foreach (var id in ids)
                    _manager?.UnregisterService(id);
            }

            _serviceIdsBySource.Clear();
            _ownersByServiceName.Clear();
        }

        private void ReleaseServiceNamesByOwner(IFoxgloveServiceSource source)
        {
            var names = new List<string>();
            foreach (var pair in _ownersByServiceName)
            {
                if (ReferenceEquals(pair.Value, source))
                    names.Add(pair.Key);
            }

            foreach (var name in names)
                _ownersByServiceName.Remove(name);
        }

        private static ServiceDescriptor ToServiceDescriptor(FoxgloveGeneratedServiceDescriptor generated)
        {
            return new ServiceDescriptor
            {
                Name = generated.Name,
                Type = generated.Type,
                Request = new ServiceSchemaDescriptor
                {
                    Encoding = JsonMessageEncoding,
                    SchemaName = generated.RequestSchemaName,
                    Schema = string.Empty
                },
                Response = new ServiceSchemaDescriptor
                {
                    Encoding = JsonMessageEncoding,
                    SchemaName = generated.ResponseSchemaName,
                    Schema = string.Empty
                }
            };
        }

        private void WarnOnce(IFoxgloveServiceSource source, string serviceName, string message)
        {
            var sourceName = source?.GetType().FullName ?? "<null>";
            var key = sourceName + ":" + serviceName + ":" + message;
            if (_warnedFailures.Add(key))
                Debug.LogWarning("[FoxService] " + sourceName + " service '" + serviceName + "' was not registered: " + message);
        }
    }
}
