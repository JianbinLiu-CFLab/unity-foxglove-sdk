// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Sensors
// Purpose: Focused Inspector for Virtual IMU output and advanced sensor model settings.

using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Custom Inspector for VirtualImu. Keeps the common setup path small and
    /// moves calibration/noise-model details behind an advanced foldout.
    /// </summary>
    [CustomEditor(typeof(VirtualImu))]
    public sealed class VirtualImuEditor : UnityEditor.Editor
    {
        private static readonly string[] PublishRateSourceLabels =
        {
            "Use Manager Default",
            "Override Local"
        };

        private static bool _showAdvancedImuModel;

        /// <summary>
        /// Draws VirtualImu settings with the same rate-source mental model used
        /// by publishers while preserving the IMU-specific 200Hz local default.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptField();
            DrawImuOutputSection();
            DrawPublishRateSection();
            DrawAdvancedImuModelSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptField()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }
        }

        private void DrawImuOutputSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("IMU Output", EditorStyles.boldLabel);
            DrawProperty("_manager", "Manager");
            DrawProperty("_rigidbody", "Rigidbody");
            DrawProperty("_topic", "Topic");
            DrawProperty("_frameId", "Frame Id");
        }

        private void DrawPublishRateSection()
        {
            var source = serializedObject.FindProperty("_publishRateSource");
            var targetRateHz = serializedObject.FindProperty("_targetRateHz");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish Rate", EditorStyles.boldLabel);

            if (source != null)
            {
                source.enumValueIndex = EditorGUILayout.Popup(
                    "Publish Rate Source",
                    source.enumValueIndex,
                    PublishRateSourceLabels);
            }

            var usesLocalRate = source == null
                                || source.enumValueIndex == (int)PublisherRateSource.OverrideLocal;
            using (new EditorGUI.DisabledScope(!usesLocalRate))
            {
                if (targetRateHz != null)
                    EditorGUILayout.PropertyField(targetRateHz, new GUIContent("Sample Rate Hz"));
            }

            EditorGUILayout.HelpBox(
                "IMU defaults to a local 200Hz sample rate. Manager defaults are usually tuned for lower-rate visual topics.",
                MessageType.Info);
        }

        private void DrawAdvancedImuModelSection()
        {
            EditorGUILayout.Space();
            _showAdvancedImuModel = EditorGUILayout.Foldout(_showAdvancedImuModel, "Advanced IMU Model", true);
            if (!_showAdvancedImuModel)
                return;

            DrawProperty("_includeOrientation", "Publish Orientation");
            DrawProperty("_imuOrientationCovariance", "Orientation Covariance");
            DrawProperty("_imuAngularVelocityCovariance", "Angular Velocity Covariance");
            DrawProperty("_imuLinearAccelerationCovariance", "Linear Acceleration Covariance");

            EditorGUILayout.Space();
            DrawProperty("_globalPhysicsRateHzOverride", "Override Unity Physics Rate Hz");
            EditorGUILayout.HelpBox(
                "Leave at 0 for normal use. A positive value changes Unity Time.fixedDeltaTime globally and can affect physics, vehicle control, and performance.",
                MessageType.Warning);
        }

        private void DrawProperty(string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
    }
}
