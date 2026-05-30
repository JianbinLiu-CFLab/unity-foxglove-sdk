// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Sensors

using System;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Sensors.Lidar;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="VirtualLidar"/>. For BuiltInPreset mode it
    /// shows a cascading Vendor 鈫?Model (鈫?optional Mode) selection driven by
    /// <see cref="LidarModelRegistry"/>, with a live read-only preview of the
    /// resolved scan geometry.
    /// </summary>
    /// <summary>
    /// Summary text for this member.
    /// </summary>

    [CustomEditor(typeof(VirtualLidar))]
/// <summary>Summary text for this member.</summary>
    public class VirtualLidarEditor : UnityEditor.Editor
    {
        private SerializedProperty _profileSource;
        private SerializedProperty _metadataJson, _metadataMode;
        private SerializedProperty _vendor, _model, _mode;
        private SerializedProperty _customPixelsPerColumn, _customFovTopDeg, _customFovBottomDeg,
            _customColumnsPerFrame, _customScanRateHz, _customMinRangeMeters;
        private SerializedProperty _frameId, _columnStep, _maxRaysPerScan, _maxRangeMeters,
            _scanSubSteps, _scanRateSource, _scanRateHzOverride,
            _layerMask, _publishEmptyFrames, _drawDebugRays;
        private SerializedProperty _syntheticReflectivity, _syntheticIntensity;
        private SerializedProperty _pointCloudPublisher;

        private void OnEnable()
        {
            _profileSource = serializedObject.FindProperty("_profileSource");
            _metadataJson = serializedObject.FindProperty("_metadataJson");
            _metadataMode = serializedObject.FindProperty("_metadataMode");
            _vendor = serializedObject.FindProperty("_vendor");
            _model = serializedObject.FindProperty("_model");
            _mode = serializedObject.FindProperty("_mode");
            _customPixelsPerColumn = serializedObject.FindProperty("_customPixelsPerColumn");
            _customFovTopDeg = serializedObject.FindProperty("_customFovTopDeg");
            _customFovBottomDeg = serializedObject.FindProperty("_customFovBottomDeg");
            _customColumnsPerFrame = serializedObject.FindProperty("_customColumnsPerFrame");
            _customScanRateHz = serializedObject.FindProperty("_customScanRateHz");
            _customMinRangeMeters = serializedObject.FindProperty("_customMinRangeMeters");
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
            _pointCloudPublisher = serializedObject.FindProperty("_pointCloudPublisher");
        }

        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Section titles ("Output", "Profile", "Scan", "Synthetic Values") come
            // from the [Header] attributes on the VirtualLidar fields 鈥?PropertyField
            // renders them. Don't also draw LabelField headers here or they double up.
            EditorGUILayout.PropertyField(_pointCloudPublisher);   // [Header("Output")]

            EditorGUILayout.PropertyField(_profileSource);         // [Header("Profile")]
            switch ((VirtualLidar.ProfileSource)_profileSource.enumValueIndex)
            {
                case VirtualLidar.ProfileSource.BuiltInPreset: DrawPresetSection(); break;
                case VirtualLidar.ProfileSource.MetadataJson: DrawMetadataSection(); break;
                case VirtualLidar.ProfileSource.Custom: DrawCustomSection(); break;
            }

            EditorGUILayout.PropertyField(_frameId);               // [Header("Scan")]
            EditorGUILayout.PropertyField(_columnStep);
            EditorGUILayout.PropertyField(_scanSubSteps);
            EditorGUILayout.PropertyField(_maxRaysPerScan);
            EditorGUILayout.PropertyField(_maxRangeMeters);

            // Scan rate 鈥?mirrors the publisher's "Publish Rate" UX. NOTE: this is the
            // LiDAR's frame-generation rate; the point cloud's publish rate to Foxglove
            // is set on FoxglovePointCloudPublisher (Publish Rate Source / Hz).
            EditorGUILayout.PropertyField(_scanRateSource, new GUIContent("Scan Rate Source"));
            using (new EditorGUI.DisabledScope(
                _scanRateSource.enumValueIndex != (int)VirtualLidar.ScanRateSource.Override))
            {
                EditorGUILayout.PropertyField(_scanRateHzOverride, new GUIContent("Scan Rate Hz"));
            }

            EditorGUILayout.PropertyField(_layerMask);
            EditorGUILayout.PropertyField(_publishEmptyFrames);
            EditorGUILayout.PropertyField(_drawDebugRays);

            EditorGUILayout.PropertyField(_syntheticReflectivity); // [Header("Synthetic Values")]
            EditorGUILayout.PropertyField(_syntheticIntensity);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPresetSection()
        {
            EditorGUILayout.PropertyField(_vendor);
            var vendor = (LidarVendor)_vendor.enumValueIndex;

            var models = LidarModelRegistry.ForVendor(vendor).ToList();
            if (models.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No built-in models for {vendor}. Use Profile Source = Custom, or pick another vendor.",
                    MessageType.Info);
                return;
            }

            // Model dropdown (filtered by vendor). Reset to first model when the
            // stored model id does not belong to the current vendor.
            var modelNames = models.Select(m => m.Model).ToArray();
            var modelIdx = Array.IndexOf(modelNames, _model.stringValue);
            if (modelIdx < 0) modelIdx = 0;
            var newModelIdx = EditorGUILayout.Popup("Model", modelIdx, modelNames);
            if (newModelIdx != modelIdx || _model.stringValue != modelNames[newModelIdx])
                _model.stringValue = modelNames[newModelIdx];

            var spec = models[newModelIdx];

            // Optional scan-mode dropdown for models that support multiple modes.
            if (spec.Modes != null && spec.Modes.Length > 0)
            {
                var modeIdx = Array.IndexOf(spec.Modes, _mode.stringValue);
                if (modeIdx < 0) modeIdx = 0;
                var newModeIdx = EditorGUILayout.Popup("Mode", modeIdx, spec.Modes);
                if (newModeIdx != modeIdx || _mode.stringValue != spec.Modes[newModeIdx])
                    _mode.stringValue = spec.Modes[newModeIdx];
            }

            DrawSpecPreview(spec, _mode.stringValue);
        }

        private void DrawMetadataSection()
        {
            EditorGUILayout.PropertyField(_metadataJson);
            EditorGUILayout.PropertyField(_metadataMode);

            if (_metadataJson.objectReferenceValue is TextAsset ta && !string.IsNullOrEmpty(ta.text))
            {
                if (LidarProfileLoader.TryParseFromJson(ta.text, _metadataMode.stringValue, out var profile, out var error))
                    DrawProfilePreview(profile);
                else
                    EditorGUILayout.HelpBox($"Parse error: {error}", MessageType.Warning);
            }
        }

        private void DrawCustomSection()
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

        private static void DrawSpecPreview(LidarModelSpec spec, string mode)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);

            var columns = spec.Columns;
            var rate = spec.RateHz;
            if (LidarProfileLoader.TryParseMode(mode, out var mc, out var mr)) { columns = mc; rate = mr; }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Vendor", spec.Vendor.ToString());
            EditorGUILayout.TextField("Model", spec.Model);
            EditorGUILayout.TextField("Scan Kind", spec.Kind.ToString());

            if (spec.Kind == LidarScanKind.Spinning)
            {
                EditorGUILayout.IntField("Rings", spec.Rings);
                EditorGUILayout.IntField("Columns / Frame", columns);
                EditorGUILayout.FloatField("FOV Top (deg)", (float)spec.FovTopDeg);
                EditorGUILayout.FloatField("FOV Bottom (deg)", (float)spec.FovBottomDeg);
            }
            else
            {
                EditorGUILayout.FloatField("FOV Horizontal (deg)", (float)spec.FovHDeg);
                EditorGUILayout.FloatField("FOV Vertical (deg)", (float)spec.FovVDeg);
                EditorGUILayout.IntField("Beams / Frame", spec.BeamsPerFrame);
            }

            EditorGUILayout.FloatField("Scan Rate (Hz)", (float)rate);
            EditorGUILayout.FloatField("Min Range (m)", (float)spec.MinRangeMeters);
            EditorGUILayout.FloatField("Max Range (m)", (float)spec.MaxRangeMeters);
            var translation = spec.TIlTranslationMeters;
            var rotation = spec.TIlRotation;
            EditorGUILayout.Vector3Field("T_IL Translation (m)", new Vector3(
                (float)translation.X, (float)translation.Y, (float)translation.Z));
            EditorGUILayout.Vector3Field("T_IL Rotation (deg, euler)", new Quaternion(
                (float)rotation.X, (float)rotation.Y, (float)rotation.Z, (float)rotation.W).eulerAngles);
            EditorGUI.EndDisabledGroup();
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
                EditorGUILayout.FloatField("FOV Top (deg)",
                    (float)(profile.BeamAltitudeAngles[0] * Mathf.Rad2Deg));
                EditorGUILayout.FloatField("FOV Bottom (deg)",
                    (float)(profile.BeamAltitudeAngles[profile.BeamAltitudeAngles.Length - 1] * Mathf.Rad2Deg));
            }
            EditorGUILayout.FloatField("Scan Rate (Hz)", (float)profile.ScanRateHz);
            EditorGUILayout.FloatField("Min Range (m)", (float)profile.MinRangeMeters);
            EditorGUI.EndDisabledGroup();
        }
    }
}
