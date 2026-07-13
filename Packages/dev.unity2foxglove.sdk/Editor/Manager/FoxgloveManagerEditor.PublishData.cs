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
        private const string R2fuRuntimeSelectorInspectorTypeName =
            "Unity2Foxglove.Ros2" + "ForUnity.Editor.Ros2" + "ForUnityRuntimeSelectorInspector, Unity2Foxglove.Ros2" + "ForUnity.Editor";
        private static bool _r2fuRuntimeSelectorResolved;
        private static System.Reflection.MethodInfo _r2fuRuntimeSelectorDrawMethod;

        private static void ResetOptionalR2fuRuntimeSelectorCache()
        {
            _r2fuRuntimeSelectorResolved = false;
            _r2fuRuntimeSelectorDrawMethod = null;
        }

        private void DrawPublishDataSection()
        {
            FoxgloveManagerInspectorLayout.Subheader("Output Mode");
            DrawProperty("_foxgloveOutputEnabled", "Foxglove WebSocket");
            DrawProperty("_ros2NativeEnabled", "ROS2 Native (R2FU)");
            DrawOptionalR2fuRuntimeSelector();
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

            FoxgloveManagerInspectorLayout.Subheader("FoxRun Publish Encoding");
            FoxRunEncodingEditorLabels.DrawFoxRunWireEncoding(
                FindCachedProperty("_defaultFoxRunPublishEncoding"),
                "Default FoxRun Publish Encoding");

            DrawProperty("_coordinateMode");

            FoxgloveManagerInspectorLayout.Subheader("Assets");
            DrawProperty("_assetRoots");
        }

        private void DrawOptionalR2fuRuntimeSelector()
        {
            var ros2Native = FindCachedProperty("_ros2NativeEnabled");
            if (ros2Native == null || !ros2Native.boolValue)
                return;

            var drawMethod = ResolveR2fuRuntimeSelectorDrawMethod();
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

        private static System.Reflection.MethodInfo ResolveR2fuRuntimeSelectorDrawMethod()
        {
            if (_r2fuRuntimeSelectorResolved)
                return _r2fuRuntimeSelectorDrawMethod;

            _r2fuRuntimeSelectorResolved = true;
            var selectorType = System.Type.GetType(R2fuRuntimeSelectorInspectorTypeName);
            _r2fuRuntimeSelectorDrawMethod = selectorType?.GetMethod(
                "DrawActiveRuntimeSelector",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return _r2fuRuntimeSelectorDrawMethod;
        }
    }
}
