// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxService
// Purpose: Service registration ownership for generated declarative FoxServices.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Protocol;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public sealed partial class FoxgloveServiceHub
    {
        private const string JsonMessageEncoding = "json";

        private readonly List<IFoxgloveServiceSource> _pendingDrainBuffer = new();
        private readonly List<IFoxgloveServiceSource> _staleSources = new();
        private readonly List<IFoxgloveServiceSource> _temporarilyUnavailableSources = new();
        private readonly Dictionary<IFoxgloveServiceSource, List<uint>> _serviceIdsBySource = new();
        private readonly Dictionary<IFoxgloveServiceSource, List<FoxgloveGeneratedServiceDescriptor>> _descriptorsBySource = new();
        private readonly Dictionary<string, IFoxgloveServiceSource> _ownersByServiceName = new();

        private void DrainPendingRegistrations()
        {
            lock (PendingGate)
            {
                if (PendingRegistrations.Count == 0)
                    return;

                _pendingDrainBuffer.AddRange(PendingRegistrations);
                PendingRegistrations.Clear();
                PendingRegistrationSet.Clear();
            }

            try
            {
                foreach (var source in _pendingDrainBuffer)
                    RegisterSourceNow(source);
            }
            finally
            {
                _pendingDrainBuffer.Clear();
            }
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
            _descriptorsBySource[source] = new List<FoxgloveGeneratedServiceDescriptor>(descriptors);
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

            _staleSources.Clear();
            foreach (var source in _serviceIdsBySource.Keys)
            {
                if (SourceUnavailable(source))
                    _staleSources.Add(source);
            }

            foreach (var source in _staleSources)
            {
                UnregisterSourceNow(source);
                TrackTemporarilyUnavailableSource(source);
            }
            _staleSources.Clear();
        }

        private void TrackTemporarilyUnavailableSource(IFoxgloveServiceSource source)
        {
            if (source is MonoBehaviour behaviour
                && behaviour != null
                && !_temporarilyUnavailableSources.Contains(source))
                _temporarilyUnavailableSources.Add(source);
        }

        private void ReregisterReenabledSources()
        {
            // Source lifecycle callbacks can precede the hub's registration path, so poll only the small parked set.
            for (var i = _temporarilyUnavailableSources.Count - 1; i >= 0; i--)
            {
                var source = _temporarilyUnavailableSources[i];
                if (source is not MonoBehaviour behaviour || behaviour == null)
                {
                    _temporarilyUnavailableSources.RemoveAt(i);
                    continue;
                }

                if (!behaviour.isActiveAndEnabled)
                    continue;

                _temporarilyUnavailableSources.RemoveAt(i);
                RegisterSourceNow(source);
            }
        }

        private void RemoveTemporarilyUnavailableSource(IFoxgloveServiceSource source)
        {
            _temporarilyUnavailableSources.Remove(source);
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
            _descriptorsBySource.Remove(source);

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

            // Non-MonoBehaviour service sources own their lifetime and must call UnregisterSource explicitly.
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
            _descriptorsBySource.Clear();
            _ownersByServiceName.Clear();
            _warnedFailures.Clear();
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
                    Schema = generated.RequestSchema
                },
                Response = new ServiceSchemaDescriptor
                {
                    Encoding = JsonMessageEncoding,
                    SchemaName = generated.ResponseSchemaName,
                    Schema = generated.ResponseSchema
                }
            };
        }
    }
}
