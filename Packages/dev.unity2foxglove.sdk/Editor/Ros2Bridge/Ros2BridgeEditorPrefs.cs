// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Ros2Bridge
// Purpose: EditorPrefs keys for ROS2 Bridge diagnostics.

using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Persists the optional ros2 executable path selected for bridge health diagnostics.</summary>
    internal static class Ros2BridgeEditorPrefs
    {
        private const string Ros2ExecutablePathKey = "Unity2Foxglove.Ros2Bridge.Ros2ExecutablePath";
        private static readonly string[] LegacyRos2ExecutablePathKeys =
        {
            "Unity2Foxglove.Ros2Bridge.Ros2Path",
            "Unity2Foxglove.Ros2Bridge.Diagnostics.Ros2ExecutablePath"
        };

        internal static string Ros2ExecutablePath
        {
            get
            {
                MigrateLegacyRos2ExecutablePath();
                return EditorPrefs.GetString(Ros2ExecutablePathKey, string.Empty);
            }
            set => EditorPrefs.SetString(Ros2ExecutablePathKey, value ?? string.Empty);
        }

        internal static void ClearRos2ExecutablePath()
        {
            EditorPrefs.DeleteKey(Ros2ExecutablePathKey);
            foreach (var key in LegacyRos2ExecutablePathKeys)
                EditorPrefs.DeleteKey(key);
        }

        private static void MigrateLegacyRos2ExecutablePath()
        {
            if (EditorPrefs.HasKey(Ros2ExecutablePathKey))
                return;

            foreach (var key in LegacyRos2ExecutablePathKeys)
            {
                if (!EditorPrefs.HasKey(key))
                    continue;

                EditorPrefs.SetString(
                    Ros2ExecutablePathKey,
                    EditorPrefs.GetString(key, string.Empty));
                EditorPrefs.DeleteKey(key);
                break;
            }
        }
    }
}
