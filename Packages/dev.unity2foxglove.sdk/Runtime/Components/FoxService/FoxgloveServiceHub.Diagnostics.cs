// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxService
// Purpose: Service inspection, local invocation, and warning diagnostics.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public sealed partial class FoxgloveServiceHub
    {
        private readonly HashSet<string> _warnedFailures = new();

        public IReadOnlyList<FoxgloveRegisteredServiceSnapshot> GetRegisteredServiceSnapshots()
        {
            var snapshots = new List<FoxgloveRegisteredServiceSnapshot>();
            foreach (var pair in _serviceIdsBySource)
            {
                var source = pair.Key;
                var ids = pair.Value;
                if (!_descriptorsBySource.TryGetValue(source, out var descriptors))
                    continue;

                var count = Math.Min(ids.Count, descriptors.Count);
                var sourceName = SourceDisplayName(source);
                for (var i = 0; i < count; i++)
                {
                    var descriptor = descriptors[i];
                    snapshots.Add(new FoxgloveRegisteredServiceSnapshot(
                        ids[i],
                        descriptor.Name,
                        descriptor.Type,
                        descriptor.RequestSchemaName,
                        descriptor.ResponseSchemaName,
                        sourceName));
                }
            }

            snapshots.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            return snapshots;
        }

        /// <summary>
        /// Invokes an existing generated service locally on the Unity main thread.
        /// This reuses the active FoxService registry and does not create a
        /// parallel service authoring or registration path.
        /// </summary>
        public FoxgloveLocalServiceCallResult CallLocal(
            string serviceName,
            Newtonsoft.Json.Linq.JToken request,
            TimeSpan timeout)
        {
            FoxgloveGeneratedServiceDescriptor descriptor = null;
            if (!string.IsNullOrEmpty(serviceName)
                && _ownersByServiceName.TryGetValue(serviceName, out var owner)
                && _descriptorsBySource.TryGetValue(owner, out var descriptors))
            {
                descriptor = descriptors.Find(item =>
                    string.Equals(item.Name, serviceName, StringComparison.Ordinal));
            }

            return FoxgloveLocalServiceCall.Invoke(descriptor, request, timeout);
        }

        private static string SourceDisplayName(IFoxgloveServiceSource source)
        {
            if (source is MonoBehaviour behaviour)
            {
                if (behaviour == null || behaviour.gameObject == null)
                    return source.GetType().Name;
                return behaviour.gameObject.name + " (" + source.GetType().Name + ")";
            }

            return source?.GetType().Name ?? string.Empty;
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
