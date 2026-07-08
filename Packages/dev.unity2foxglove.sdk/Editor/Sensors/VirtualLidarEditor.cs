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
        private SerializedProperty _profileSource, _vendor, _model, _mode, _metadataJson, _metadataMode,
            _customPixelsPerColumn, _customFovTopDeg, _customFovBottomDeg,
            _customColumnsPerFrame, _customScanRateHz, _customMinRangeMeters;
        private SerializedProperty _overrideTIl, _tIlRotationInputFormat, _tIlTranslationMeters, _tIlRotation;
        private SerializedProperty _frameId, _columnStep, _maxRaysPerScan, _maxRangeMeters,
            _scanRateSource, _scanRateHzOverride,
            _layerMask, _publishEmptyFrames, _drawDebugRays;
        private SerializedProperty _maxRaycastCommandsPerFixedUpdate;
        private SerializedProperty _logPerformanceDiagnostics;
        private SerializedProperty _syntheticReflectivity, _syntheticIntensity;

        private void OnEnable()
        {
            _sensorUnitProfile = serializedObject.FindProperty("_sensorUnitProfile");
            _profileSource = serializedObject.FindProperty("_profileSource");
            _vendor = serializedObject.FindProperty("_vendor");
            _model = serializedObject.FindProperty("_model");
            _mode = serializedObject.FindProperty("_mode");
            _metadataJson = serializedObject.FindProperty("_metadataJson");
            _metadataMode = serializedObject.FindProperty("_metadataMode");
            _customPixelsPerColumn = serializedObject.FindProperty("_customPixelsPerColumn");
            _customFovTopDeg = serializedObject.FindProperty("_customFovTopDeg");
            _customFovBottomDeg = serializedObject.FindProperty("_customFovBottomDeg");
            _customColumnsPerFrame = serializedObject.FindProperty("_customColumnsPerFrame");
            _customScanRateHz = serializedObject.FindProperty("_customScanRateHz");
            _customMinRangeMeters = serializedObject.FindProperty("_customMinRangeMeters");
            _overrideTIl = serializedObject.FindProperty("_overrideTIl");
            _tIlRotationInputFormat = serializedObject.FindProperty("_tIlRotationInputFormat");
            _tIlTranslationMeters = serializedObject.FindProperty("_tIlTranslationMeters");
            _tIlRotation = serializedObject.FindProperty("_tIlRotation");
            _frameId = serializedObject.FindProperty("_frameId");
            _columnStep = serializedObject.FindProperty("_columnStep");
            _maxRaysPerScan = serializedObject.FindProperty("_maxRaysPerScan");
            _maxRangeMeters = serializedObject.FindProperty("_maxRangeMeters");
            _scanRateSource = serializedObject.FindProperty("_scanRateSource");
            _scanRateHzOverride = serializedObject.FindProperty("_scanRateHzOverride");
            _layerMask = serializedObject.FindProperty("_layerMask");
            _publishEmptyFrames = serializedObject.FindProperty("_publishEmptyFrames");
            _drawDebugRays = serializedObject.FindProperty("_drawDebugRays");
            _maxRaycastCommandsPerFixedUpdate = serializedObject.FindProperty("_maxRaycastCommandsPerFixedUpdate");
            _logPerformanceDiagnostics = serializedObject.FindProperty("_logPerformanceDiagnostics");
            _syntheticReflectivity = serializedObject.FindProperty("_syntheticReflectivity");
            _syntheticIntensity = serializedObject.FindProperty("_syntheticIntensity");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_sensorUnitProfile, new GUIContent("Sensor Unit Profile"));
            DrawProfileSection();
            DrawTIlSection();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scan", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_frameId);
            EditorGUILayout.PropertyField(_columnStep);
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
            EditorGUILayout.PropertyField(_logPerformanceDiagnostics, new GUIContent("Log Performance Diagnostics"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _maxRaycastCommandsPerFixedUpdate,
                new GUIContent("Max Raycast Commands Per FixedUpdate"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Synthetic Values", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_syntheticReflectivity);
            EditorGUILayout.PropertyField(_syntheticIntensity);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawProfileSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Profile", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_profileSource, new GUIContent("Profile Source"));

            switch ((VirtualLidar.ProfileSource)_profileSource.enumValueIndex)
            {
                case VirtualLidar.ProfileSource.BuiltInPreset:
                    EditorGUILayout.PropertyField(_vendor);
                    EditorGUILayout.PropertyField(_model);
                    EditorGUILayout.PropertyField(_mode);
                    break;
                case VirtualLidar.ProfileSource.MetadataJson:
                    EditorGUILayout.PropertyField(_metadataJson);
                    EditorGUILayout.PropertyField(_metadataMode);
                    break;
                case VirtualLidar.ProfileSource.Custom:
                    EditorGUILayout.PropertyField(_customPixelsPerColumn);
                    EditorGUILayout.PropertyField(_customFovTopDeg);
                    EditorGUILayout.PropertyField(_customFovBottomDeg);
                    EditorGUILayout.PropertyField(_customColumnsPerFrame);
                    EditorGUILayout.PropertyField(_customScanRateHz);
                    EditorGUILayout.PropertyField(_customMinRangeMeters);
                    break;
            }
        }

        private void DrawTIlSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("T_IL Override", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_overrideTIl, new GUIContent("Override T_IL"));
            using (new EditorGUI.DisabledScope(!_overrideTIl.boolValue))
            {
                EditorGUILayout.PropertyField(_tIlRotationInputFormat);
                EditorGUILayout.PropertyField(_tIlTranslationMeters);
                EditorGUILayout.PropertyField(_tIlRotation);
            }
        }
    }
}
