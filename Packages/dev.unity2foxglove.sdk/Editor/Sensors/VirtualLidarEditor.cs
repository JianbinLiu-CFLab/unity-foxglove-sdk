// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Sensors

using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Sensors.Lidar;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    [CustomEditor(typeof(VirtualLidar))]
    public class VirtualLidarEditor : UnityEditor.Editor
    {
        private SerializedProperty _profileSource;
        private SerializedProperty _metadataJson;
        private SerializedProperty _metadataMode;
        private SerializedProperty _preset;
        private SerializedProperty _customPixelsPerColumn;
        private SerializedProperty _customFovTopDeg;
        private SerializedProperty _customFovBottomDeg;
        private SerializedProperty _customColumnsPerFrame;
        private SerializedProperty _customScanRateHz;
        private SerializedProperty _customMinRangeMeters;
        private SerializedProperty _columnStep;
        private SerializedProperty _maxRangeMeters;
        private SerializedProperty _scanRateHzOverride;
        private SerializedProperty _frameId;
        private SerializedProperty _layerMask;
        private SerializedProperty _publishEmptyFrames;
        private SerializedProperty _drawDebugRays;
        private SerializedProperty _syntheticReflectivity;
        private SerializedProperty _syntheticIntensity;
        private SerializedProperty _pointCloudPublisher;

        private void OnEnable()
        {
            _profileSource = serializedObject.FindProperty("_profileSource");
            _metadataJson = serializedObject.FindProperty("_metadataJson");
            _metadataMode = serializedObject.FindProperty("_metadataMode");
            _preset = serializedObject.FindProperty("_preset");
            _customPixelsPerColumn = serializedObject.FindProperty("_customPixelsPerColumn");
            _customFovTopDeg = serializedObject.FindProperty("_customFovTopDeg");
            _customFovBottomDeg = serializedObject.FindProperty("_customFovBottomDeg");
            _customColumnsPerFrame = serializedObject.FindProperty("_customColumnsPerFrame");
            _customScanRateHz = serializedObject.FindProperty("_customScanRateHz");
            _customMinRangeMeters = serializedObject.FindProperty("_customMinRangeMeters");
            _columnStep = serializedObject.FindProperty("_columnStep");
            _maxRangeMeters = serializedObject.FindProperty("_maxRangeMeters");
            _scanRateHzOverride = serializedObject.FindProperty("_scanRateHzOverride");
            _frameId = serializedObject.FindProperty("_frameId");
            _layerMask = serializedObject.FindProperty("_layerMask");
            _publishEmptyFrames = serializedObject.FindProperty("_publishEmptyFrames");
            _drawDebugRays = serializedObject.FindProperty("_drawDebugRays");
            _syntheticReflectivity = serializedObject.FindProperty("_syntheticReflectivity");
            _syntheticIntensity = serializedObject.FindProperty("_syntheticIntensity");
            _pointCloudPublisher = serializedObject.FindProperty("_pointCloudPublisher");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // --- Output ---
            EditorGUILayout.PropertyField(_pointCloudPublisher);
            EditorGUILayout.Space();

            // --- Profile source (read-only dropdown, managed by target) ---
            DrawProfileSourceSection();

            // --- Scan parameters ---
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scan", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_frameId);
            EditorGUILayout.PropertyField(_columnStep);
            EditorGUILayout.PropertyField(_maxRangeMeters);
            EditorGUILayout.PropertyField(_scanRateHzOverride);
            EditorGUILayout.PropertyField(_layerMask);
            EditorGUILayout.PropertyField(_publishEmptyFrames);
            EditorGUILayout.PropertyField(_drawDebugRays);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Synthetic Values", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_syntheticReflectivity);
            EditorGUILayout.PropertyField(_syntheticIntensity);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawProfileSourceSection()
        {
            EditorGUILayout.LabelField("Profile", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_profileSource);

            var source = (VirtualLidar.ProfileSource)_profileSource.enumValueIndex;

            switch (source)
            {
                case VirtualLidar.ProfileSource.BuiltInPreset:
                    DrawBuiltInPresetSection();
                    break;
                case VirtualLidar.ProfileSource.MetadataJson:
                    DrawMetadataJsonSection();
                    break;
                case VirtualLidar.ProfileSource.Custom:
                    DrawCustomProfileSection();
                    break;
            }
        }

        private void DrawBuiltInPresetSection()
        {
            EditorGUILayout.PropertyField(_preset);

            var preset = (LidarPreset)_preset.enumValueIndex;
            var profile = LidarProfileLoader.CreatePreset(preset);

            DrawProfilePreview(profile);

            EditorGUILayout.Space();
            if (GUILayout.Button("Apply Preset to Custom"))
            {
                _profileSource.enumValueIndex = (int)VirtualLidar.ProfileSource.Custom;
                _customPixelsPerColumn.intValue = profile.PixelsPerColumn;
                _customFovTopDeg.floatValue = profile.BeamAltitudeAngles.Length > 0
                    ? (float)(profile.BeamAltitudeAngles[0] * Mathf.Rad2Deg)
                    : profile.BeamAltitudeAngles.Length > 0 ? GetFovTopFromAngles(profile.BeamAltitudeAngles) : 16.6f;
                _customFovBottomDeg.floatValue = profile.BeamAltitudeAngles.Length > 0
                    ? (float)(profile.BeamAltitudeAngles[profile.BeamAltitudeAngles.Length - 1] * Mathf.Rad2Deg)
                    : GetFovBottomFromAngles(profile.BeamAltitudeAngles);
                _customColumnsPerFrame.intValue = profile.ColumnsPerFrame;
                _customScanRateHz.floatValue = (float)profile.ScanRateHz;
                _customMinRangeMeters.floatValue = (float)profile.MinRangeMeters;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void DrawMetadataJsonSection()
        {
            EditorGUILayout.PropertyField(_metadataJson);
            EditorGUILayout.PropertyField(_metadataMode);

            if (_metadataJson.objectReferenceValue is TextAsset ta && !string.IsNullOrEmpty(ta.text))
            {
                if (LidarProfileLoader.TryParseFromJson(ta.text, _metadataMode.stringValue, out var profile, out var error))
                {
                    DrawProfilePreview(profile);
                }
                else
                {
                    EditorGUILayout.HelpBox($"Parse error: {error}", MessageType.Warning);
                }
            }
        }

        private void DrawCustomProfileSection()
        {
            EditorGUILayout.PropertyField(_customPixelsPerColumn);
            EditorGUILayout.PropertyField(_customFovTopDeg);
            EditorGUILayout.PropertyField(_customFovBottomDeg);
            EditorGUILayout.PropertyField(_customColumnsPerFrame);
            EditorGUILayout.PropertyField(_customScanRateHz);
            EditorGUILayout.PropertyField(_customMinRangeMeters);

            var profile = LidarProfileLoader.CreateUniform(
                "Custom", _customPixelsPerColumn.intValue, _customColumnsPerFrame.intValue,
                _customScanRateHz.floatValue, _customFovTopDeg.floatValue, _customFovBottomDeg.floatValue,
                _customMinRangeMeters.floatValue);

            DrawProfilePreview(profile);
        }

        private static void DrawProfilePreview(LidarProfile profile)
        {
            if (profile == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Product Line", profile.ProductLine);
            EditorGUILayout.TextField("Mode", profile.LidarMode);
            EditorGUILayout.IntField("Pixels Per Column", profile.PixelsPerColumn);
            EditorGUILayout.IntField("Columns Per Frame", profile.ColumnsPerFrame);

            if (profile.BeamAltitudeAngles != null && profile.BeamAltitudeAngles.Length > 0)
            {
                var fovTop = profile.BeamAltitudeAngles[0] * Mathf.Rad2Deg;
                var fovBottom = profile.BeamAltitudeAngles[profile.BeamAltitudeAngles.Length - 1] * Mathf.Rad2Deg;
                EditorGUILayout.FloatField("FOV Top (deg)", fovTop);
                EditorGUILayout.FloatField("FOV Bottom (deg)", fovBottom);
            }

            EditorGUILayout.FloatField("Scan Rate (Hz)", (float)profile.ScanRateHz);
            EditorGUILayout.FloatField("Min Range (m)", (float)profile.MinRangeMeters);
            EditorGUI.EndDisabledGroup();
        }

        private static float GetFovTopFromAngles(double[] angles)
        {
            if (angles == null || angles.Length == 0) return 16.6f;
            var max = angles.Max();
            var min = angles.Min();
            return (float)(max * Mathf.Rad2Deg);
        }

        private static float GetFovBottomFromAngles(double[] angles)
        {
            if (angles == null || angles.Length == 0) return -16.6f;
            var min = angles.Min();
            return (float)(min * Mathf.Rad2Deg);
        }
    }
}
