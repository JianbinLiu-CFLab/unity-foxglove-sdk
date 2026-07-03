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
    /// <summary>
    /// Custom inspector for point-cloud publish settings, including Draco checks and
    /// transport mode hints.
    /// </summary>
    [CustomEditor(typeof(FoxglovePointCloudPublisher))]
    public class FoxglovePointCloudPublisherEditor : UnityEditor.Editor
    {
        private static readonly string[] PointCloudOutputModeLabels =
        {
            "Raw",
            "Draco",
            "PointCloud2 Native"
        };

        private DracoPointCloudNativeCheckResult _dracoCheck =
            new DracoPointCloudNativeCheckResult(DracoPointCloudNativeStatus.NotChecked, "", "", 0);
        private SerializedProperty _script;
        private SerializedProperty _manager;
        private SerializedProperty _topic;
        private SerializedProperty _outputMode;
        private SerializedProperty _publishOnEnable;
        private SerializedProperty _warnIfManagerMissing;
        private SerializedProperty _frameId;
        private SerializedProperty _publishPointCloud2NativeTfAnchor;
        private SerializedProperty _pointCloud2NativeTfParentFrame;
        private SerializedProperty _pointCloud2NativeTfChildFrame;
        private SerializedProperty _pointCloud2NativeTfTranslation;
        private SerializedProperty _pointCloud2NativeTfRotationEuler;
        private SerializedProperty _enableMotionCompensation;
        private SerializedProperty _motionCompensationOutputPolicy;
        private SerializedProperty _deskewedPointCloud2NativeTopic;
        private SerializedProperty _deskewedPointCloud2NativeMaxPublishRateHz;
        private SerializedProperty _motionCompensationReferenceTime;
        private SerializedProperty _motionCompensationSource;
        private SerializedProperty _samplingMode;
        private SerializedProperty _maxPoints;
        private SerializedProperty _maxPackedBytes;
        private SerializedProperty _voxelSizeMeters;
        private SerializedProperty _logQosDrops;
        private SerializedProperty _logPerformanceDiagnostics;
        private SerializedProperty _nativeDracoMaxPublishRateHz;
        private SerializedProperty _suppressTransformFallbackAfterSourceFrames;
        private SerializedProperty _publishRateSource;
        private SerializedProperty _publishRateHz;
        private SerializedProperty _encodingOverride;
        private SerializedProperty _bridgeOutput;
        private SerializedProperty _bridgeTopicOverride;

        private void OnEnable()
        {
            _script = serializedObject.FindProperty("m_Script");
            _manager = serializedObject.FindProperty("_manager");
            _topic = serializedObject.FindProperty("_topic");
            _outputMode = serializedObject.FindProperty("_outputMode");
            _publishOnEnable = serializedObject.FindProperty("_publishOnEnable");
            _warnIfManagerMissing = serializedObject.FindProperty("_warnIfManagerMissing");
            _frameId = serializedObject.FindProperty("_frameId");
            _publishPointCloud2NativeTfAnchor = serializedObject.FindProperty("_publishPointCloud2NativeTfAnchor");
            _pointCloud2NativeTfParentFrame = serializedObject.FindProperty("_pointCloud2NativeTfParentFrame");
            _pointCloud2NativeTfChildFrame = serializedObject.FindProperty("_pointCloud2NativeTfChildFrame");
            _pointCloud2NativeTfTranslation = serializedObject.FindProperty("_pointCloud2NativeTfTranslation");
            _pointCloud2NativeTfRotationEuler = serializedObject.FindProperty("_pointCloud2NativeTfRotationEuler");
            _enableMotionCompensation = serializedObject.FindProperty("_enableMotionCompensation");
            _motionCompensationOutputPolicy = serializedObject.FindProperty("_motionCompensationOutputPolicy");
            _deskewedPointCloud2NativeTopic = serializedObject.FindProperty("_deskewedPointCloud2NativeTopic");
            _deskewedPointCloud2NativeMaxPublishRateHz = serializedObject.FindProperty("_deskewedPointCloud2NativeMaxPublishRateHz");
            _motionCompensationReferenceTime = serializedObject.FindProperty("_motionCompensationReferenceTime");
            _motionCompensationSource = serializedObject.FindProperty("_motionCompensationSource");
            _samplingMode = serializedObject.FindProperty("_samplingMode");
            _maxPoints = serializedObject.FindProperty("_maxPoints");
            _maxPackedBytes = serializedObject.FindProperty("_maxPackedBytes");
            _voxelSizeMeters = serializedObject.FindProperty("_voxelSizeMeters");
            _logQosDrops = serializedObject.FindProperty("_logQosDrops");
            _logPerformanceDiagnostics = serializedObject.FindProperty("_logPerformanceDiagnostics");
            _nativeDracoMaxPublishRateHz = serializedObject.FindProperty("_nativeDracoMaxPublishRateHz");
            _suppressTransformFallbackAfterSourceFrames = serializedObject.FindProperty("_suppressTransformFallbackAfterSourceFrames");
            _publishRateSource = serializedObject.FindProperty("_publishRateSource");
            _publishRateHz = serializedObject.FindProperty("_publishRateHz");
            _encodingOverride = serializedObject.FindProperty("_encodingOverride");
            _bridgeOutput = serializedObject.FindProperty("_ros2BridgeOutput");
            _bridgeTopicOverride = serializedObject.FindProperty("_ros2BridgeTopicOverride");
        }

        /// <summary>
        /// Draws the PointCloud publisher inspector and switches the visible
        /// controls to match the selected output mode.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptField();
            DrawOutputModeSection(_outputMode, _topic);
            DrawGeneralSection();
            if (GetMode(_outputMode) == PointCloudOutputMode.PointCloud2Native)
            {
                DrawMotionCompensationSection();
            }

            DrawPointCloudQosSection();

            if (GetMode(_outputMode) == PointCloudOutputMode.Draco)
            {
                DrawDracoSection();
            }

            DrawPublishRateSection();
            DrawEncodingPolicySection();
            DrawRos2BridgeSection();
            if (GetMode(_outputMode) == PointCloudOutputMode.PointCloud2Native)
            {
                DrawPointCloud2NativeTfAnchorSection();
            }

            serializedObject.ApplyModifiedProperties();

            DrawResolvedSummaries();
        }

        private void DrawScriptField()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                if (_script != null)
                    EditorGUILayout.PropertyField(_script);
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
            else if (mode == PointCloudOutputMode.PointCloud2Native)
            {
                EditorGUILayout.HelpBox(
                    "PointCloud2 Native mode publishes standard sensor_msgs/msg/PointCloud2 as ROS2 CDR for SLAM consumers. Virtual LiDAR source frames use a native snapshot path and background CDR packing.",
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
            DrawProperty(_manager, "Manager");
            DrawProperty(_topic, "Topic");
            DrawProperty(_publishOnEnable, "Publish On Enable");
            DrawProperty(_warnIfManagerMissing, "Warn If Manager Missing");
            DrawProperty(_frameId, "Frame Id");
        }

        private void DrawPointCloud2NativeTfAnchorSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Optional TF Anchor", EditorStyles.boldLabel);
            DrawProperty(_publishPointCloud2NativeTfAnchor, "Publish PointCloud2 TF Anchor");

            if (_publishPointCloud2NativeTfAnchor != null && _publishPointCloud2NativeTfAnchor.boolValue)
            {
                DrawProperty(_pointCloud2NativeTfParentFrame, "TF Parent Frame");
                DrawProperty(_pointCloud2NativeTfChildFrame, "TF Child Frame");
                DrawProperty(_pointCloud2NativeTfTranslation, "TF Translation");
                DrawProperty(_pointCloud2NativeTfRotationEuler, "TF Rotation Euler");
            }

            EditorGUILayout.HelpBox(
                "Off by default. Enable only as an RViz fallback when no other /tf source connects the fixed frame to this PointCloud2 Frame Id.",
                MessageType.Info);
        }

        private void DrawMotionCompensationSection()
        {
            EditorGUILayout.Space();
            DrawProperty(_enableMotionCompensation, "Enable Deskew");

            using (new EditorGUI.DisabledScope(_enableMotionCompensation == null || !_enableMotionCompensation.boolValue))
            {
                DrawProperty(_motionCompensationOutputPolicy, "Output Policy");
                DrawProperty(_deskewedPointCloud2NativeTopic, "Deskewed Topic");
                DrawProperty(_deskewedPointCloud2NativeMaxPublishRateHz, "Deskewed Max Rate Hz");
                DrawProperty(_motionCompensationReferenceTime, "Reference Time");
                DrawProperty(_motionCompensationSource, "Motion Source");
            }

            EditorGUILayout.HelpBox(
                "Deskew is a visualization/output transform. Keep raw rolling PointCloud2 as the input for SLAM front ends such as FAST-LIO2 or LIVO2 that deskew from IMU and per-point time.",
                MessageType.Warning);
        }

        private void DrawPointCloudQosSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Point Budget", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Optional visualization budget for legacy/managed point-cloud frames. Set Max Packed Bytes to 0 to disable the byte budget.",
                MessageType.Info);
            DrawProperty(_maxPoints, "Max Points");
            DrawProperty(_maxPackedBytes, "Max Packed Bytes");
            EditorGUILayout.PropertyField(_samplingMode, new GUIContent("Sampling Mode"));

            if (_samplingMode != null && _samplingMode.enumValueIndex == (int)PointCloudSamplingMode.VoxelGrid)
            {
                EditorGUILayout.PropertyField(_voxelSizeMeters, new GUIContent("Voxel Size Meters"));
                EditorGUILayout.HelpBox(
                    "VoxelGrid keeps the first source point in each occupied voxel so optional point fields keep their original values.",
                    MessageType.Info);
            }

            DrawProperty(_logQosDrops, "Log Budget Drops");
            DrawProperty(_logPerformanceDiagnostics, "Log Performance Diagnostics");
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
                "Native Draco encode runs on a worker thread. Managed frames still pass through QoS before encoding; Virtual LiDAR native snapshots can bypass that managed point append path.",
                MessageType.Info);
            DrawProperty(_nativeDracoMaxPublishRateHz, "Native LiDAR Max Rate Hz");
            DrawProperty(_suppressTransformFallbackAfterSourceFrames, "Suppress Transform Fallback After Source");
            EditorGUILayout.HelpBox(
                "Virtual LiDAR can hand full-resolution Draco snapshots directly to the worker, bypassing the regular Update publish gate. Keep a positive Max Rate for responsive Foxglove visualization; set 0 only when you explicitly want every completed source scan. Source-driven publishers suppress transform fallback frames so sparse child-transform points cannot overwrite real LiDAR clouds.",
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
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish Rate", EditorStyles.boldLabel);
            if (_publishRateSource != null)
                EditorGUILayout.PropertyField(_publishRateSource, new GUIContent("Publish Rate Source"));

            var usesLocalRate = _publishRateSource == null
                || _publishRateSource.enumValueIndex == (int)PublisherRateSource.OverrideLocal;
            using (new EditorGUI.DisabledScope(!usesLocalRate))
            {
                if (_publishRateHz != null)
                    EditorGUILayout.PropertyField(_publishRateHz, new GUIContent("Publish Rate Hz"));
            }
        }

        private void DrawEncodingPolicySection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encoding Policy", EditorStyles.boldLabel);
            if (_encodingOverride != null)
                PublisherEncodingEditorLabels.DrawPublisherOverride(_encodingOverride, "Encoding Override");
        }

        private void DrawRos2BridgeSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ROS2 Bridge", EditorStyles.boldLabel);
            if (_bridgeOutput != null)
                PublisherEncodingEditorLabels.DrawRos2BridgeOverride(_bridgeOutput, "Bridge Output");
            if (_bridgeTopicOverride != null)
                EditorGUILayout.PropertyField(_bridgeTopicOverride, new GUIContent("Bridge Topic Override"));
            EditorGUILayout.HelpBox(
                "Raw, Draco, and PointCloud2 Native point clouds can mirror their ROS2 CDR payloads to the optional local bridge after the same source-frame timing step.",
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
            {
                EditorGUILayout.HelpBox(
                    "Point cloud output mode is outside the supported enum range. Update the SDK Inspector labels before editing this value.",
                    MessageType.Error);
                EditorGUILayout.Popup("Point Cloud Output Mode", 0, PointCloudOutputModeLabels);
                return;
            }

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

        private void DrawProperty(SerializedProperty property, string label)
        {
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(label), true);
        }

        private sealed class DracoHelpWindow : EditorWindow
        {
            private Vector2 _scroll;

            /// <summary>
            /// Opens the Draco help popup without changing the publisher state.
            /// </summary>
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
