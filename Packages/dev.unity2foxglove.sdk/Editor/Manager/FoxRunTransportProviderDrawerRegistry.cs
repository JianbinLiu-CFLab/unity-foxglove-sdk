// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Editor-only extension registry for Provider controls inside the Manager Inspector.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public interface IFoxRunTransportProviderDrawer
    {
        string TransportId { get; }
        string DisplayName { get; }
        int Order { get; }
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
        private static readonly FoxRunEditorDefinitionRegistry<
            IFoxRunTransportProviderDrawer> Drawers =
                new FoxRunEditorDefinitionRegistry<
                    IFoxRunTransportProviderDrawer>(
                    drawer => new FoxRunTransportId(
                            drawer.TransportId)
                        .Value,
                    drawer => drawer.Order);

        static FoxRunTransportProviderDrawerRegistry()
        {
        }

        public static void Register(IFoxRunTransportProviderDrawer drawer)
        {
            if (drawer == null)
                throw new ArgumentNullException(nameof(drawer));
            var id = new FoxRunTransportId(drawer.TransportId);
            if (id == FoxgloveWebSocketTransport.TransportId)
            {
                throw new InvalidOperationException(
                    "The built-in Foxglove WebSocket Provider ID is reserved.");
            }
            if (string.IsNullOrWhiteSpace(drawer.DisplayName))
                throw new ArgumentException("Provider drawer display name cannot be empty.", nameof(drawer));
            if (drawer.Capabilities == 0)
                throw new ArgumentException("Provider drawer capabilities cannot be empty.", nameof(drawer));

            if (Drawers.Register(drawer)
                == FoxRunEditorDefinitionRegistrationResult.Conflict)
            {
                throw new InvalidOperationException(
                    "Duplicate FoxRun Provider drawer ID '"
                    + id.Value
                    + "'. The ID is conflicted until domain reload.");
            }
        }

        public static IReadOnlyList<IFoxRunTransportProviderDrawer> Capture()
            => Drawers.Capture();

        public static bool IsConflicted(string transportId)
            => Drawers.IsConflicted(transportId);
    }

    /// <summary>
    /// Optional package setup controls that must remain available before a
    /// transport runtime or Provider component can exist.
    /// </summary>
    [InitializeOnLoad]
    public static class FoxRunManagerSetupDrawerRegistry
    {
        private static readonly FoxRunEditorDefinitionRegistry<
            IFoxRunManagerSetupDrawer> Drawers =
                new FoxRunEditorDefinitionRegistry<
                    IFoxRunManagerSetupDrawer>(
                    drawer => drawer.DrawerId,
                    drawer => drawer.Order);

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

            if (Drawers.Register(drawer)
                == FoxRunEditorDefinitionRegistrationResult.Conflict)
            {
                throw new InvalidOperationException(
                    "Duplicate Manager setup drawer ID '"
                    + drawer.DrawerId
                    + "'. The ID is conflicted until domain reload.");
            }
        }

        public static IReadOnlyList<
            IFoxRunManagerSetupDrawer> Capture()
            => Drawers.Capture();
    }
}
