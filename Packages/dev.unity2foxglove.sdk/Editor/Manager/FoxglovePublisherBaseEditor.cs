// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Shared custom Inspector for Foxglove publisher components.

using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Custom Inspector for publisher components. Draws the normal serialized
    /// fields plus a read-only encoding summary resolved from manager policy
    /// and publisher capabilities.
    /// </summary>
    [CustomEditor(typeof(Components.FoxglovePublisherBase), true)]
    public class FoxglovePublisherBaseEditor : UnityEditor.Editor
    {
        /// <summary>
        /// Draws the shared publisher inspector, including encoding override
        /// controls and inherited serialized fields.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var publishRateSource = serializedObject.FindProperty("_publishRateSource");
            var publishRateHz = serializedObject.FindProperty("_publishRateHz");
            var encodingOverride = serializedObject.FindProperty("_encodingOverride");
            var bridgeOverride = serializedObject.FindProperty("_ros2BridgeOutput");
            var bridgeTopicOverride = serializedObject.FindProperty("_ros2BridgeTopicOverride");
            var prop = serializedObject.GetIterator();
            if (prop.NextVisible(true))
            {
                do
                {
                    if (prop.name == "_publishRateSource")
                        continue;
                    if (prop.name == "_publishRateHz")
                        continue;
                    if (prop.name == "_encodingOverride")
                        continue;
                    if (prop.name == "_ros2BridgeOutput")
                        continue;
                    if (prop.name == "_ros2BridgeTopicOverride")
                        continue;

                    using (new EditorGUI.DisabledScope(prop.propertyPath == "m_Script"))
                    {
                        EditorGUILayout.PropertyField(prop, true);
                    }
                }
                while (prop.NextVisible(false));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish Rate", EditorStyles.boldLabel);
            if (publishRateSource != null)
                EditorGUILayout.PropertyField(publishRateSource, new GUIContent("Publish Rate Source"));

            var usesLocalRate = publishRateSource == null
                || publishRateSource.enumValueIndex == (int)Components.PublisherRateSource.OverrideLocal;
            using (new EditorGUI.DisabledScope(!usesLocalRate))
            {
                if (publishRateHz != null)
                    EditorGUILayout.PropertyField(publishRateHz, new GUIContent("Publish Rate Hz"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encoding Policy", EditorStyles.boldLabel);
            if (encodingOverride != null)
                PublisherEncodingEditorLabels.DrawPublisherOverride(encodingOverride, "Encoding Override");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ROS2 Bridge", EditorStyles.boldLabel);
            if (bridgeOverride != null)
                PublisherEncodingEditorLabels.DrawRos2BridgeOverride(bridgeOverride, "Bridge Output");
            if (bridgeTopicOverride != null)
                EditorGUILayout.PropertyField(bridgeTopicOverride, new GUIContent("Bridge Topic Override"));

            serializedObject.ApplyModifiedProperties();

            var publisher = (Components.FoxglovePublisherBase)target;
            var resolution = publisher.EncodingResolution;
            var bridgeResolution = publisher.BridgeOutputResolution;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("Effective Publish Rate Hz", publisher.EffectivePublishRateHz);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Supported Encodings", publisher.SupportedEncodingSummary);
                PublisherEncodingEditorLabels.DrawEffectiveEncoding(resolution.Effective, "Effective Encoding");
                PublisherEncodingEditorLabels.DrawEffectiveRos2BridgeOutput(bridgeResolution.Effective, "Effective ROS2 Bridge");
                EditorGUILayout.TextField("Effective Bridge Topic", publisher.EffectiveRos2BridgeTopic);
                EditorGUILayout.TextField("Effective Bridge QoS", publisher.EffectiveRos2BridgeQos.DisplaySummary);
            }

            if (publisher.ConfiguredManager != null
                && !publisher.ConfiguredManager.AllowPublisherOverride
                && publisher.EncodingOverride != Components.PublisherEncodingOverride.UseManager)
            {
                EditorGUILayout.HelpBox(
                    "FoxgloveManager disables publisher overrides; the global default is used.",
                    MessageType.Info);
            }
            else if (resolution.Effective == Components.PublisherEffectiveEncoding.Unsupported)
            {
                EditorGUILayout.HelpBox(
                    "This publisher declares no supported encoding and will not publish messages.",
                    MessageType.Error);
            }
            else if (resolution.FellBack)
            {
                EditorGUILayout.HelpBox(
                    $"Requested {resolution.RequestedLabel}, but this publisher will emit {resolution.EffectiveLabel}.",
                    MessageType.Warning);
            }

            if (bridgeResolution.FellBack)
            {
                EditorGUILayout.HelpBox(
                    "Requested ROS2 Bridge output, but this publisher cannot mirror a ROS2 payload.",
                    MessageType.Warning);
            }
        }
    }
}
