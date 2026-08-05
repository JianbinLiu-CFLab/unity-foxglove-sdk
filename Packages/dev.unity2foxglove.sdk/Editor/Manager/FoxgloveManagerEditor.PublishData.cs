// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Provider-neutral publish controls and Provider editor extensions.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor
    {
        private void DrawPublishDataSection()
        {
            FoxgloveManagerInspectorLayout.Subheader("Publish Destinations");
            var destinations =
                FindCachedProperty("_foxRunPublishTransportIds");
            DrawPublishTransportSelection(destinations);

            if (SerializedStringArrayContains(
                    destinations,
                    FoxgloveWebSocketTransport.Id))
            {
                FoxRunEncodingEditorLabels.DrawFoxRunEncoding(
                    FindCachedProperty("_defaultFoxRunPublishEncoding"),
                    "WebSocket Encoding");
            }

            DrawFloatProperty(
                "_defaultPublishRateHz",
                "Default Publish Rate Hz",
                "Default rate used by publishers that choose the Manager default. Use <= 0 to publish every eligible frame.");

            var manager = target as FoxgloveManager;
            if (manager != null
                && manager.ActiveFoxRunPublishSessionPolicy.SessionActive)
            {
                EditorGUILayout.HelpBox(
                    "Publish profile changes apply after this Manager is disabled and re-enabled. The active session retains its captured Provider selection, encoding, and rate.",
                    MessageType.Info);
            }

            FoxgloveManagerInspectorLayout.Subheader("Component Publishers");
            DrawGlobalEncodingProperty(
                "_defaultPublisherEncoding",
                "WebSocket Encoding");
            DrawProperty(
                "_allowPublisherOverride",
                "Allow Component Publisher Override");
            EditorGUILayout.HelpBox(
                "Component publishers and generated FoxRun contracts use independent WebSocket encoding defaults.",
                MessageType.Info);

            FoxgloveManagerInspectorLayout.Subheader("Coordinate System");
            DrawProperty("_outputCoordinateMode", "Output Coordinate Mode");
            EditorGUILayout.HelpBox(
                "Defines the coordinate convention of supported data published from Unity. MCAP records the same converted external payload and labels output channels with this mode.",
                MessageType.Info);

            FoxgloveManagerInspectorLayout.Subheader("Assets");
            DrawProperty("_assetRoots");
        }

        private void DrawFoxRunTransportProviderExtensions()
        {
            var manager = target as FoxgloveManager;
            if (manager == null)
                return;

            foreach (var setupDrawer in
                     FoxRunManagerSetupDrawerRegistry.Capture())
            {
                setupDrawer.Draw(manager, serializedObject);
            }

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox(
                    "Provider details are available when inspecting one FoxgloveManager. Mixed selections never create Provider companions.",
                    MessageType.Info);
                return;
            }

            var publishTransportIds =
                FindCachedProperty(
                    "_foxRunPublishTransportIds");
            var subscribeTransportId =
                FindCachedProperty(
                    "_foxRunSubscribeTransportId");
            DrawFoxRunTransportObservedStatus(
                manager,
                publishTransportIds,
                subscribeTransportId);
            foreach (var drawer in
                     FoxRunTransportProviderDrawerRegistry.Capture())
            {
                if (ShouldEnsureProvider(
                        drawer,
                        publishTransportIds,
                        subscribeTransportId))
                {
                    drawer.EnsureProvider(manager);
                }

                drawer.Draw(manager, serializedObject);
            }
        }

        private static void DrawFoxRunTransportObservedStatus(
            FoxgloveManager manager,
            SerializedProperty publishTransportIds,
            SerializedProperty subscribeTransportId)
        {
            var statuses =
                manager.CaptureFoxRunTransportStatuses();
            var failure =
                manager.LastFoxRunTransportSessionCaptureError;
            var visibleProviderIds = new HashSet<string>(
                statuses.Select(status => status.ProviderId.Value),
                StringComparer.Ordinal);
            foreach (var transportId in
                     ReadSerializedStringSet(publishTransportIds))
            {
                if (!string.IsNullOrWhiteSpace(transportId))
                    visibleProviderIds.Add(transportId);
            }
            var configuredSubscribeTransportId =
                subscribeTransportId?.stringValue;
            if (!string.IsNullOrWhiteSpace(configuredSubscribeTransportId))
                visibleProviderIds.Add(configuredSubscribeTransportId);
            var retired = manager
                .CaptureRetiredFoxRunTransportWorkers()
                .Where(worker => visibleProviderIds.Contains(
                    worker.ProviderId.Value))
                .ToArray();
            var finalExits = manager
                .CaptureFoxRunTransportWorkerFinalExits()
                .Where(worker => visibleProviderIds.Contains(
                    worker.ProviderId.Value))
                .ToArray();
            if (statuses.Count == 0
                && !failure.HasValue
                && retired.Length == 0
                && finalExits.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Provider Runtime Status",
                EditorStyles.boldLabel);
            if (failure.HasValue)
            {
                var captureFailure = failure.Value;
                EditorGUILayout.HelpBox(
                    "Configured Provider capture failed closed ["
                    + captureFailure.Code
                    + "] "
                    + (captureFailure.TransportId.Value
                       ?? "<invalid Provider ID>")
                    + ": "
                    + captureFailure.Reason,
                    MessageType.Error);
            }

            for (var index = 0; index < statuses.Count; index++)
            {
                var status = statuses[index];
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(
                        "Provider",
                        status.ProviderId.Value);
                    EditorGUILayout.EnumPopup(
                        "Aggregate",
                        status.State);
                    if (status.Publish.Selected)
                    {
                        EditorGUILayout.TextField(
                            "Publish",
                            DirectionLabel(status.Publish));
                    }
                    if (status.Subscribe.Selected)
                    {
                        EditorGUILayout.TextField(
                            "Subscribe",
                            DirectionLabel(status.Subscribe));
                    }
                }
                for (var diagnosticIndex = 0;
                     diagnosticIndex < status.Diagnostics.Count;
                     diagnosticIndex++)
                {
                    var diagnostic =
                        status.Diagnostics[diagnosticIndex];
                    EditorGUILayout.HelpBox(
                        diagnostic.Code + ": " + diagnostic.Message,
                        status.State == FoxRunTransportObservedState.Failed
                            ? MessageType.Error
                            : MessageType.Warning);
                }
            }

            for (var index = 0; index < retired.Length; index++)
            {
                var worker = retired[index];
                EditorGUILayout.HelpBox(
                    "Retired Provider worker: "
                    + worker.ProviderId.Value
                    + " / "
                    + worker.Direction
                    + " / generation "
                    + worker.Generation
                    + " / "
                    + worker.WorkerIdentity
                    + " / resources "
                    + worker.RetainedResources
                    + " / bytes "
                    + worker.RetainedBytes
                    + " / age "
                    + worker.Age.TotalSeconds.ToString("F1")
                    + "s",
                    MessageType.Warning);
            }

            var firstExit = Math.Max(0, finalExits.Length - 4);
            for (var index = firstExit;
                 index < finalExits.Length;
                 index++)
            {
                var worker = finalExits[index];
                EditorGUILayout.HelpBox(
                    worker.DiagnosticCode
                    + ": Provider worker final exit: "
                    + worker.ProviderId.Value
                    + " / "
                    + worker.Direction
                    + " / generation "
                    + worker.Generation
                    + " / "
                    + worker.WorkerIdentity
                    + " / resources "
                    + worker.RetainedResources
                    + " / bytes "
                    + worker.RetainedBytes
                    + " / age "
                    + worker.Age.TotalSeconds.ToString("F1")
                    + "s"
                    + (worker.Succeeded
                        ? " / completed"
                        : " / failed: " + worker.Failure),
                    worker.Succeeded
                        ? MessageType.Info
                        : MessageType.Error);
            }
        }

        private static string DirectionLabel(
            FoxRunTransportDirectionStatus status)
            => status.State
               + " (ready "
               + status.ReadyContractCount
               + "/"
               + status.ObservedContractCount
               + ", failed "
               + status.FailedContractCount
               + ")";

        private void DrawPublishTransportSelection(
            SerializedProperty destinations)
        {
            if (destinations == null)
                return;
            if (serializedObject.isEditingMultipleObjects
                || destinations.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(
                    destinations,
                    new GUIContent("Publish Destinations"),
                    includeChildren: true);
                return;
            }

            var selected = ReadSerializedStringSet(destinations);
            var choices = CaptureProviderChoices(
                FoxRunTransportCapabilities.Publish);
            var known = new HashSet<string>(
                choices.Select(choice => choice.TransportId),
                StringComparer.Ordinal);
            var changed = false;
            foreach (var choice in choices)
            {
                var wasSelected = selected.Contains(choice.TransportId);
                var isSelected = EditorGUILayout.ToggleLeft(
                    choice.DisplayName + " (" + choice.TransportId + ")",
                    wasSelected);
                if (isSelected == wasSelected)
                    continue;
                changed = true;
                if (isSelected)
                    selected.Add(choice.TransportId);
                else
                    selected.Remove(choice.TransportId);
            }

            var unavailable = selected
                .Where(id => !known.Contains(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            foreach (var id in unavailable)
            {
                var conflicted =
                    FoxRunTransportProviderDrawerRegistry
                        .IsConflicted(id);
                var keep = EditorGUILayout.ToggleLeft(
                    (conflicted
                        ? "Conflicted Provider"
                        : "Unavailable Provider")
                    + " ("
                    + (string.IsNullOrEmpty(id) ? "<empty>" : id)
                    + ")",
                    true);
                if (keep)
                    continue;
                selected.Remove(id);
                changed = true;
            }

            if (unavailable.Length != 0)
            {
                EditorGUILayout.HelpBox(
                    "Configured unavailable or conflicted Provider IDs fail closed. Clear them explicitly or install/repair the owning package; no fallback is selected.",
                    MessageType.Error);
            }
            if (changed)
                WriteSerializedStringSet(destinations, selected);
        }

        private void DrawSubscribeTransportSelection(
            SerializedProperty source,
            string label)
        {
            if (source == null)
                return;
            if (serializedObject.isEditingMultipleObjects
                || source.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(
                    source,
                    new GUIContent(label));
                return;
            }

            var choices = CaptureProviderChoices(
                FoxRunTransportCapabilities.Subscribe);
            var labels = new List<string> { "Not Configured" };
            var values = new List<string> { string.Empty };
            for (var index = 0; index < choices.Count; index++)
            {
                labels.Add(
                    choices[index].DisplayName
                    + " ("
                    + choices[index].TransportId
                    + ")");
                values.Add(choices[index].TransportId);
            }

            var current = source.stringValue ?? string.Empty;
            var currentIndex = values.IndexOf(current);
            if (currentIndex < 0)
            {
                var conflicted =
                    FoxRunTransportProviderDrawerRegistry
                        .IsConflicted(current);
                labels.Add(
                    (conflicted ? "Conflicted" : "Unavailable")
                    + " ("
                    + current
                    + ")");
                values.Add(current);
                currentIndex = values.Count - 1;
            }

            var next = EditorGUILayout.Popup(
                label,
                currentIndex,
                labels.ToArray());
            if (next != currentIndex)
                source.stringValue = values[next];
        }

        private static bool IsSelectableTransportId(
            string transportId,
            FoxRunTransportCapabilities capability)
        {
            if (string.Equals(
                    transportId,
                    FoxgloveWebSocketTransport.Id,
                    StringComparison.Ordinal))
            {
                return true;
            }
            return FoxRunTransportProviderDrawerRegistry.Capture()
                .Any(drawer =>
                    string.Equals(
                        drawer.TransportId,
                        transportId,
                        StringComparison.Ordinal)
                    && (drawer.Capabilities & capability) == capability);
        }

        private static IReadOnlyList<ProviderChoice>
            CaptureProviderChoices(
                FoxRunTransportCapabilities capability)
        {
            var choices = new List<ProviderChoice>
            {
                new ProviderChoice(
                    FoxgloveWebSocketTransport.Id,
                    "Foxglove WebSocket")
            };
            foreach (var drawer in
                     FoxRunTransportProviderDrawerRegistry.Capture())
            {
                if ((drawer.Capabilities & capability) != capability)
                    continue;
                choices.Add(new ProviderChoice(
                    drawer.TransportId,
                    drawer.DisplayName));
            }
            return choices;
        }

        private static HashSet<string> ReadSerializedStringSet(
            SerializedProperty property)
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            if (property == null || !property.isArray)
                return values;
            for (var index = 0; index < property.arraySize; index++)
            {
                values.Add(
                    property.GetArrayElementAtIndex(index).stringValue
                    ?? string.Empty);
            }
            return values;
        }

        private static void WriteSerializedStringSet(
            SerializedProperty property,
            IEnumerable<string> values)
        {
            var canonical = values
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            property.ClearArray();
            for (var index = 0; index < canonical.Length; index++)
            {
                property.InsertArrayElementAtIndex(index);
                property.GetArrayElementAtIndex(index).stringValue =
                    canonical[index];
            }
        }

        private readonly struct ProviderChoice
        {
            internal ProviderChoice(
                string transportId,
                string displayName)
            {
                TransportId = transportId;
                DisplayName = displayName;
            }

            internal string TransportId { get; }
            internal string DisplayName { get; }
        }

        private bool ShouldEnsureProvider(
            IFoxRunTransportProviderDrawer drawer,
            SerializedProperty publishTransportIds,
            SerializedProperty subscribeTransportId)
        {
            if (drawer == null
                || serializedObject.isEditingMultipleObjects)
            {
                return false;
            }

            var publishSelected =
                (drawer.Capabilities
                 & FoxRunTransportCapabilities.Publish) != 0
                && publishTransportIds != null
                && !publishTransportIds
                    .hasMultipleDifferentValues
                && SerializedStringArrayContains(
                    publishTransportIds,
                    drawer.TransportId);
            var subscribeSelected =
                (drawer.Capabilities
                 & FoxRunTransportCapabilities.Subscribe) != 0
                && subscribeTransportId != null
                && !subscribeTransportId
                    .hasMultipleDifferentValues
                && string.Equals(
                    subscribeTransportId.stringValue,
                    drawer.TransportId,
                    System.StringComparison.Ordinal);
            return publishSelected || subscribeSelected;
        }

        private static bool SerializedStringArrayContains(
            SerializedProperty property,
            string expected)
        {
            if (property == null || !property.isArray)
                return false;

            for (var i = 0; i < property.arraySize; i++)
            {
                if (string.Equals(
                        property.GetArrayElementAtIndex(i).stringValue,
                        expected,
                        System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
