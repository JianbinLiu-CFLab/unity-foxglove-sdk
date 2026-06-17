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
            DrawOptionalRos2ForUnityRuntimeSelector();
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

        private void DrawOptionalRos2ForUnityRuntimeSelector()
        {
            var ros2Native = serializedObject.FindProperty("_ros2NativeEnabled");
            if (ros2Native == null || !ros2Native.boolValue)
                return;

            var selectorType = System.Type.GetType(
                "Unity2Foxglove.Ros2ForUnity.Editor.Ros2ForUnityRuntimeSelectorInspector, Unity2Foxglove.Ros2ForUnity.Editor");
            var drawMethod = selectorType?.GetMethod(
                "DrawActiveRuntimeSelector",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (drawMethod == null)
            {
                EditorGUILayout.HelpBox(
                    "Install the Unity2Foxglove ROS2 For Unity adapter package to select an active R2FU runtime.",
                    MessageType.Info);
                return;
            }

            try
            {
                drawMethod.Invoke(null, null);
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
            {
                EditorGUILayout.HelpBox(
                    "ROS2 For Unity runtime selector failed: "
                    + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message,
                    MessageType.Warning);
            }
            catch (System.Exception ex)
            {
                EditorGUILayout.HelpBox(
                    "ROS2 For Unity runtime selector failed: " + ex.GetType().Name + ": " + ex.Message,
                    MessageType.Warning);
            }
        }
    }
}
