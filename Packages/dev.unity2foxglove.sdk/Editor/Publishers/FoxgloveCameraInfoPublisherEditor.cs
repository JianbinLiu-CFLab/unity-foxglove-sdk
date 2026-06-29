// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Inspector for standard sensor_msgs/msg/CameraInfo output.

using Unity.FoxgloveSDK.Components;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Custom inspector for <see cref="FoxgloveCameraInfoPublisher"/>.</summary>
    [CustomEditor(typeof(FoxgloveCameraInfoPublisher))]
    public class FoxgloveCameraInfoPublisherEditor : UnityEditor.Editor
    {
        private static bool _showAdvancedCalibration;
        private static bool _showOptionalTfAnchor;
        private static bool _showAdvancedTransport;
        private static readonly Dictionary<string, System.Type> ObjectFieldTypeCache =
            new Dictionary<string, System.Type>(StringComparer.Ordinal)
            {
                ["_manager"] = typeof(FoxgloveManager),
                ["_sourceCamera"] = typeof(Camera),
                ["_imagePublisher"] = typeof(FoxgloveCameraPublisher),
                ["_sensorUnitProfile"] = typeof(SensorUnitProfile)
            };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Standalone CameraInfo", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            EditorGUILayout.HelpBox(
                "Advanced CameraInfo Publisher for ROS2 calibration streams and compatibility scenes. Most camera users can keep this component disabled or absent unless a ROS2 tool needs sensor_msgs/msg/CameraInfo.",
                MessageType.Info);

            DrawGeneralSection();
            DrawCameraInfoSourceSection();
            DrawAdvancedCalibrationSection();
            DrawOptionalTfAnchorSection();
            DrawAdvancedTransportSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGeneralSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            DrawObjectField(serializedObject.FindProperty("_manager"), "Manager");
            DrawStringField(serializedObject.FindProperty("_topic"), "Topic");
            DrawBoolField(serializedObject.FindProperty("_publishOnEnable"), "Publish On Enable");
            DrawBoolField(serializedObject.FindProperty("_warnIfManagerMissing"), "Warn If Manager Missing");
        }

        private void DrawCameraInfoSourceSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("CameraInfo Source", EditorStyles.boldLabel);
            DrawObjectField(serializedObject.FindProperty("_sourceCamera"), "Source Camera");
            DrawObjectField(serializedObject.FindProperty("_imagePublisher"), "Image Publisher");
            DrawObjectField(serializedObject.FindProperty("_sensorUnitProfile"), "Sensor Unit Profile");
            DrawStringField(serializedObject.FindProperty("_frameId"), "Frame Id");
            DrawBoolField(serializedObject.FindProperty("_useSharedSensorClock"), "Use Shared Sensor Clock");
        }

        private void DrawAdvancedCalibrationSection()
        {
            EditorGUILayout.Space();
            _showAdvancedCalibration = EditorGUILayout.Foldout(_showAdvancedCalibration, "Advanced Camera Calibration", true);
            if (!_showAdvancedCalibration)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                DrawBoolField(serializedObject.FindProperty("_autoFromCamera"), "Auto From Camera");
                DrawIntField(serializedObject.FindProperty("_widthOverride"), "Width Override");
                DrawIntField(serializedObject.FindProperty("_heightOverride"), "Height Override");
                DrawDoubleField(serializedObject.FindProperty("_fxOverride"), "Fx Override");
                DrawDoubleField(serializedObject.FindProperty("_fyOverride"), "Fy Override");
                DrawDoubleField(serializedObject.FindProperty("_cxOverride"), "Cx Override");
                DrawDoubleField(serializedObject.FindProperty("_cyOverride"), "Cy Override");
                DrawStringField(serializedObject.FindProperty("_distortionModel"), "Distortion Model");
            }
        }

        private void DrawOptionalTfAnchorSection()
        {
            EditorGUILayout.Space();
            _showOptionalTfAnchor = EditorGUILayout.Foldout(_showOptionalTfAnchor, "Optional TF Anchor", true);
            if (!_showOptionalTfAnchor)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                var publishCameraTfAnchor = serializedObject.FindProperty("_publishCameraTfAnchor");
                DrawBoolField(publishCameraTfAnchor, "Publish Camera TF Anchor");
                var tfAnchorEnabled = publishCameraTfAnchor != null && publishCameraTfAnchor.boolValue;
                using (new EditorGUI.DisabledScope(!tfAnchorEnabled))
                {
                    DrawStringField(serializedObject.FindProperty("_cameraTfParentFrame"), "TF Parent Frame");
                }

                EditorGUILayout.HelpBox(
                    "Enable only when no scene, robot, or SLAM TF tree already resolves the CameraInfo frame.",
                    MessageType.None);
            }
        }

        private void DrawAdvancedTransportSection()
        {
            EditorGUILayout.Space();
            _showAdvancedTransport = EditorGUILayout.Foldout(_showAdvancedTransport, "Advanced Transport", true);
            if (!_showAdvancedTransport)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                var publishRateSource = serializedObject.FindProperty("_publishRateSource");
                DrawEnumField(publishRateSource, "Publish Rate Source");
                var usesLocalRate = publishRateSource == null
                    || publishRateSource.enumValueIndex == (int)PublisherRateSource.OverrideLocal;
                using (new EditorGUI.DisabledScope(!usesLocalRate))
                {
                    DrawFloatField(serializedObject.FindProperty("_publishRateHz"), "Publish Rate Hz");
                }

                DrawEnumField(serializedObject.FindProperty("_encodingOverride"), "Encoding Override");
                DrawEnumField(serializedObject.FindProperty("_ros2BridgeOutput"), "ROS2 Bridge Output");
                DrawStringField(serializedObject.FindProperty("_ros2BridgeTopicOverride"), "Bridge Topic Override");
            }
        }

        private static void DrawObjectField(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            property.objectReferenceValue = EditorGUILayout.ObjectField(
                label,
                property.objectReferenceValue,
                GetObjectFieldType(property),
                allowSceneObjects: true);
        }

        private static void DrawStringField(SerializedProperty property, string label)
        {
            if (property != null)
                property.stringValue = EditorGUILayout.TextField(label, property.stringValue);
        }

        private static void DrawBoolField(SerializedProperty property, string label)
        {
            if (property != null)
                property.boolValue = EditorGUILayout.Toggle(label, property.boolValue);
        }

        private static void DrawIntField(SerializedProperty property, string label)
        {
            if (property != null)
                property.intValue = EditorGUILayout.IntField(label, property.intValue);
        }

        private static void DrawFloatField(SerializedProperty property, string label)
        {
            if (property != null)
                property.floatValue = EditorGUILayout.FloatField(label, property.floatValue);
        }

        private static void DrawDoubleField(SerializedProperty property, string label)
        {
            if (property != null)
                property.doubleValue = EditorGUILayout.DoubleField(label, property.doubleValue);
        }

        private static void DrawEnumField(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var index = property.enumValueIndex;
            if (index < 0 || index >= property.enumDisplayNames.Length)
                index = 0;

            property.enumValueIndex = EditorGUILayout.Popup(label, index, property.enumDisplayNames);
        }

        private static System.Type GetObjectFieldType(SerializedProperty property)
        {
            if (ObjectFieldTypeCache.TryGetValue(property.name, out var knownType))
                return knownType;

            var typeName = property.type;
            if (typeName.StartsWith("PPtr<$", System.StringComparison.Ordinal)
                && typeName.EndsWith(">", System.StringComparison.Ordinal))
            {
                typeName = typeName.Substring(6, typeName.Length - 7);
            }

            if (ObjectFieldTypeCache.TryGetValue(typeName, out var cachedType))
                return cachedType;

            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    ObjectFieldTypeCache[typeName] = type;
                    return type;
                }
            }

            Debug.LogWarning(
                $"[Foxglove] CameraInfo Inspector could not resolve ObjectField type '{typeName}' for '{property.name}'; using UnityEngine.Object fallback.");
            ObjectFieldTypeCache[typeName] = typeof(UnityEngine.Object);
            return typeof(UnityEngine.Object);
        }
    }
}
