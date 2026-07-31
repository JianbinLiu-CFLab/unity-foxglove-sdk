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

    public interface IFoxRunManagerSetupDrawer
    {
        string DrawerId { get; }
        int Order { get; }

        void Draw(
            FoxgloveManager manager,
            SerializedObject managerObject);
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

    /// <summary>
    /// Optional package setup controls that must remain available before a
    /// transport runtime or Provider component can exist.
    /// </summary>
    [InitializeOnLoad]
    public static class FoxRunManagerSetupDrawerRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<
            string,
            IFoxRunManagerSetupDrawer> Drawers =
                new Dictionary<
                    string,
                    IFoxRunManagerSetupDrawer>(
                    StringComparer.Ordinal);

        static FoxRunManagerSetupDrawerRegistry()
        {
        }

        public static void Register(
            IFoxRunManagerSetupDrawer drawer)
        {
            if (drawer == null)
                throw new ArgumentNullException(nameof(drawer));
            if (string.IsNullOrWhiteSpace(drawer.DrawerId))
            {
                throw new ArgumentException(
                    "Manager setup drawer ID cannot be empty.",
                    nameof(drawer));
            }

            lock (Gate)
            {
                if (Drawers.TryGetValue(
                        drawer.DrawerId,
                        out var existing)
                    && !ReferenceEquals(existing, drawer)
                    && existing.GetType() != drawer.GetType())
                {
                    throw new InvalidOperationException(
                        "Duplicate Manager setup drawer ID '"
                        + drawer.DrawerId
                        + "'.");
                }

                Drawers[drawer.DrawerId] = drawer;
            }
        }

        public static IReadOnlyList<
            IFoxRunManagerSetupDrawer> Capture()
        {
            lock (Gate)
            {
                return Drawers.Values
                    .OrderBy(drawer => drawer.Order)
                    .ThenBy(
                        drawer => drawer.DrawerId,
                        StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }
}
