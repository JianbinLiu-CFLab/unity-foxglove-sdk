// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager

using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor : UnityEditor.Editor
    {
        private void DrawPublishDataSection()
        {
            FoxgloveManagerInspectorLayout.Subheader("Publish Destinations");
            DrawProperty("_foxgloveOutputEnabled", "Foxglove WebSocket");
            DrawProperty("_ros2NativeEnabled", "ROS 2 Native (R2FU)");
            DrawProperty("_ros2BridgeEnabled", "ROS 2 Bridge");

            EditorGUILayout.Space();
            FoxgloveManagerInspectorLayout.Subheader("FoxRun Publish Profile");
            var targets = FoxRunEndpointEditorLabels.DrawTargets(
                FindCachedProperty("_defaultFoxRunPublishTargets"),
                "Targets");
            var includesFoxglove = FoxRunEndpointEditorModel.Includes(
                targets,
                FoxRunEndpoint.Foxglove);
            var includesRos2Native = FoxRunEndpointEditorModel.Includes(
                targets,
                FoxRunEndpoint.Ros2Native);
            var includesRos2Bridge = FoxRunEndpointEditorModel.Includes(
                targets,
                FoxRunEndpoint.Ros2Bridge);

            if (includesFoxglove)
            {
                FoxRunEncodingEditorLabels.DrawFoxRunEncoding(
                    FindCachedProperty("_defaultFoxRunPublishEncoding"),
                    "Foxglove Encoding");
            }

            if (includesRos2Native)
            {
                DrawFoxRunRos2Qos(
                    FindCachedProperty("_defaultFoxRunNativePublishQos"),
                    "ROS 2 Native QoS Profile");
                EditorGUILayout.HelpBox(
                    "FoxRun resolves the ROS 2 message type automatically from the generated contract.",
                    MessageType.Info);
            }

            if (includesRos2Bridge)
            {
                EditorGUILayout.HelpBox(
                    "FoxRun resolves the ROS 2 message type automatically and uses the shared ROS 2 Bridge connection and QoS settings below.",
                    MessageType.Info);
            }

            DrawFloatProperty(
                "_defaultPublishRateHz",
                "Default Publish Rate Hz",
                "Default publish rate used by publishers that choose the manager default. Use <= 0 to publish every eligible frame.");
            var manager = target as FoxgloveManager;
            if (manager != null && manager.ActiveFoxRunPublishSessionPolicy.SessionActive)
            {
                EditorGUILayout.HelpBox(
                    "FoxRun Publish Profile changes apply after this Manager is disabled and re-enabled. Restarting one transport does not recapture the active profile.",
                    MessageType.Info);
            }

            if (GetBool("_ros2BridgeEnabled") || includesRos2Bridge)
            {
                DrawDataTransportSubsection(
                    "ROS 2 Bridge Output",
                    "DataTransportRos2Bridge",
                    ref _dataTransportRos2BridgeExpanded,
                    DrawRos2BridgeSection);
            }

            FoxgloveManagerInspectorLayout.Subheader("Publisher Encoding");
            DrawGlobalEncodingProperty("_defaultPublisherEncoding", "Component Publisher Encoding");
            DrawProperty("_allowPublisherOverride", "Allow Component Publisher Override");
            EditorGUILayout.HelpBox(
                "Component publishers and generated FoxRun contracts use independent default encodings.",
                MessageType.Info);

            FoxgloveManagerInspectorLayout.Subheader("Coordinate System");
            DrawProperty("_outputCoordinateMode", "Output Coordinate Mode");
            EditorGUILayout.HelpBox(
                "Defines the coordinate convention of supported data published from Unity. MCAP records the same converted external payload and labels output channels with this mode.",
                MessageType.Info);

            FoxgloveManagerInspectorLayout.Subheader("Assets");
            DrawProperty("_assetRoots");
        }

    }
}
