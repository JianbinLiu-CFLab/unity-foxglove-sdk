// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Inspector controls for Unity subscriptions to client-published FoxRun data.

using System.Globalization;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor
    {
        private const string NativeCopyBudgetUnitSessionStateName =
            "DataTransportNativeCopyBudgetUnit";

        private static readonly string[] NativeCopyBudgetUnitLabels = { "KiB", "MiB" };

        private void DrawSubscribeDataSection()
        {
            var manager = target as FoxgloveManager;
            var providerProperty = FindCachedProperty("_defaultFoxRunSubscriptionProvider");
            var encodingProperty = FindCachedProperty("_defaultFoxRunSubscriptionEncoding");

            FoxgloveManagerInspectorLayout.Subheader("FoxRun Subscription Control");
            DrawProperty("_enableFoxRunInbound", "Enable FoxRun Subscriptions");
            EditorGUILayout.HelpBox(
                "Unity subscribes to data published by a ROS2 or Foxglove client. This is independent from Unity publish output.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(!GetBool("_enableFoxRunInbound")))
            {
                FoxgloveManagerInspectorLayout.Subheader("Input Transport");
                FoxRunSubscriptionProtocolEditorLabels.Draw(
                    providerProperty,
                    encodingProperty,
                    "Default Input Transport");
                var selectedProvider = providerProperty != null
                    && providerProperty.enumValueIndex == (int)FoxRunSubscriptionProvider.Ros2Native
                    ? FoxRunSubscriptionProvider.Ros2Native
                    : FoxRunSubscriptionProvider.FoxgloveWebSocket;
                var showWebSocket = selectedProvider == FoxRunSubscriptionProvider.FoxgloveWebSocket
                                    || HasExplicitSubscriptionProvider(FoxRunSubscriptionProvider.FoxgloveWebSocket);
                var showRos2Native = selectedProvider == FoxRunSubscriptionProvider.Ros2Native
                                     || HasExplicitSubscriptionProvider(FoxRunSubscriptionProvider.Ros2Native);
                if (manager != null && manager.ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled)
                {
                    EditorGUILayout.HelpBox(
                        "Subscription-policy changes apply after subscriptions are re-enabled. The active FoxRun session keeps its captured provider, WebSocket encoding, QoS, copy budget, and rate.",
                        MessageType.Info);
                }

                FoxgloveManagerInspectorLayout.Subheader("Subscription Delivery");
                DrawProperty("_foxRunInboundMaxMessagesPerSecondPerTopic", "Subscription Rate Limit Hz (per Topic)");

                if (showWebSocket)
                {
                    FoxgloveManagerInspectorLayout.Subheader("Foxglove WebSocket Input");
                    DrawProperty("_allowRemoteFoxRunInboundWithSharedToken", "Allow Remote FoxRun Subscriptions With Shared Token");
                    DrawProperty("_foxRunInboundMaxPayloadBytes", "Subscription Max Payload Bytes");
                }

                if (showRos2Native)
                {
                    FoxgloveManagerInspectorLayout.Subheader("ROS 2 Native Input");
                    DrawRos2NativeSubscriptionQos();
                    DrawRos2NativeCopyBudget();
                    DrawOptionalR2fuNativeSubscriptionDiagnostics();
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

        }

        private void DrawRos2NativeSubscriptionQos()
        {
            var qosProperty = FindCachedProperty("_defaultFoxRunRos2Qos");
            if (qosProperty == null)
            {
                DrawMissingProperty("_defaultFoxRunRos2Qos");
                return;
            }

            var normalizedPreset = FoxRunRos2QosResolver.NormalizeSerializedManagerDefault(
                (FoxRunRos2QosPreset)qosProperty.enumValueIndex);
            if (qosProperty.enumValueIndex != (int)normalizedPreset)
                qosProperty.enumValueIndex = (int)normalizedPreset;

            var choices = FoxRunRos2SubscriptionInspectorPresentation.ManagerQosChoices;
            var labels = new string[choices.Count];
            var selectedIndex = 0;
            for (var i = 0; i < choices.Count; i++)
            {
                labels[i] = choices[i].Label;
                if (choices[i].Preset == normalizedPreset)
                    selectedIndex = i;
            }

            var changedIndex = EditorGUILayout.Popup(
                "ROS 2 Native Subscription QoS",
                selectedIndex,
                labels);
            var selectedChoice = choices[changedIndex];
            if (selectedChoice.Preset != normalizedPreset)
                qosProperty.enumValueIndex = (int)selectedChoice.Preset;

            EditorGUILayout.HelpBox(selectedChoice.Summary, MessageType.Info);
        }

        private void DrawRos2NativeCopyBudget()
        {
            var budgetProperty = FindCachedProperty("_foxRunRos2NativeCopyBudgetBytes");
            if (budgetProperty == null)
            {
                DrawMissingProperty("_foxRunRos2NativeCopyBudgetBytes");
                return;
            }

            var displayUnit = GetNativeCopyBudgetDisplayUnit();
            var selectedUnitIndex = EditorGUILayout.Popup(
                "Native Copied-Message Budget Unit",
                (int)displayUnit,
                NativeCopyBudgetUnitLabels);
            if (selectedUnitIndex != (int)displayUnit)
            {
                displayUnit = (FoxRunRos2NativeCopyBudgetUnit)selectedUnitIndex;
                SessionState.SetInt(
                    InspectorFoldoutKey(NativeCopyBudgetUnitSessionStateName),
                    selectedUnitIndex);
            }

            var normalizedBytes = FoxRunRos2NativeCopyBudgetPolicy.NormalizeSerializedBytes(
                budgetProperty.intValue);
            if (budgetProperty.intValue != normalizedBytes)
                budgetProperty.intValue = normalizedBytes;

            var displayValue = FoxRunRos2SubscriptionInspectorPresentation.ToDisplayValue(
                normalizedBytes,
                displayUnit);
            EditorGUI.BeginChangeCheck();
            var editedDisplayValue = EditorGUILayout.DoubleField(
                "Native Copied-Message Budget (" + NativeCopyBudgetUnitLabels[(int)displayUnit] + ")",
                displayValue);
            if (EditorGUI.EndChangeCheck())
            {
                budgetProperty.intValue = FoxRunRos2SubscriptionInspectorPresentation.ToClampedBytes(
                    editedDisplayValue,
                    displayUnit);
                normalizedBytes = budgetProperty.intValue;
                displayValue = FoxRunRos2SubscriptionInspectorPresentation.ToDisplayValue(
                    normalizedBytes,
                    displayUnit);
            }

            EditorGUILayout.LabelField(
                "Stored Native Budget",
                displayValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " " + NativeCopyBudgetUnitLabels[(int)displayUnit]
                + " = " + normalizedBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
        }

        private static FoxRunRos2NativeCopyBudgetUnit GetNativeCopyBudgetDisplayUnit()
        {
            var storedUnit = SessionState.GetInt(
                InspectorFoldoutKey(NativeCopyBudgetUnitSessionStateName),
                (int)FoxRunRos2NativeCopyBudgetUnit.MiB);
            return storedUnit == (int)FoxRunRos2NativeCopyBudgetUnit.KiB
                ? FoxRunRos2NativeCopyBudgetUnit.KiB
                : FoxRunRos2NativeCopyBudgetUnit.MiB;
        }

        private static bool HasExplicitSubscriptionProvider(FoxRunSubscriptionProvider provider)
            => HasGeneratedExplicitSubscriptionProvider(provider);
    }
}
