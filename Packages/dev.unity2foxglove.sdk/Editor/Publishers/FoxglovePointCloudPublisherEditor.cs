// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Dedicated Inspector for point-cloud publisher QoS controls.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Util;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    [CustomEditor(typeof(FoxglovePointCloudPublisher))]
    public class FoxglovePointCloudPublisherEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptField();
            DrawGeneralSection();
            DrawPointSourcesSection();
            DrawPointCloudQosSection();
            DrawPublishRateSection();
            DrawEncodingPolicySection();

            serializedObject.ApplyModifiedProperties();

            DrawResolvedSummaries();
        }

        private void DrawScriptField()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }
        }

        private void DrawGeneralSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            DrawProperty("_manager", "Manager");
            DrawProperty("_topic", "Topic");
            DrawProperty("_publishOnEnable", "Publish On Enable");
            DrawProperty("_warnIfManagerMissing", "Warn If Manager Missing");
            DrawProperty("_frameId", "Frame Id");
        }

        private void DrawPointSourcesSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Point Sources", EditorStyles.boldLabel);
            DrawProperty("_pointSources", "Point Sources");
            DrawProperty("_useChildrenWhenSourcesEmpty", "Use Children When Sources Empty");
            DrawProperty("_includeInactiveChildren", "Include Inactive Children");
            DrawProperty("_includeSyntheticIntensity", "Include Synthetic Intensity");
        }

        private void DrawPointCloudQosSection()
        {
            var samplingMode = serializedObject.FindProperty("_samplingMode");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Point Cloud QoS", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Max Packed Bytes budgets the PointCloud.data payload before publish. Set it to 0 to disable the byte budget.",
                MessageType.Info);
            DrawProperty("_maxPoints", "Max Points");
            DrawProperty("_maxPackedBytes", "Max Packed Bytes");
            EditorGUILayout.PropertyField(samplingMode, new GUIContent("Sampling Mode"));

            if (samplingMode != null && samplingMode.enumValueIndex == (int)PointCloudSamplingMode.VoxelGrid)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_voxelSizeMeters"), new GUIContent("Voxel Size Meters"));
                EditorGUILayout.HelpBox(
                    "VoxelGrid keeps the first source point in each occupied voxel so optional point fields keep their original values.",
                    MessageType.Info);
            }

            DrawProperty("_logQosDrops", "Log QoS Drops");
            EditorGUILayout.HelpBox(
                "Heavy point-cloud work is skipped when there is no live subscriber or active MCAP recording demand.",
                MessageType.Info);
        }

        private void DrawPublishRateSection()
        {
            var publishRateSource = serializedObject.FindProperty("_publishRateSource");
            var publishRateHz = serializedObject.FindProperty("_publishRateHz");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish Rate", EditorStyles.boldLabel);
            if (publishRateSource != null)
                EditorGUILayout.PropertyField(publishRateSource, new GUIContent("Publish Rate Source"));

            var usesLocalRate = publishRateSource == null
                || publishRateSource.enumValueIndex == (int)PublisherRateSource.OverrideLocal;
            using (new EditorGUI.DisabledScope(!usesLocalRate))
            {
                if (publishRateHz != null)
                    EditorGUILayout.PropertyField(publishRateHz, new GUIContent("Publish Rate Hz"));
            }
        }

        private void DrawEncodingPolicySection()
        {
            var encodingOverride = serializedObject.FindProperty("_encodingOverride");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encoding Policy", EditorStyles.boldLabel);
            if (encodingOverride != null)
                EditorGUILayout.PropertyField(encodingOverride, new GUIContent("Encoding Override"));
        }

        private void DrawResolvedSummaries()
        {
            var publisher = (FoxglovePublisherBase)target;
            var resolution = publisher.EncodingResolution;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("Effective Publish Rate Hz", publisher.EffectivePublishRateHz);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Supported Encodings", publisher.SupportedEncodingSummary);
                EditorGUILayout.EnumPopup("Effective Encoding", resolution.Effective);
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
        }

        private void DrawProperty(string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(label), true);
        }
    }
}
