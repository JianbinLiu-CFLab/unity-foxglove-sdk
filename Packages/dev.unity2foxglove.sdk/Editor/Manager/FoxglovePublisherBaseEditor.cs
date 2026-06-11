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
        private static readonly System.Collections.Generic.Dictionary<string, GUIContent> GuiContentCache =
            new System.Collections.Generic.Dictionary<string, GUIContent>(System.StringComparer.Ordinal);

        private readonly System.Collections.Generic.List<SerializedProperty> _defaultProperties =
            new System.Collections.Generic.List<SerializedProperty>();
        private SerializedProperty _publishRateSource;
        private SerializedProperty _publishRateHz;
        private SerializedProperty _encodingOverride;
        private SerializedProperty _bridgeOverride;
        private SerializedProperty _bridgeTopicOverride;
        private SerializedProperty _topic;

        private void OnEnable()
        {
            _publishRateSource = serializedObject.FindProperty("_publishRateSource");
            _publishRateHz = serializedObject.FindProperty("_publishRateHz");
            _encodingOverride = serializedObject.FindProperty("_encodingOverride");
            _bridgeOverride = serializedObject.FindProperty("_ros2BridgeOutput");
            _bridgeTopicOverride = serializedObject.FindProperty("_ros2BridgeTopicOverride");
            _topic = serializedObject.FindProperty("_topic");
            CacheDefaultProperties();
        }

        /// <summary>
        /// Draws the shared publisher inspector, including encoding override
        /// controls and inherited serialized fields.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            foreach (var prop in _defaultProperties)
            {
                using (new EditorGUI.DisabledScope(prop.propertyPath == "m_Script"))
                {
                    EditorGUILayout.PropertyField(prop, true);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish Rate", EditorStyles.boldLabel);
            if (_publishRateSource != null)
                EditorGUILayout.PropertyField(_publishRateSource, Label("Publish Rate Source"));

            var usesLocalRate = _publishRateSource == null
                || _publishRateSource.enumValueIndex == (int)Components.PublisherRateSource.OverrideLocal;
            using (new EditorGUI.DisabledScope(!usesLocalRate))
            {
                if (_publishRateHz != null)
                    EditorGUILayout.PropertyField(_publishRateHz, Label("Publish Rate Hz"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encoding Policy", EditorStyles.boldLabel);
            if (_encodingOverride != null)
                PublisherEncodingEditorLabels.DrawPublisherOverride(_encodingOverride, "Encoding Override");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ROS2 Bridge", EditorStyles.boldLabel);
            if (_bridgeOverride != null)
                PublisherEncodingEditorLabels.DrawRos2BridgeOverride(_bridgeOverride, "Bridge Output");
            if (_bridgeTopicOverride != null)
                EditorGUILayout.PropertyField(_bridgeTopicOverride, Label("Bridge Topic Override"));

            serializedObject.ApplyModifiedProperties();

            var publisher = (Components.FoxglovePublisherBase)target;
            var resolution = publisher.EncodingResolution;
            var bridgeResolution = publisher.BridgeOutputResolution;

            if (_topic != null && !Components.FoxglovePublisherBase.HasValidPublisherTopic(_topic.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "Topic is required. Blank publisher topics are not advertised or published.",
                    MessageType.Error);
            }

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

        private void CacheDefaultProperties()
        {
            _defaultProperties.Clear();
            var prop = serializedObject.GetIterator();
            if (!prop.NextVisible(true))
                return;

            do
            {
                if (ShouldSkipDefaultProperty(prop.name))
                    continue;

                _defaultProperties.Add(prop.Copy());
            }
            while (prop.NextVisible(false));
        }

        private static bool ShouldSkipDefaultProperty(string propertyName)
        {
            return propertyName == "_publishRateSource"
                || propertyName == "_publishRateHz"
                || propertyName == "_encodingOverride"
                || propertyName == "_ros2BridgeOutput"
                || propertyName == "_ros2BridgeTopicOverride";
        }

        private static GUIContent Label(string text)
        {
            if (!GuiContentCache.TryGetValue(text, out var content))
            {
                content = new GUIContent(text);
                GuiContentCache.Add(text, content);
            }

            return content;
        }
    }
}
