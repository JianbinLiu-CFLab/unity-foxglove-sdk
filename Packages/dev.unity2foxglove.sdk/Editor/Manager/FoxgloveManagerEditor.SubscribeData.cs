// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Provider-neutral subscription controls.

using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor
    {
        private void DrawSubscribeDataSection()
        {
            var manager = target as FoxgloveManager;
            var source = FindCachedProperty(
                "_foxRunSubscribeTransportId");

            FoxgloveManagerInspectorLayout.Subheader(
                "Subscription Control");
            DrawProperty(
                "_enableFoxRunInbound",
                "Enable FoxRun Subscriptions");

            using (new EditorGUI.DisabledScope(
                       !GetBool("_enableFoxRunInbound")))
            {
                DrawSubscribeTransportSelection(source, "Source");

                if (GetBool("_enableFoxRunInbound")
                    && source != null
                    && !source.hasMultipleDifferentValues
                    && !IsSelectableTransportId(
                        source.stringValue,
                        FoxRunTransportCapabilities.Subscribe))
                {
                    EditorGUILayout.HelpBox(
                        "Configured Provider is unavailable or conflicted. Subscription capture fails closed; no fallback Source is selected.",
                        MessageType.Error);
                }

                if (source != null
                    && string.Equals(
                        source.stringValue,
                        FoxgloveWebSocketTransport.Id,
                        System.StringComparison.Ordinal))
                {
                    FoxRunEncodingEditorLabels.DrawFoxRunEncoding(
                        FindCachedProperty(
                            "_defaultFoxRunSubscriptionEncoding"),
                        "WebSocket Encoding");
                    DrawProperty(
                        "_allowRemoteFoxRunInboundWithSharedToken",
                        "Allow Remote Subscriptions With Shared Token");
                    DrawProperty(
                        "_foxRunInboundMaxPayloadBytes",
                        "Maximum Payload Bytes");
                }

                DrawProperty(
                    "_foxRunDefaultSubscribeRateHz",
                    "Default Subscribe Rate Hz");
                DrawProperty(
                    "_foxRunInboundMaxMessagesPerSecondPerTopic",
                    "Maximum Subscribe Rate Hz (per Topic)");

                if (manager != null
                    && manager.ActiveFoxRunSubscriptionSessionPolicy
                        .SubscriptionsEnabled)
                {
                    EditorGUILayout.HelpBox(
                        "Subscription profile changes apply after subscriptions are disabled and re-enabled. The active session retains its captured Provider, encoding, rate, and payload bounds.",
                        MessageType.Info);
                }
            }

            FoxgloveManagerInspectorLayout.Subheader(
                "Coordinate System");
            DrawProperty("_inputCoordinateMode", "Input Coordinate Mode");
            EditorGUILayout.HelpBox(
                "Defines the coordinate convention expected from supported external publishers. MCAP records original external input first; Unity converts an owned value only when applying it.",
                MessageType.Info);

            if (GetBool("_enableFoxRunInbound")
                && source != null
                && string.Equals(
                    source.stringValue,
                    FoxgloveWebSocketTransport.Id,
                    System.StringComparison.Ordinal)
                && !FoxgloveManager.IsLoopbackHost(
                    GetString("_host", "127.0.0.1"))
                && (!GetBool(
                        "_allowRemoteFoxRunInboundWithSharedToken")
                    || string.IsNullOrWhiteSpace(
                        GetString("_sharedToken", ""))))
            {
                EditorGUILayout.HelpBox(
                    "WebSocket subscriptions are fail-closed for non-loopback hosts. Enable remote subscriptions explicitly and configure a shared token.",
                    MessageType.Warning);
            }
        }
    }
}
