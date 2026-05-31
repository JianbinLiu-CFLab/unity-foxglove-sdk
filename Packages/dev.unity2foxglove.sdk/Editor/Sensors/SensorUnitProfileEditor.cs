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
    /// <summary>Custom inspector for the LiDAR/IMU unit profile on os_sensor.</summary>
    [CustomEditor(typeof(SensorUnitProfile))]
    public class SensorUnitProfileEditor : UnityEditor.Editor
    {
        private SerializedProperty _manager, _pointCloudPublisher;
        private SerializedProperty _profileSource, _metadataJson, _metadataMode;
        private SerializedProperty _vendor, _model, _mode;
        private SerializedProperty _sensorFrameId, _lidarFrameId, _imuFrameId;
        private SerializedProperty _useLidarToSensorExtrinsic, _useImuToSensorExtrinsic,
            _useLidarToImuExtrinsic;
        private SerializedProperty _overrideLidarToSensor, _lidarToSensorRotationInputFormat,
            _lidarToSensorTranslationMeters, _lidarToSensorRotation;
        private SerializedProperty _overrideImuToSensor, _imuToSensorRotationInputFormat,
            _imuToSensorTranslationMeters, _imuToSensorRotation;
        private SerializedProperty _overrideLidarToImu, _lidarToImuRotationInputFormat,
            _lidarToImuTranslationMeters, _lidarToImuRotation;
        private SerializedProperty _customPixelsPerColumn, _customFovTopDeg, _customFovBottomDeg,
            _customColumnsPerFrame, _customScanRateHz, _customMinRangeMeters;

        private void OnEnable()
        {
            _manager = serializedObject.FindProperty("_manager");
            _pointCloudPublisher = serializedObject.FindProperty("_pointCloudPublisher");
            _profileSource = serializedObject.FindProperty("_profileSource");
            _metadataJson = serializedObject.FindProperty("_metadataJson");
            _metadataMode = serializedObject.FindProperty("_metadataMode");
            _vendor = serializedObject.FindProperty("_vendor");
            _model = serializedObject.FindProperty("_model");
            _mode = serializedObject.FindProperty("_mode");
            _sensorFrameId = serializedObject.FindProperty("_sensorFrameId");
            _lidarFrameId = serializedObject.FindProperty("_lidarFrameId");
            _imuFrameId = serializedObject.FindProperty("_imuFrameId");
            _useLidarToSensorExtrinsic = serializedObject.FindProperty("_useLidarToSensorExtrinsic");
            _useImuToSensorExtrinsic = serializedObject.FindProperty("_useImuToSensorExtrinsic");
            _useLidarToImuExtrinsic = serializedObject.FindProperty("_useLidarToImuExtrinsic");
            _overrideLidarToSensor = serializedObject.FindProperty("_overrideLidarToSensor");
            _lidarToSensorRotationInputFormat = serializedObject.FindProperty("_lidarToSensorRotationInputFormat");
            _lidarToSensorTranslationMeters = serializedObject.FindProperty("_lidarToSensorTranslationMeters");
            _lidarToSensorRotation = serializedObject.FindProperty("_lidarToSensorRotation");
            _overrideImuToSensor = serializedObject.FindProperty("_overrideImuToSensor");
            _imuToSensorRotationInputFormat = serializedObject.FindProperty("_imuToSensorRotationInputFormat");
            _imuToSensorTranslationMeters = serializedObject.FindProperty("_imuToSensorTranslationMeters");
            _imuToSensorRotation = serializedObject.FindProperty("_imuToSensorRotation");
            _overrideLidarToImu = serializedObject.FindProperty("_overrideLidarToImu");
            _lidarToImuRotationInputFormat = serializedObject.FindProperty("_lidarToImuRotationInputFormat");
            _lidarToImuTranslationMeters = serializedObject.FindProperty("_lidarToImuTranslationMeters");
            _lidarToImuRotation = serializedObject.FindProperty("_lidarToImuRotation");
            _customPixelsPerColumn = serializedObject.FindProperty("_customPixelsPerColumn");
            _customFovTopDeg = serializedObject.FindProperty("_customFovTopDeg");
            _customFovBottomDeg = serializedObject.FindProperty("_customFovBottomDeg");
            _customColumnsPerFrame = serializedObject.FindProperty("_customColumnsPerFrame");
            _customScanRateHz = serializedObject.FindProperty("_customScanRateHz");
            _customMinRangeMeters = serializedObject.FindProperty("_customMinRangeMeters");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_manager);
            EditorGUILayout.PropertyField(_pointCloudPublisher);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Profile", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_profileSource);
            switch ((SensorUnitProfile.ProfileSource)_profileSource.enumValueIndex)
            {
                case SensorUnitProfile.ProfileSource.BuiltInPreset:
                    DrawPresetSection();
                    break;
                case SensorUnitProfile.ProfileSource.MetadataJson:
                    DrawMetadataSection();
                    break;
                case SensorUnitProfile.ProfileSource.Custom:
                    DrawCustomSection();
                    break;
            }

            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            DrawModelDefaults();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Frames", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_sensorFrameId, new GUIContent("Sensor Frame Id"));
            EditorGUILayout.PropertyField(_lidarFrameId, new GUIContent("LiDAR Frame Id"));
            EditorGUILayout.PropertyField(_imuFrameId, new GUIContent("IMU Frame Id"));

            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();

            DrawExtrinsics();

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

            var modelNames = models.Select(m => m.Model).ToArray();
            var modelIdx = Array.IndexOf(modelNames, _model.stringValue);
            if (modelIdx < 0) modelIdx = 0;
            var newModelIdx = EditorGUILayout.Popup("Model", modelIdx, modelNames);
            if (newModelIdx != modelIdx || _model.stringValue != modelNames[newModelIdx])
                _model.stringValue = modelNames[newModelIdx];

            var spec = models[newModelIdx];
            if (spec.Modes != null && spec.Modes.Length > 0)
            {
                var modeIdx = Array.IndexOf(spec.Modes, _mode.stringValue);
                if (modeIdx < 0) modeIdx = 0;
                var newModeIdx = EditorGUILayout.Popup("Mode", modeIdx, spec.Modes);
                if (newModeIdx != modeIdx || _mode.stringValue != spec.Modes[newModeIdx])
                    _mode.stringValue = spec.Modes[newModeIdx];
            }
        }

        private void DrawMetadataSection()
        {
            EditorGUILayout.PropertyField(_metadataJson);
            EditorGUILayout.PropertyField(_metadataMode);

            if (_metadataJson.objectReferenceValue is TextAsset ta && !string.IsNullOrEmpty(ta.text))
            {
                if (!LidarProfileLoader.TryParseFromJson(ta.text, _metadataMode.stringValue, out _, out var error))
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
        }

        private void DrawModelDefaults()
        {
            var profile = target as SensorUnitProfile;
            if (profile == null)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Model Defaults", EditorStyles.boldLabel);

            switch ((SensorUnitProfile.ProfileSource)_profileSource.enumValueIndex)
            {
                case SensorUnitProfile.ProfileSource.BuiltInPreset:
                    if (LidarModelRegistry.TryGet((LidarVendor)_vendor.enumValueIndex, _model.stringValue, out var spec))
                        DrawSpecPreview(spec, _mode.stringValue);
                    break;
                case SensorUnitProfile.ProfileSource.MetadataJson:
                    if (_metadataJson.objectReferenceValue is TextAsset ta && !string.IsNullOrEmpty(ta.text) &&
                        LidarProfileLoader.TryParseFromJson(ta.text, _metadataMode.stringValue, out var parsed, out _))
                        DrawProfilePreview(parsed);
                    break;
                case SensorUnitProfile.ProfileSource.Custom:
                    DrawProfilePreview(LidarProfileLoader.CreateUniform(
                        "Custom", _customPixelsPerColumn.intValue, _customColumnsPerFrame.intValue,
                        _customScanRateHz.floatValue, _customFovTopDeg.floatValue, _customFovBottomDeg.floatValue,
                        _customMinRangeMeters.floatValue));
                    break;
            }

            DrawReadonlyExtrinsic("Model LiDAR -> Sensor", "LiDAR->Sensor", profile.ModelLidarToSensor);
            DrawReadonlyExtrinsic("Model IMU -> Sensor", "IMU->Sensor", profile.ModelImuToSensor);
            DrawReadonlyExtrinsic("Model LiDAR -> IMU", "LiDAR->IMU", profile.ModelLidarToImu);
        }

        private void DrawExtrinsics()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Extrinsics", EditorStyles.boldLabel);
            DrawExtrinsicsHelp();

            DrawExtrinsicUsageToggle(_useLidarToSensorExtrinsic, "Use LiDAR -> Sensor", ExtrinsicKind.LidarToSensor);
            DrawExtrinsicUsageToggle(_useImuToSensorExtrinsic, "Use IMU -> Sensor", ExtrinsicKind.ImuToSensor);
            DrawExtrinsicUsageToggle(_useLidarToImuExtrinsic, "Use LiDAR -> IMU", ExtrinsicKind.LidarToImu);

            if (_useLidarToSensorExtrinsic.boolValue)
            {
                DrawExtrinsicSection(
                    "LiDAR -> Sensor",
                    "LiDAR->Sensor",
                    "Override Model LiDAR->Sensor",
                    ResolveModelExtrinsic(ExtrinsicKind.LidarToSensor),
                    _overrideLidarToSensor,
                    _lidarToSensorTranslationMeters,
                    _lidarToSensorRotationInputFormat,
                    _lidarToSensorRotation);
            }

            if (_useImuToSensorExtrinsic.boolValue)
            {
                DrawExtrinsicSection(
                    "IMU -> Sensor",
                    "IMU->Sensor",
                    "Override Model IMU->Sensor",
                    ResolveModelExtrinsic(ExtrinsicKind.ImuToSensor),
                    _overrideImuToSensor,
                    _imuToSensorTranslationMeters,
                    _imuToSensorRotationInputFormat,
                    _imuToSensorRotation);
            }

            if (_useLidarToImuExtrinsic.boolValue)
            {
                DrawExtrinsicSection(
                    "LiDAR -> IMU",
                    "LiDAR->IMU",
                    "Override Model LiDAR->IMU",
                    ResolveModelExtrinsic(ExtrinsicKind.LidarToImu),
                    _overrideLidarToImu,
                    _lidarToImuTranslationMeters,
                    _lidarToImuRotationInputFormat,
                    _lidarToImuRotation);
            }

            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();

            var profile = (SensorUnitProfile)target;
            if (!_useLidarToSensorExtrinsic.boolValue)
                DrawDerivedExtrinsicPreview("Derived LiDAR -> Sensor", "LiDAR->Sensor", profile.EffectiveLidarToSensor);
            if (!_useImuToSensorExtrinsic.boolValue)
                DrawDerivedExtrinsicPreview("Derived IMU -> Sensor", "IMU->Sensor", profile.EffectiveImuToSensor);
            if (!_useLidarToImuExtrinsic.boolValue)
                DrawDerivedExtrinsicPreview("Derived LiDAR -> IMU", "LiDAR->IMU", profile.EffectiveLidarToImu);
        }

        private static void DrawExtrinsicsHelp()
        {
            EditorGUILayout.HelpBox(
                "Select exactly two authoritative relationships. The unchecked relationship is derived automatically.",
                MessageType.Info);
        }

        private void DrawExtrinsicUsageToggle(
            SerializedProperty useProperty,
            string label,
            ExtrinsicKind changedKind)
        {
            EditorGUI.BeginChangeCheck();
            var newValue = EditorGUILayout.ToggleLeft(label, useProperty.boolValue);
            if (!EditorGUI.EndChangeCheck())
                return;

            useProperty.boolValue = newValue;
            NormalizeUsageSelection(changedKind);
        }

        private void DrawExtrinsicSection(
            string title,
            string labelPrefix,
            string overrideLabel,
            LidarTIlExtrinsic modelExtrinsic,
            SerializedProperty overrideProperty,
            SerializedProperty translationProperty,
            SerializedProperty rotationFormatProperty,
            SerializedProperty rotationProperty)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            var wasOverride = overrideProperty.boolValue;
            EditorGUI.BeginChangeCheck();
            var wantsOverride = EditorGUILayout.Toggle(overrideLabel, wasOverride);
            if (EditorGUI.EndChangeCheck())
            {
                overrideProperty.boolValue = wantsOverride;
                if (wantsOverride && !wasOverride)
                {
                    translationProperty.vector3Value = SensorUnitProfile.ToUnityVector3(modelExtrinsic.TranslationMeters);
                    rotationProperty.quaternionValue = SensorUnitProfile.ToUnityQuaternion(modelExtrinsic.Rotation);
                }
            }

            using (new EditorGUI.DisabledScope(!overrideProperty.boolValue))
            {
                EditorGUILayout.PropertyField(translationProperty, new GUIContent($"{labelPrefix} Translation (m)"));
                rotationFormatProperty.enumValueIndex = EditorGUILayout.Popup(
                    "Rotation Input",
                    rotationFormatProperty.enumValueIndex,
                    new[] { "Quaternion", "3x3 Rotation Matrix" });

                var format = (SensorUnitProfile.RotationInputFormat)rotationFormatProperty.enumValueIndex;
                if (format == SensorUnitProfile.RotationInputFormat.Matrix3x3)
                    DrawRotationMatrixEditor(rotationProperty);
                else
                    DrawRotationQuaternionEditor(rotationProperty, $"{labelPrefix} Rotation (xyzw)");
            }
        }

        private void NormalizeUsageSelection(ExtrinsicKind changedKind)
        {
            var useLidarToSensor = _useLidarToSensorExtrinsic.boolValue;
            var useImuToSensor = _useImuToSensorExtrinsic.boolValue;
            var useLidarToImu = _useLidarToImuExtrinsic.boolValue;
            var count = (useLidarToSensor ? 1 : 0) + (useImuToSensor ? 1 : 0) + (useLidarToImu ? 1 : 0);

            if (count < 2)
            {
                if (changedKind != ExtrinsicKind.LidarToSensor && !useLidarToSensor)
                    useLidarToSensor = true;
                else if (changedKind != ExtrinsicKind.ImuToSensor && !useImuToSensor)
                    useImuToSensor = true;
                else if (changedKind != ExtrinsicKind.LidarToImu && !useLidarToImu)
                    useLidarToImu = true;
            }
            else if (count > 2)
            {
                if (changedKind != ExtrinsicKind.LidarToImu && useLidarToImu)
                    useLidarToImu = false;
                else if (changedKind != ExtrinsicKind.ImuToSensor && useImuToSensor)
                    useImuToSensor = false;
                else if (changedKind != ExtrinsicKind.LidarToSensor && useLidarToSensor)
                    useLidarToSensor = false;
            }

            _useLidarToSensorExtrinsic.boolValue = useLidarToSensor;
            _useImuToSensorExtrinsic.boolValue = useImuToSensor;
            _useLidarToImuExtrinsic.boolValue = useLidarToImu;
        }

        private LidarTIlExtrinsic ResolveModelExtrinsic(ExtrinsicKind kind)
        {
            var profile = target as SensorUnitProfile;
            if (profile == null)
                return LidarTIlExtrinsic.Identity;

            switch (kind)
            {
                case ExtrinsicKind.ImuToSensor:
                    return profile.ModelImuToSensor;
                case ExtrinsicKind.LidarToImu:
                    return profile.ModelLidarToImu;
                case ExtrinsicKind.LidarToSensor:
                default:
                    return profile.ModelLidarToSensor;
            }
        }

        private static void DrawReadonlyExtrinsic(
            string title,
            string labelPrefix,
            LidarTIlExtrinsic extrinsic)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector3Field(
                    $"{labelPrefix} Translation (m)",
                    SensorUnitProfile.ToUnityVector3(extrinsic.TranslationMeters));
                DrawQuaternionVector4(
                    $"{labelPrefix} Rotation (xyzw)",
                    SensorUnitProfile.ToUnityQuaternion(extrinsic.Rotation),
                    editable: false);
            }
        }

        private static void DrawDerivedExtrinsicPreview(
            string title,
            string labelPrefix,
            LidarTIlExtrinsic extrinsic)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector3Field(
                    $"{labelPrefix} Translation (m)",
                    SensorUnitProfile.ToUnityVector3(extrinsic.TranslationMeters));
                DrawQuaternionVector4(
                    $"{labelPrefix} Rotation (xyzw)",
                    SensorUnitProfile.ToUnityQuaternion(extrinsic.Rotation),
                    editable: false);
            }
        }

        private enum ExtrinsicKind
        {
            LidarToSensor,
            ImuToSensor,
            LidarToImu
        }

        private static void DrawRotationQuaternionEditor(SerializedProperty rotationProperty, string label)
        {
            var rotation = rotationProperty.quaternionValue;
            EditorGUI.BeginChangeCheck();
            rotation = DrawQuaternionVector4(label, rotation, editable: true);
            if (EditorGUI.EndChangeCheck())
                rotationProperty.quaternionValue = NormalizeUnityQuaternion(rotation);
        }

        private static void DrawRotationMatrixEditor(SerializedProperty rotationProperty)
        {
            var matrix = Matrix4x4.Rotate(NormalizeUnityQuaternion(rotationProperty.quaternionValue));
            var row0 = new Vector3(matrix.m00, matrix.m01, matrix.m02);
            var row1 = new Vector3(matrix.m10, matrix.m11, matrix.m12);
            var row2 = new Vector3(matrix.m20, matrix.m21, matrix.m22);

            EditorGUI.BeginChangeCheck();
            row0 = EditorGUILayout.Vector3Field("R0", row0);
            row1 = EditorGUILayout.Vector3Field("R1", row1);
            row2 = EditorGUILayout.Vector3Field("R2", row2);
            if (!EditorGUI.EndChangeCheck())
                return;

            var extrinsic = LidarTIlExtrinsic.FromRotationMatrix3x3(
                System.Numerics.Vector3.Zero,
                row0.x, row0.y, row0.z,
                row1.x, row1.y, row1.z,
                row2.x, row2.y, row2.z);
            rotationProperty.quaternionValue = SensorUnitProfile.ToUnityQuaternion(extrinsic.Rotation);
        }

        private static Quaternion DrawQuaternionVector4(string label, Quaternion rotation, bool editable)
        {
            var value = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
            using (new EditorGUI.DisabledScope(!editable))
            {
                value = EditorGUILayout.Vector4Field(label, value);
            }
            return new Quaternion(value.x, value.y, value.z, value.w);
        }

        private static Quaternion NormalizeUnityQuaternion(Quaternion rotation)
            => SensorUnitProfile.ToUnityQuaternion(SensorUnitProfile.ToNumericsQuaternion(rotation));

        private static void DrawSpecPreview(LidarModelSpec spec, string mode)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);

            var columns = spec.Columns;
            var rate = spec.RateHz;
            if (LidarProfileLoader.TryParseMode(mode, out var mc, out var mr))
            {
                columns = mc;
                rate = mr;
            }

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
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawProfilePreview(LidarProfile profile)
        {
            if (profile == null)
                return;

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
