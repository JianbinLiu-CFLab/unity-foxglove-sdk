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

        private void DrawSubscribeDataSection()
        {
            var manager = target as FoxgloveManager;
            var sourceProperty = FindCachedProperty("_defaultFoxRunSubscriptionSource");
            var encodingProperty = FindCachedProperty("_defaultFoxRunSubscriptionEncoding");

            FoxgloveManagerInspectorLayout.Subheader("FoxRun Subscription Control");
            DrawProperty("_enableFoxRunInbound", "Enable FoxRun Subscriptions");
            EditorGUILayout.HelpBox(
                "Unity subscribes to data published by a ROS2 or Foxglove client. This is independent from Unity publish output.",
                MessageType.Info);

            FoxRunEndpoint selectedSource;
            bool showFoxglove;
            bool showRos2Native;
            using (new EditorGUI.DisabledScope(!GetBool("_enableFoxRunInbound")))
            {
                FoxgloveManagerInspectorLayout.Subheader("FoxRun Subscribe Profile");
                selectedSource = FoxRunEndpointEditorLabels.DrawSource(
                    sourceProperty,
                    "Source");
                showFoxglove = selectedSource == FoxRunEndpoint.Foxglove
                               || HasExplicitSource(FoxRunEndpoint.Foxglove);
                showRos2Native = selectedSource == FoxRunEndpoint.Ros2Native
                                 || HasExplicitSource(FoxRunEndpoint.Ros2Native);
                if (manager != null && manager.ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled)
                {
                    EditorGUILayout.HelpBox(
                        "FoxRun Subscribe Profile changes apply after subscriptions are disabled and re-enabled. The active session keeps its captured source, Foxglove encoding, QoS, copy budget, default subscribe rate, and maximum subscribe rate.",
                        MessageType.Info);
                }

                if (showFoxglove)
                {
                    FoxgloveManagerInspectorLayout.Subheader("Foxglove");
                    FoxRunEncodingEditorLabels.DrawFoxRunEncoding(
                        encodingProperty,
                        "Foxglove Encoding");
                    DrawProperty(
                        "_allowRemoteFoxRunInboundWithSharedToken",
                        "Allow Remote FoxRun Subscriptions With Shared Token");
                    DrawProperty(
                        "_foxRunInboundMaxPayloadBytes",
                        "Subscription Max Payload Bytes");
                }

                if (showRos2Native)
                {
                    FoxgloveManagerInspectorLayout.Subheader("ROS 2 Native");
                    DrawRos2NativeSubscriptionQos();
                    DrawRos2NativeCopyBudget();
                }

                DrawProperty("_foxRunDefaultSubscribeRateHz", "Default Subscribe Rate Hz");
                DrawProperty("_foxRunInboundMaxMessagesPerSecondPerTopic", "Maximum Subscribe Rate Hz (per Topic)");
            }

            FoxgloveManagerInspectorLayout.Subheader("Coordinate System");
            DrawProperty("_inputCoordinateMode", "Input Coordinate Mode");
            EditorGUILayout.HelpBox(
                "Defines the coordinate convention expected from supported external publishers. MCAP records original external input first; Unity converts an owned value only when applying it.",
                MessageType.Info);

            if (showRos2Native)
            {
                DrawOptionalR2fuNativeSubscriptionDiagnostics();
            }
            if (HasR2fuNativeSubscriptionDemand())
            {
                EditorGUILayout.HelpBox(
                    "ROS 2 Native Subscribe requires the shared ROS 2 Native Runtime (R2FU). Subscribe does not enable Publish.",
                    MessageType.Info);
            }
            if (GetBool("_enableFoxRunInbound")
                && (sourceProperty == null
                    || selectedSource != FoxRunEndpoint.Ros2Native
                    || HasExplicitSource(FoxRunEndpoint.Foxglove))
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
            var qosProperty = FindCachedProperty("_defaultFoxRunNativeSubscribeQos");
            if (qosProperty == null)
            {
                DrawMissingProperty("_defaultFoxRunNativeSubscribeQos");
                return;
            }

            DrawFoxRunRos2Qos(qosProperty, "ROS 2 Native QoS Profile");
        }

        private static void DrawFoxRunRos2Qos(
            SerializedProperty qosProperty,
            string label)
        {
            if (qosProperty == null)
                return;

            var profileProperty = qosProperty.FindPropertyRelative("_profile");
            var overrideReliability = qosProperty.FindPropertyRelative("_overrideReliability");
            var reliabilityProperty = qosProperty.FindPropertyRelative("_reliability");
            var overrideDurability = qosProperty.FindPropertyRelative("_overrideDurability");
            var durabilityProperty = qosProperty.FindPropertyRelative("_durability");
            var overrideHistory = qosProperty.FindPropertyRelative("_overrideHistory");
            var historyProperty = qosProperty.FindPropertyRelative("_history");
            var overrideDepth = qosProperty.FindPropertyRelative("_overrideDepth");
            var depthProperty = qosProperty.FindPropertyRelative("_depth");
            if (profileProperty == null
                || overrideReliability == null
                || reliabilityProperty == null
                || overrideDurability == null
                || durabilityProperty == null
                || overrideHistory == null
                || historyProperty == null
                || overrideDepth == null
                || depthProperty == null)
            {
                DrawMissingProperty(qosProperty.propertyPath);
                return;
            }

            var choices = FoxRunRos2SubscriptionInspectorPresentation.ManagerQosChoices;
            var normalizedProfile = (FoxRunQosProfile)profileProperty.intValue;
            if (normalizedProfile != FoxRunQosProfile.Default
                && normalizedProfile != FoxRunQosProfile.SensorData
                && normalizedProfile != FoxRunQosProfile.SystemDefault)
            {
                normalizedProfile = FoxRunQosProfile.Default;
                profileProperty.intValue = (int)normalizedProfile;
            }

            var selectedIndex = 0;
            for (var i = 0; i < choices.Count; i++)
            {
                if (choices[i].Profile == normalizedProfile)
                    selectedIndex = i;
            }

            var changedIndex = EditorGUILayout.Popup(
                label,
                selectedIndex,
                FoxRunRos2SubscriptionInspectorPresentation.ManagerQosLabels);
            var selectedChoice = choices[changedIndex];
            if (selectedChoice.Profile != normalizedProfile)
                profileProperty.intValue = (int)selectedChoice.Profile;

            qosProperty.isExpanded = EditorGUILayout.Foldout(
                qosProperty.isExpanded,
                "Advanced Overrides",
                toggleOnLabelClick: true);
            if (qosProperty.isExpanded)
            {
                DrawQosOverride(
                    overrideReliability,
                    reliabilityProperty,
                    "Reliability");
                DrawQosOverride(
                    overrideDurability,
                    durabilityProperty,
                    "Durability");
                DrawQosOverride(
                    overrideHistory,
                    historyProperty,
                    "History");
                DrawQosOverride(
                    overrideDepth,
                    depthProperty,
                    "Depth");
            }

            var resolution = FoxRunRos2QosProfileResolver.Resolve(
                selectedChoice.Profile,
                hasProfile: true,
                (FoxRunQosReliability)reliabilityProperty.intValue,
                overrideReliability.boolValue,
                (FoxRunQosDurability)durabilityProperty.intValue,
                overrideDurability.boolValue,
                (FoxRunQosHistory)historyProperty.intValue,
                overrideHistory.boolValue,
                depthProperty.intValue,
                overrideDepth.boolValue,
                FoxRunResolvedQos.Default);
            EditorGUILayout.HelpBox(
                resolution.Success
                    ? FoxRunRos2SubscriptionInspectorPresentation.Summary(resolution.Qos)
                    : resolution.DiagnosticMessage,
                resolution.Success ? MessageType.Info : MessageType.Error);
        }

        private static void DrawQosOverride(
            SerializedProperty enabledProperty,
            SerializedProperty valueProperty,
            string label)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                enabledProperty.boolValue = EditorGUILayout.Toggle(
                    enabledProperty.boolValue,
                    GUILayout.Width(18f));
                using (new EditorGUI.DisabledScope(!enabledProperty.boolValue))
                    EditorGUILayout.PropertyField(valueProperty, new GUIContent(label));
            }
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
                FoxRunRos2SubscriptionInspectorPresentation.NativeCopyBudgetLabels);
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
                "Native Copied-Message Budget ("
                + FoxRunRos2SubscriptionInspectorPresentation.NativeCopyBudgetLabels[(int)displayUnit]
                + ")",
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
                displayValue.ToString("0.######", CultureInfo.InvariantCulture)
                + " " + FoxRunRos2SubscriptionInspectorPresentation.NativeCopyBudgetLabels[(int)displayUnit]
                + " = " + normalizedBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
        }

        private static FoxRunRos2NativeCopyBudgetUnit GetNativeCopyBudgetDisplayUnit()
        {
            var storedUnit = SessionState.GetInt(
                InspectorFoldoutKey(NativeCopyBudgetUnitSessionStateName),
                (int)FoxRunRos2NativeCopyBudgetUnit.MB);
            return storedUnit == (int)FoxRunRos2NativeCopyBudgetUnit.KB
                ? FoxRunRos2NativeCopyBudgetUnit.KB
                : FoxRunRos2NativeCopyBudgetUnit.MB;
        }

        private static bool HasExplicitSource(FoxRunEndpoint provider)
            => HasGeneratedExplicitSource(provider);
    }
}
