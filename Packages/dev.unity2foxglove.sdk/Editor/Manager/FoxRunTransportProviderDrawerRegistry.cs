// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Editor-only extension registry for Provider controls inside the Manager Inspector.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public interface IFoxRunTransportProviderDrawer
    {
        string TransportId { get; }
        string DisplayName { get; }
        FoxRunTransportCapabilities Capabilities { get; }

        void EnsureProvider(FoxgloveManager manager);
        void Draw(FoxgloveManager manager, SerializedObject managerObject);
    }

    /// <summary>
    /// Domain-reload-scoped definitions only. Runtime Provider/component
    /// instances are never stored here.
    /// </summary>
    [InitializeOnLoad]
    public static class FoxRunTransportProviderDrawerRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<FoxRunTransportId, IFoxRunTransportProviderDrawer>
            Drawers = new Dictionary<FoxRunTransportId, IFoxRunTransportProviderDrawer>();

        static FoxRunTransportProviderDrawerRegistry()
        {
        }

        public static void Register(IFoxRunTransportProviderDrawer drawer)
        {
            if (drawer == null)
                throw new ArgumentNullException(nameof(drawer));
            var id = new FoxRunTransportId(drawer.TransportId);
            if (string.IsNullOrWhiteSpace(drawer.DisplayName))
                throw new ArgumentException("Provider drawer display name cannot be empty.", nameof(drawer));
            if (drawer.Capabilities == 0)
                throw new ArgumentException("Provider drawer capabilities cannot be empty.", nameof(drawer));

            lock (Gate)
            {
                if (Drawers.TryGetValue(id, out var existing)
                    && !ReferenceEquals(existing, drawer)
                    && existing.GetType() != drawer.GetType())
                {
                    throw new InvalidOperationException(
                        "Duplicate FoxRun Provider drawer ID '" + id.Value + "'.");
                }

                Drawers[id] = drawer;
            }
        }

        public static IReadOnlyList<IFoxRunTransportProviderDrawer> Capture()
        {
            lock (Gate)
            {
                return Drawers
                    .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
                    .Select(pair => pair.Value)
                    .ToArray();
            }
        }
    }
}
