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
            var providerProperty = FindCachedProperty("_defaultFoxRunSubscriptionProvider");
            var encodingProperty = FindCachedProperty("_defaultFoxRunSubscriptionEncoding");

            FoxgloveManagerInspectorLayout.Subheader("Subscription Protocol");
            FoxRunSubscriptionProtocolEditorLabels.Draw(
                providerProperty,
                encodingProperty,
                "Default Subscription Protocol");
            if (manager != null && manager.ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled)
            {
                EditorGUILayout.HelpBox(
                    "Subscription-policy changes apply after subscriptions are re-enabled. The active FoxRun session keeps its captured provider, WebSocket encoding, QoS, and copy budget.",
                    MessageType.Info);
            }

            FoxgloveManagerInspectorLayout.Subheader("FoxRun Subscription Control");
            DrawProperty("_enableFoxRunInbound", "Enable FoxRun Subscriptions");
            EditorGUILayout.HelpBox(
                "Unity subscribes to data published by a ROS2 or Foxglove client. This is independent from Unity publish output.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(!GetBool("_enableFoxRunInbound")))
            {
                DrawProperty("_foxRunInboundMaxMessagesPerSecondPerTopic", "Subscription Rate Limit Hz (per Topic)");

                var selectedProvider = providerProperty != null
                    && providerProperty.enumValueIndex == (int)FoxRunSubscriptionProvider.Ros2Native
                    ? FoxRunSubscriptionProvider.Ros2Native
                    : FoxRunSubscriptionProvider.FoxgloveWebSocket;
                var showWebSocket = selectedProvider == FoxRunSubscriptionProvider.FoxgloveWebSocket
                                    || HasExplicitSubscriptionProvider(FoxRunSubscriptionProvider.FoxgloveWebSocket);
                var showRos2Native = selectedProvider == FoxRunSubscriptionProvider.Ros2Native
                                     || HasExplicitSubscriptionProvider(FoxRunSubscriptionProvider.Ros2Native);

                if (showWebSocket)
                {
                    FoxgloveManagerInspectorLayout.Subheader("Foxglove WebSocket Subscription Settings");
                    DrawProperty("_allowRemoteFoxRunInboundWithSharedToken", "Allow Remote FoxRun Subscriptions With Shared Token");
                    DrawProperty("_foxRunInboundMaxPayloadBytes", "Subscription Max Payload Bytes");
                }

                if (showRos2Native)
                {
                    FoxgloveManagerInspectorLayout.Subheader("ROS2 Native Subscription Settings");
                    DrawProperty("_defaultFoxRunRos2Qos", "Default ROS2 QoS");
                    DrawProperty("_foxRunRos2NativeCopyBudgetBytes", "Native Copied-Data Budget Bytes");
                }
            }
            if (HasR2fuNativeRuntimeDemand())
            {
                EditorGUILayout.HelpBox(
                    "Native subscription demand uses the shared ROS2 Runtime (R2FU) section below; it does not enable ROS2 Publish Data output.",
                    MessageType.Info);
            }
            if (GetBool("_enableFoxRunInbound")
                && (providerProperty == null
                    || providerProperty.enumValueIndex != (int)FoxRunSubscriptionProvider.Ros2Native
                    || HasExplicitSubscriptionProvider(FoxRunSubscriptionProvider.FoxgloveWebSocket))
                && !FoxgloveManager.IsLoopbackHost(GetString("_host", "127.0.0.1"))
                && (!GetBool("_allowRemoteFoxRunInboundWithSharedToken")
                    || string.IsNullOrWhiteSpace(GetString("_sharedToken", ""))))
            {
                EditorGUILayout.HelpBox(
                    "FoxRun subscriptions are fail-closed for non-loopback hosts. Enable remote subscriptions explicitly and configure a shared token.",
                    MessageType.Warning);
            }

            if (HasR2fuNativeRuntimeDemand()
                || HasExplicitSubscriptionProvider(FoxRunSubscriptionProvider.Ros2Native))
            {
                FoxgloveManagerInspectorLayout.Subheader("ROS2 Native Subscription Diagnostics");
                DrawOptionalR2fuNativeSubscriptionDiagnostics();
            }
        }

        private static bool HasExplicitSubscriptionProvider(FoxRunSubscriptionProvider provider)
            => HasGeneratedExplicitSubscriptionProvider(provider);
    }
}
