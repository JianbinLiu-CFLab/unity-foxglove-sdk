// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Register a generated custom typesupport add-on's Editor-native directory before ROS2 loads it.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.IO;
using System.Reflection;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Resolves a generated add-on package and delegates native registration
    /// to the selected R2FU runtime before a custom message can be resolved.
    /// </summary>
    /// <remarks>
    /// In a Player, Unity places the selected runtime and generated add-on
    /// libraries in the same Plugins directory already owned by R2FU. In an
    /// Editor, the add-on remains in its Package Manager location, so it must
    /// be registered explicitly before the first ROS2 native load.
    /// </remarks>
    public static class FoxRunRos2CustomTypesupportNativePluginBootstrap
    {
        /// <summary>
        /// Register the selected generated add-on package through R2FU's
        /// Editor-native bootstrap. This is intentionally a metadata/path
        /// operation: it creates no ROS2 node, executor, or transport endpoint.
        /// </summary>
        public static void Register(Assembly addOnAssembly)
        {
#if UNITY_EDITOR
            if (addOnAssembly == null)
                return;

            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(addOnAssembly);
                if (package == null
                    || string.IsNullOrWhiteSpace(package.resolvedPath))
                {
                    return;
                }

                if (ROS2.Ros2ForUnityNativePluginBootstrap.RegisterEditorPackagePluginDirectory(package.resolvedPath))
                {
                    AppendSelectedAddOnPluginDirectoryToProcessPath(package.resolvedPath);
                }
            }
            catch (Exception)
            {
                // A generated startup callback must remain inert when Package
                // Manager metadata is unavailable. The normal preflight emits
                // the user-facing readiness result before native endpoints run.
            }
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Makes sibling DLLs of the selected custom typesupport visible to the Windows loader.
        /// </summary>
        /// <remarks>
        /// ros2cs loads a selected library by its exact path, but Windows resolves that
        /// library's native siblings through the process search path.  Append rather than
        /// prepend so the selected R2FU runtime remains authoritative for shared libraries.
        /// </remarks>
        private static void AppendSelectedAddOnPluginDirectoryToProcessPath(string packageRoot)
        {
            var pluginDirectory = Path.Combine(
                Path.GetFullPath(packageRoot),
                "Runtime",
                "Ros2ForUnity",
                "Plugins",
                "Windows",
                "x86_64");
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var entry in currentPath.Split(Path.PathSeparator))
            {
                if (string.Equals(entry.Trim(), pluginDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            Environment.SetEnvironmentVariable(
                "PATH",
                string.IsNullOrEmpty(currentPath)
                    ? pluginDirectory
                    : currentPath + Path.PathSeparator + pluginDirectory);
        }
#endif

    }
}
#endif
