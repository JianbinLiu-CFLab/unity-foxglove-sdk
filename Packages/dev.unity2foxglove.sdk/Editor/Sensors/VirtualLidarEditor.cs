// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Sensors

using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Custom inspector for LiDAR scan behavior on the os_lidar frame.</summary>
    [CustomEditor(typeof(VirtualLidar))]
    public class VirtualLidarEditor : UnityEditor.Editor
    {
        private SerializedProperty _sensorUnitProfile;
        private SerializedProperty _frameId, _columnStep, _maxRaysPerScan, _maxRangeMeters,
            _scanSubSteps, _scanRateSource, _scanRateHzOverride,
            _layerMask, _publishEmptyFrames, _drawDebugRays;
        private SerializedProperty _syntheticReflectivity, _syntheticIntensity;

        private void OnEnable()
        {
            _sensorUnitProfile = serializedObject.FindProperty("_sensorUnitProfile");
            _frameId = serializedObject.FindProperty("_frameId");
            _columnStep = serializedObject.FindProperty("_columnStep");
            _maxRaysPerScan = serializedObject.FindProperty("_maxRaysPerScan");
            _maxRangeMeters = serializedObject.FindProperty("_maxRangeMeters");
            _scanSubSteps = serializedObject.FindProperty("_scanSubSteps");
            _scanRateSource = serializedObject.FindProperty("_scanRateSource");
            _scanRateHzOverride = serializedObject.FindProperty("_scanRateHzOverride");
            _layerMask = serializedObject.FindProperty("_layerMask");
            _publishEmptyFrames = serializedObject.FindProperty("_publishEmptyFrames");
            _drawDebugRays = serializedObject.FindProperty("_drawDebugRays");
            _syntheticReflectivity = serializedObject.FindProperty("_syntheticReflectivity");
            _syntheticIntensity = serializedObject.FindProperty("_syntheticIntensity");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_sensorUnitProfile, new GUIContent("Sensor Unit Profile"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scan", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_frameId);
            EditorGUILayout.PropertyField(_columnStep);
            EditorGUILayout.PropertyField(_scanSubSteps);
            EditorGUILayout.PropertyField(_maxRaysPerScan);
            EditorGUILayout.PropertyField(_maxRangeMeters);
            EditorGUILayout.PropertyField(_scanRateSource, new GUIContent("Scan Rate Source"));
            using (new EditorGUI.DisabledScope(
                _scanRateSource.enumValueIndex != (int)VirtualLidar.ScanRateSource.Override))
            {
                EditorGUILayout.PropertyField(_scanRateHzOverride, new GUIContent("Scan Rate Hz"));
            }

            EditorGUILayout.PropertyField(_layerMask);
            EditorGUILayout.PropertyField(_publishEmptyFrames);
            EditorGUILayout.PropertyField(_drawDebugRays);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Synthetic Values", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_syntheticReflectivity);
            EditorGUILayout.PropertyField(_syntheticIntensity);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
