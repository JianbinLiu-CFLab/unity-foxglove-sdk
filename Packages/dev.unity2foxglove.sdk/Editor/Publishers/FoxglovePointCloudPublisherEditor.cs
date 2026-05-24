// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Dedicated Inspector for point-cloud publisher output and QoS controls.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Util;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    [CustomEditor(typeof(FoxglovePointCloudPublisher))]
    public class FoxglovePointCloudPublisherEditor : UnityEditor.Editor
    {
        private static readonly string[] PointCloudOutputModeLabels =
        {
            "Raw",
            "Draco"
        };

        private DracoPointCloudNativeCheckResult _dracoCheck =
            new DracoPointCloudNativeCheckResult(DracoPointCloudNativeStatus.NotChecked, "", "", 0);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var topic = serializedObject.FindProperty("_topic");
            var outputMode = serializedObject.FindProperty("_outputMode");

            DrawScriptField();
            DrawOutputModeSection(outputMode, topic);
            DrawGeneralSection();
            DrawPointSourcesSection();
            DrawPointCloudQosSection();

            if (GetMode(outputMode) == PointCloudOutputMode.Draco)
            {
                DrawDracoSection();
            }

            DrawPublishRateSection();
            DrawEncodingPolicySection();
            DrawRos2BridgeSection();

            serializedObject.ApplyModifiedProperties();

            DrawResolvedSummaries();
        }

        private void DrawScriptField()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }
        }

        private void DrawOutputModeSection(SerializedProperty outputMode, SerializedProperty topic)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Point Cloud Output", EditorStyles.boldLabel);

            var oldMode = GetMode(outputMode);
            EditorGUI.BeginChangeCheck();
            DrawPointCloudOutputMode(outputMode);
            if (EditorGUI.EndChangeCheck())
            {
                var newMode = GetMode(outputMode);
                ApplyTopicForModeChange(topic, oldMode, newMode);
                _dracoCheck = new DracoPointCloudNativeCheckResult(DracoPointCloudNativeStatus.NotChecked, "", "", 0);
            }

            var mode = GetMode(outputMode);
            if (mode == PointCloudOutputMode.Raw)
            {
                EditorGUILayout.HelpBox(
                    "Raw mode publishes foxglove.PointCloud and supports JSON or protobuf without external dependencies.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Draco mode publishes foxglove.CompressedPointCloud with format = \"draco\". It supports Protobuf and ROS2 using the bundled Windows native plugin.",
                    MessageType.Info);
            }
        }

        private void DrawGeneralSection()
        {
            EditorGUILayout.Space();
            DrawProperty("_manager", "Manager");
            DrawProperty("_topic", "Topic");
            DrawProperty("_publishOnEnable", "Publish On Enable");
            DrawProperty("_warnIfManagerMissing", "Warn If Manager Missing");
            DrawProperty("_frameId", "Frame Id");
        }

        private void DrawPointSourcesSection()
        {
            EditorGUILayout.Space();
            DrawProperty("_pointSources", "Point Sources");
            DrawProperty("_useChildrenWhenSourcesEmpty", "Use Children When Sources Empty");
            DrawProperty("_includeInactiveChildren", "Include Inactive Children");
            DrawProperty("_includeSyntheticIntensity", "Include Synthetic Intensity");
        }

        private void DrawPointCloudQosSection()
        {
            var samplingMode = serializedObject.FindProperty("_samplingMode");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Point Cloud QoS", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Max Packed Bytes budgets the PointCloud.data payload before publish. Set it to 0 to disable the byte budget. Draco mode uses the same sampled frame before compression.",
                MessageType.Info);
            DrawProperty("_maxPoints", "Max Points");
            DrawProperty("_maxPackedBytes", "Max Packed Bytes");
            EditorGUILayout.PropertyField(samplingMode, new GUIContent("Sampling Mode"));

            if (samplingMode != null && samplingMode.enumValueIndex == (int)PointCloudSamplingMode.VoxelGrid)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_voxelSizeMeters"), new GUIContent("Voxel Size Meters"));
                EditorGUILayout.HelpBox(
                    "VoxelGrid keeps the first source point in each occupied voxel so optional point fields keep their original values.",
                    MessageType.Info);
            }

            DrawProperty("_logQosDrops", "Log QoS Drops");
            EditorGUILayout.HelpBox(
                "Heavy point-cloud work is skipped when there is no live subscriber or active MCAP recording demand.",
                MessageType.Info);
        }

        private void DrawDracoSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Draco", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Draco mode uses the bundled Windows native plugin Unity2FoxgloveDracoNative.dll. No helper executable or PATH setup is required.",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Phase 89 uses synchronous native encode. Keep point-cloud QoS budgets realistic and use Raw mode on unsupported platforms.",
                MessageType.Info);

            var checkRequested = false;
            var helpRequested = false;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Check Draco"))
                    checkRequested = true;
            }

            if (checkRequested)
                _dracoCheck = DracoPointCloudNativeCheck.Check();

            DrawDracoStatus();

            helpRequested = GUILayout.Button("Draco Help...");
            if (helpRequested)
                DracoHelpWindow.ShowWindow();
        }

        private void DrawPublishRateSection()
        {
            var publishRateSource = serializedObject.FindProperty("_publishRateSource");
            var publishRateHz = serializedObject.FindProperty("_publishRateHz");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish Rate", EditorStyles.boldLabel);
            if (publishRateSource != null)
                EditorGUILayout.PropertyField(publishRateSource, new GUIContent("Publish Rate Source"));

            var usesLocalRate = publishRateSource == null
                || publishRateSource.enumValueIndex == (int)PublisherRateSource.OverrideLocal;
            using (new EditorGUI.DisabledScope(!usesLocalRate))
            {
                if (publishRateHz != null)
                    EditorGUILayout.PropertyField(publishRateHz, new GUIContent("Publish Rate Hz"));
            }
        }

        private void DrawEncodingPolicySection()
        {
            var encodingOverride = serializedObject.FindProperty("_encodingOverride");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encoding Policy", EditorStyles.boldLabel);
            if (encodingOverride != null)
                PublisherEncodingEditorLabels.DrawPublisherOverride(encodingOverride, "Encoding Override");
        }

        private void DrawRos2BridgeSection()
        {
            var bridgeOutput = serializedObject.FindProperty("_ros2BridgeOutput");
            var bridgeTopicOverride = serializedObject.FindProperty("_ros2BridgeTopicOverride");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ROS2 Bridge", EditorStyles.boldLabel);
            if (bridgeOutput != null)
                PublisherEncodingEditorLabels.DrawRos2BridgeOverride(bridgeOutput, "Bridge Output");
            if (bridgeTopicOverride != null)
                EditorGUILayout.PropertyField(bridgeTopicOverride, new GUIContent("Bridge Topic Override"));
            EditorGUILayout.HelpBox(
                "Raw and Draco point clouds can mirror their ROS2 CDR payloads to the optional local bridge after the same QoS sampling step.",
                MessageType.Info);
        }

        private void DrawResolvedSummaries()
        {
            var publisher = (FoxglovePublisherBase)target;
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
                && publisher.EncodingOverride != PublisherEncodingOverride.UseManager)
            {
                EditorGUILayout.HelpBox(
                    "FoxgloveManager disables publisher overrides; the global default is used.",
                    MessageType.Info);
            }
            else if (resolution.Effective == PublisherEffectiveEncoding.Unsupported)
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
                    "Requested ROS2 Bridge output, but this point-cloud mode cannot mirror a ROS2 payload.",
                    MessageType.Warning);
            }
        }

        private static void DrawPointCloudOutputMode(SerializedProperty outputMode)
        {
            if (outputMode == null)
                return;

            var currentIndex = outputMode.enumValueIndex;
            if (currentIndex < 0 || currentIndex >= PointCloudOutputModeLabels.Length)
                currentIndex = 0;

            outputMode.enumValueIndex = EditorGUILayout.Popup("Point Cloud Output Mode", currentIndex, PointCloudOutputModeLabels);
        }

        private static PointCloudOutputMode GetMode(SerializedProperty outputMode)
            => outputMode == null ? PointCloudOutputMode.Raw : (PointCloudOutputMode)outputMode.enumValueIndex;

        private static void ApplyTopicForModeChange(SerializedProperty topic, PointCloudOutputMode oldMode, PointCloudOutputMode newMode)
        {
            if (topic == null || oldMode == newMode)
                return;

            var oldDefault = PointCloudOutputProfile.ForMode(oldMode).DefaultTopic;
            var newDefault = PointCloudOutputProfile.ForMode(newMode).DefaultTopic;
            if (string.IsNullOrEmpty(topic.stringValue) || topic.stringValue == oldDefault)
                topic.stringValue = newDefault;
        }

        private void DrawDracoStatus()
        {
            switch (_dracoCheck.Status)
            {
                case DracoPointCloudNativeStatus.Available:
                    var foundMessage = "Available: bundled Windows native Draco plugin validated with a tiny XYZ encode.";
                    if (!string.IsNullOrEmpty(_dracoCheck.Version))
                        foundMessage += "\nVersion: " + _dracoCheck.Version;
                    if (_dracoCheck.PayloadBytes > 0)
                        foundMessage += "\nPayload Bytes: " + _dracoCheck.PayloadBytes;
                    EditorGUILayout.HelpBox(foundMessage, MessageType.Info);
                    break;
                case DracoPointCloudNativeStatus.Missing:
                    EditorGUILayout.HelpBox(
                        string.IsNullOrEmpty(_dracoCheck.ErrorMessage)
                            ? "Bundled Windows native Draco plugin was not found."
                            : _dracoCheck.ErrorMessage,
                        MessageType.Warning);
                    break;
                case DracoPointCloudNativeStatus.Invalid:
                    EditorGUILayout.HelpBox(
                        string.IsNullOrEmpty(_dracoCheck.ErrorMessage)
                            ? "Native Draco plugin validation failed."
                            : _dracoCheck.ErrorMessage,
                        MessageType.Error);
                    break;
                case DracoPointCloudNativeStatus.NotChecked:
                default:
                    EditorGUILayout.HelpBox("Status: Not Checked\nPlugin: Unity2FoxgloveDracoNative.dll", MessageType.None);
                    break;
            }
        }

        private void DrawProperty(string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(label), true);
        }

        private sealed class DracoHelpWindow : EditorWindow
        {
            private Vector2 _scroll;

            public static void ShowWindow()
            {
                var window = CreateInstance<DracoHelpWindow>();
                window.titleContent = new GUIContent("Draco PointCloud Setup");
                window.minSize = new Vector2(560, 320);
                window.ShowUtility();
            }

            private void OnGUI()
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                EditorGUILayout.LabelField("Draco mode uses a bundled native plugin.", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Unity2FoxgloveDracoNative.dll encodes sampled XYZ point frames into Draco payloads for foxglove.CompressedPointCloud with format = \"draco\".",
                    MessageType.Info);
                EditorGUILayout.HelpBox(
                    "The Windows plugin is bundled in the SDK package. Google Draco remains Apache-2.0; update third-party notices whenever the native plugin is rebuilt from a new Draco commit.",
                    MessageType.Warning);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("1. Click Check Draco to run a tiny native XYZ encode smoke.");
                EditorGUILayout.LabelField("2. Use Raw mode for dependency-free or unsupported-platform point clouds.");
                EditorGUILayout.LabelField("3. Rebuild the native plugin only when changing the bundled Draco version.");

                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Google Draco"))
                        Application.OpenURL("https://github.com/google/draco");

                    if (GUILayout.Button("Close"))
                        Close();
                }
            }
        }
    }
}
