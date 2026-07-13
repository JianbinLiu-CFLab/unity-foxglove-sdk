// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Inspector controls for Unity subscriptions to client-published FoxRun data.

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

            FoxgloveManagerInspectorLayout.Subheader("Subscription Encoding");
            FoxRunEncodingEditorLabels.DrawFoxRunWireEncoding(
                FindCachedProperty("_defaultFoxRunSubscriptionEncoding"),
                "Default Subscription Encoding");
            if (Application.isPlaying && manager != null && manager.IsRunning)
            {
                EditorGUILayout.HelpBox(
                    "Subscription-policy changes apply after the server is restarted or re-enabled. The active FoxRun session keeps its captured encoding.",
                    MessageType.Info);
            }

            FoxgloveManagerInspectorLayout.Subheader("FoxRun Subscription Control");
            DrawProperty("_enableFoxRunInbound", "Enable FoxRun Subscriptions");
            using (new EditorGUI.DisabledScope(!GetBool("_enableFoxRunInbound")))
            {
                DrawProperty("_allowRemoteFoxRunInboundWithSharedToken", "Allow Remote FoxRun Subscriptions With Shared Token");
                DrawProperty("_foxRunInboundMaxPayloadBytes", "Subscription Max Payload Bytes");
                DrawProperty("_foxRunInboundMaxMessagesPerSecondPerTopic", "Subscription Rate Limit Hz (per Topic)");
            }
            if (GetBool("_enableFoxRunInbound")
                && !FoxgloveManager.IsLoopbackHost(GetString("_host", "127.0.0.1"))
                && (!GetBool("_allowRemoteFoxRunInboundWithSharedToken")
                    || string.IsNullOrWhiteSpace(GetString("_sharedToken", ""))))
            {
                EditorGUILayout.HelpBox(
                    "FoxRun subscriptions are fail-closed for non-loopback hosts. Enable remote subscriptions explicitly and configure a shared token.",
                    MessageType.Warning);
            }
        }
    }
}
