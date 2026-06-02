// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Inspector for standard sensor_msgs/msg/CameraInfo output.

using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Custom inspector for <see cref="FoxgloveCameraInfoPublisher"/>.</summary>
    [CustomEditor(typeof(FoxgloveCameraInfoPublisher))]
    public class FoxgloveCameraInfoPublisherEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Sensor CameraInfo", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            DrawGeneralSection();
            DrawSensorCameraInfoSection();
            DrawPublishRateSection();
            DrawEncodingSection();
            DrawRos2BridgeSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGeneralSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_manager"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_topic"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_publishOnEnable"), new GUIContent("Publish On Enable"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_warnIfManagerMissing"), new GUIContent("Warn If Manager Missing"));
        }

        private void DrawSensorCameraInfoSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sensor CameraInfo", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_sourceCamera"), new GUIContent("Source Camera"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_imagePublisher"), new GUIContent("Image Publisher"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_sensorUnitProfile"), new GUIContent("Sensor Unit Profile"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_frameId"), new GUIContent("Frame Id"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_useSharedSensorClock"), new GUIContent("Use Shared Sensor Clock"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_publishCameraTfAnchor"), new GUIContent("Publish Camera TF Anchor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_cameraTfParentFrame"), new GUIContent("TF Parent Frame"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_autoFromCamera"), new GUIContent("Auto From Camera"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_widthOverride"), new GUIContent("Width Override"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_heightOverride"), new GUIContent("Height Override"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_fxOverride"), new GUIContent("Fx Override"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_fyOverride"), new GUIContent("Fy Override"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_cxOverride"), new GUIContent("Cx Override"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_cyOverride"), new GUIContent("Cy Override"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_distortionModel"), new GUIContent("Distortion Model"));
        }

        private void DrawPublishRateSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish Rate", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_publishRateSource"), new GUIContent("Publish Rate Source"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_publishRateHz"), new GUIContent("Publish Rate Hz"));
        }

        private void DrawEncodingSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encoding", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_encodingOverride"), new GUIContent("Encoding Override"));
        }

        private void DrawRos2BridgeSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ROS2 Bridge", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_ros2BridgeOutput"), new GUIContent("ROS2 Bridge Output"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_ros2BridgeTopicOverride"), new GUIContent("Bridge Topic Override"));
        }
    }
}
