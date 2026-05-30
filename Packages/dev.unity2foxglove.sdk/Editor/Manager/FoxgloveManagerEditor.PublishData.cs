// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager

using System.IO;
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
            FoxgloveManagerInspectorLayout.Subheader("Output Mode");
            DrawProperty("_foxgloveOutputEnabled", "Foxglove WebSocket");
            DrawProperty("_ros2NativeEnabled", "ROS2 Native (R2FU)");
            DrawProperty("_ros2BridgeEnabled", "ROS2 Bridge");

            EditorGUILayout.Space();
            FoxgloveManagerInspectorLayout.Subheader("Publish Rate");
            DrawFloatProperty(
                "_defaultPublishRateHz",
                "Default Publish Rate Hz",
                "Default publish rate used by publishers that choose the manager default. Use <= 0 to publish every eligible frame.");

            FoxgloveManagerInspectorLayout.Subheader("Publisher Encoding");
            DrawGlobalEncodingProperty("_defaultPublisherEncoding", "Default Publisher Encoding");
            DrawProperty("_allowPublisherOverride");

            DrawProperty("_coordinateMode");

            FoxgloveManagerInspectorLayout.Subheader("Assets");
            DrawProperty("_assetRoots");
        }
    }
}
