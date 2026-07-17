// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager

using Unity.FoxgloveSDK.Core;
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
            if (GetBool("_ros2NativeEnabled"))
                EditorGUILayout.HelpBox(
                    "This Manager has no global ROS2 Native publish QoS override; configure QoS on individual R2FU publishers.",
                    MessageType.Info);
            DrawProperty("_ros2BridgeEnabled", "ROS 2 Bridge");
            if (GetBool("_ros2BridgeEnabled"))
            {
                DrawDataTransportSubsection(
                    "ROS 2 Bridge Output",
                    "DataTransportRos2Bridge",
                    ref _dataTransportRos2BridgeExpanded,
                    DrawRos2BridgeSection);
            }

            EditorGUILayout.Space();
            FoxgloveManagerInspectorLayout.Subheader("Publish Rate");
            DrawFloatProperty(
                "_defaultPublishRateHz",
                "Default Publish Rate Hz",
                "Default publish rate used by publishers that choose the manager default. Use <= 0 to publish every eligible frame.");

            FoxgloveManagerInspectorLayout.Subheader("Publisher Encoding");
            DrawGlobalEncodingProperty("_defaultPublisherEncoding", "Component Publisher Encoding");
            DrawProperty("_allowPublisherOverride", "Allow Component Publisher Override");
            FoxRunEncodingEditorLabels.DrawFoxRunWireEncoding(
                FindCachedProperty("_defaultFoxRunPublishEncoding"),
                "FoxRun Contract Encoding");
            EditorGUILayout.HelpBox(
                "Component publishers and generated FoxRun contracts use independent default encodings.",
                MessageType.Info);

            DrawProperty("_coordinateMode");

            FoxgloveManagerInspectorLayout.Subheader("Assets");
            DrawProperty("_assetRoots");
        }

    }
}
