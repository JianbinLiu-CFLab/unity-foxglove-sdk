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

        private const int CovarianceMatrixSize = 3;
        private const int CovarianceElementCount = CovarianceMatrixSize * CovarianceMatrixSize;
        private static readonly GUIContent ManagerLabel = new GUIContent("Manager");
        private static readonly GUIContent RigidbodyLabel = new GUIContent("Rigidbody");
        private static readonly GUIContent TopicLabel = new GUIContent("Topic");
        private static readonly GUIContent FrameIdLabel = new GUIContent("Frame Id");
        private static readonly GUIContent PublishRateSourceLabel = new GUIContent("Publish Rate Source");
        private static readonly GUIContent SampleRateLabel = new GUIContent("Sample Rate Hz");
        private static readonly GUIContent WebSocketSamplesLabel = new GUIContent("WebSocket Max Samples / Frame");
        private static readonly GUIContent PublishOrientationLabel = new GUIContent("Publish Orientation");
        private static readonly GUIContent PhysicsRateOverrideLabel = new GUIContent("Override Unity Physics Rate Hz");

        private bool _showAdvancedImuModel;
        private SerializedProperty _scriptProperty;
        private SerializedProperty _managerProperty;
        private SerializedProperty _rigidbodyProperty;
        private SerializedProperty _topicProperty;
        private SerializedProperty _frameIdProperty;
        private SerializedProperty _publishRateSourceProperty;
        private SerializedProperty _targetRateHzProperty;
        private SerializedProperty _maxWebSocketSamplesPerFrameProperty;
        private SerializedProperty _includeOrientationProperty;
        private SerializedProperty _orientationCovarianceProperty;
        private SerializedProperty _angularVelocityCovarianceProperty;
        private SerializedProperty _linearAccelerationCovarianceProperty;
        private SerializedProperty _globalPhysicsRateHzOverrideProperty;
        private readonly SerializedProperty[] _orientationCovarianceElements = new SerializedProperty[CovarianceElementCount];
        private readonly SerializedProperty[] _angularVelocityCovarianceElements = new SerializedProperty[CovarianceElementCount];
        private readonly SerializedProperty[] _linearAccelerationCovarianceElements = new SerializedProperty[CovarianceElementCount];

        private void OnEnable()
        {
            CacheProperties();
        }

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
                if (_scriptProperty != null)
                    EditorGUILayout.PropertyField(_scriptProperty);
            }
        }

        private void DrawImuOutputSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("IMU Output", EditorStyles.boldLabel);
            DrawProperty(_managerProperty, ManagerLabel);
            DrawProperty(_rigidbodyProperty, RigidbodyLabel);
            DrawProperty(_topicProperty, TopicLabel);
            DrawProperty(_frameIdProperty, FrameIdLabel);
        }

        private void DrawPublishRateSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish Rate", EditorStyles.boldLabel);

            if (_publishRateSourceProperty != null)
            {
                _publishRateSourceProperty.enumValueIndex = EditorGUILayout.Popup(
                    PublishRateSourceLabel,
                    _publishRateSourceProperty.enumValueIndex,
                    PublishRateSourceLabels);
            }

            var usesLocalRate = _publishRateSourceProperty == null
                                || _publishRateSourceProperty.enumValueIndex == (int)PublisherRateSource.OverrideLocal;
            using (new EditorGUI.DisabledScope(!usesLocalRate))
            {
                DrawProperty(_targetRateHzProperty, SampleRateLabel);
            }

            DrawProperty(_maxWebSocketSamplesPerFrameProperty, WebSocketSamplesLabel);

            EditorGUILayout.HelpBox(
                "Set WebSocket Max Samples / Frame high enough for the sample rate and lowest expected Game view FPS. Example: 640Hz needs at least 16 at 40 FPS, or 32 at 20 FPS. 0 disables the WebSocket cap.",
                MessageType.Info);
        }

        private void DrawAdvancedImuModelSection()
        {
            EditorGUILayout.Space();
            _showAdvancedImuModel = EditorGUILayout.Foldout(_showAdvancedImuModel, "Advanced IMU Model", true);
            if (!_showAdvancedImuModel)
                return;

            DrawProperty(_includeOrientationProperty, PublishOrientationLabel);
            DrawCovarianceMatrix(_orientationCovarianceProperty, _orientationCovarianceElements, "Orientation Covariance");
            DrawCovarianceMatrix(_angularVelocityCovarianceProperty, _angularVelocityCovarianceElements, "Angular Velocity Covariance");
            DrawCovarianceMatrix(_linearAccelerationCovarianceProperty, _linearAccelerationCovarianceElements, "Linear Acceleration Covariance");

            EditorGUILayout.Space();
            DrawProperty(_globalPhysicsRateHzOverrideProperty, PhysicsRateOverrideLabel);
            EditorGUILayout.HelpBox(
                "Leave at 0 for normal use. A positive value changes Unity Time.fixedDeltaTime globally and can affect physics, vehicle control, and performance.",
                MessageType.Warning);
        }

        private static void DrawProperty(SerializedProperty property, GUIContent label)
        {
            if (property != null)
                EditorGUILayout.PropertyField(property, label);
        }

        private void DrawCovarianceMatrix(SerializedProperty property, SerializedProperty[] elements, string label)
        {
            if (property == null)
                return;

            if (!property.isArray || property.arraySize != CovarianceElementCount)
            {
                property.arraySize = CovarianceElementCount;
                CacheCovarianceElements(property, elements);
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            for (var row = 0; row < CovarianceMatrixSize; row++)
            {
                var rowRect = EditorGUILayout.GetControlRect();
                var matrixRect = new Rect(
                    rowRect.x + EditorGUIUtility.labelWidth,
                    rowRect.y,
                    rowRect.width - EditorGUIUtility.labelWidth,
                    rowRect.height);
                var spacing = EditorGUIUtility.standardVerticalSpacing;
                var cellWidth = (matrixRect.width - spacing * (CovarianceMatrixSize - 1)) / CovarianceMatrixSize;

                for (var column = 0; column < CovarianceMatrixSize; column++)
                {
                    var index = row * CovarianceMatrixSize + column;
                    var element = elements[index];
                    if (element == null)
                    {
                        CacheCovarianceElements(property, elements);
                        element = elements[index];
                    }

                    var cellRect = new Rect(
                        matrixRect.x + column * (cellWidth + spacing),
                        matrixRect.y,
                        cellWidth,
                        matrixRect.height);
                    element.doubleValue = EditorGUI.DoubleField(cellRect, element.doubleValue);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void CacheProperties()
        {
            _scriptProperty = serializedObject.FindProperty("m_Script");
            _managerProperty = serializedObject.FindProperty("_manager");
            _rigidbodyProperty = serializedObject.FindProperty("_rigidbody");
            _topicProperty = serializedObject.FindProperty("_topic");
            _frameIdProperty = serializedObject.FindProperty("_frameId");
            _publishRateSourceProperty = serializedObject.FindProperty("_publishRateSource");
            _targetRateHzProperty = serializedObject.FindProperty("_targetRateHz");
            _maxWebSocketSamplesPerFrameProperty = serializedObject.FindProperty("_maxWebSocketSamplesPerFrame");
            _includeOrientationProperty = serializedObject.FindProperty("_includeOrientation");
            _orientationCovarianceProperty = serializedObject.FindProperty("_imuOrientationCovariance");
            _angularVelocityCovarianceProperty = serializedObject.FindProperty("_imuAngularVelocityCovariance");
            _linearAccelerationCovarianceProperty = serializedObject.FindProperty("_imuLinearAccelerationCovariance");
            _globalPhysicsRateHzOverrideProperty = serializedObject.FindProperty("_globalPhysicsRateHzOverride");

            CacheCovarianceElements(_orientationCovarianceProperty, _orientationCovarianceElements);
            CacheCovarianceElements(_angularVelocityCovarianceProperty, _angularVelocityCovarianceElements);
            CacheCovarianceElements(_linearAccelerationCovarianceProperty, _linearAccelerationCovarianceElements);
        }

        private static void CacheCovarianceElements(SerializedProperty property, SerializedProperty[] elements)
        {
            for (var i = 0; i < elements.Length; i++)
                elements[i] = property != null && property.isArray && i < property.arraySize
                    ? property.GetArrayElementAtIndex(i)
                    : null;
        }
    }
}
