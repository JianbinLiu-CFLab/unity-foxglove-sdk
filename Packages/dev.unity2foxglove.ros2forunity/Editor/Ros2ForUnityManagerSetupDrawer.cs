// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Always-available Manager setup UI for first-time R2FU runtime selection.

using System;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using UnityEditor;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    [InitializeOnLoad]
    internal sealed class Ros2ForUnityManagerSetupDrawer :
        IFoxRunManagerSetupDrawer
    {
        static Ros2ForUnityManagerSetupDrawer()
        {
            FoxRunManagerSetupDrawerRegistry.Register(
                new Ros2ForUnityManagerSetupDrawer());
        }

        public string DrawerId =>
            "unity2foxglove.r2fu.runtime-selection";

        public int Order => 100;

        public void Draw(
            FoxgloveManager manager,
            SerializedObject managerObject)
        {
            _ = manager;
            _ = managerObject;
            try
            {
                Ros2ForUnityRuntimeSelectorInspector
                    .DrawActiveRuntimeSelector();
            }
            catch (Exception exception)
                when (!(exception is OutOfMemoryException)
                      && !(exception is AccessViolationException)
                      && !(exception is ExitGUIException))
            {
                EditorGUILayout.HelpBox(
                    "ROS2 For Unity runtime selection failed. "
                    + "Check the Unity package manifest and installed runtime packages.",
                    MessageType.Warning);
            }
        }
    }
}
