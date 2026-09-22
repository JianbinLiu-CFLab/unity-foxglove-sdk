// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    /// <summary>Stages selected R2FU metadata and share resources into Player StreamingAssets.
    /// </summary>
    internal sealed class Ros2ForUnityRuntimePlayerStaging : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string StagingRelativePath = "Assets/StreamingAssets/Ros2ForUnity";
        private const string PackagePrefix = "dev.unity2foxglove.ros2forunity.runtime.";
        public int callbackOrder => 50;

        static Ros2ForUnityRuntimePlayerStaging()
        {
            EditorApplication.delayCall += CleanupStaleStaging;
        }

        private static void CleanupStaleStaging()
        {
            var destination = Path.Combine(Application.dataPath, "StreamingAssets", "Ros2ForUnity");
            var marker = Path.Combine(destination, ".unity2foxglove-staged");
            if (File.Exists(marker))
            {
                Directory.Delete(destination, true);
                AssetDatabase.Refresh();
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64)
                return;
            var project = Directory.GetParent(Application.dataPath)?.FullName;
            var package = FindActiveRuntimePackage(project);
            if (package == null)
                throw new BuildFailedException("No active Windows ROS2 For Unity runtime package was found.");
            var source = Path.Combine(package, "Runtime", "Ros2ForUnity");
            var destination = Path.Combine(Application.dataPath, "StreamingAssets", "Ros2ForUnity");
            if (!Directory.Exists(source))
                throw new BuildFailedException("Selected ROS2 For Unity runtime has no Runtime/Ros2ForUnity payload: " + package);
            CleanupStaleStaging();
            Directory.CreateDirectory(destination);
            CopyIfPresent(Path.Combine(source, "metadata_ros2_for_unity.xml"), destination);
            CopyIfPresent(Path.Combine(source, "metadata_ros2cs.xml"), destination);
            var share = Path.Combine(source, "Plugins", "Windows", "x86_64", "share");
            if (!Directory.Exists(share))
                throw new BuildFailedException("Selected ROS2 For Unity runtime has no plugin share payload: " + share);
            CopyDirectory(share, Path.Combine(destination, "share"));
            var packagedStreamingAssets = Path.Combine(source, "StreamingAssets", "Ros2ForUnity");
            if (Directory.Exists(packagedStreamingAssets))
                CopyDirectory(packagedStreamingAssets, destination);
            File.WriteAllText(Path.Combine(destination, ".unity2foxglove-staged"), package);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var destination = Path.Combine(Application.dataPath, "StreamingAssets", "Ros2ForUnity");
            var marker = Path.Combine(destination, ".unity2foxglove-staged");
            if (!File.Exists(marker))
                return;
            Directory.Delete(destination, true);
            AssetDatabase.Refresh();
        }

        private static string FindActiveRuntimePackage(string project)
        {
            if (string.IsNullOrWhiteSpace(project)) return null;
            var manifest = Path.Combine(project, "Packages", "manifest.json");
            if (!File.Exists(manifest)) return null;
            var text = File.ReadAllText(manifest);
            var start = text.IndexOf(PackagePrefix, StringComparison.Ordinal);
            if (start < 0) return null;
            var end = text.IndexOf('"', start);
            if (end < 0) return null;
            var packageName = text.Substring(start, end - start);
            var path = Path.Combine(project, "Packages", packageName);
            return Directory.Exists(path) ? path : null;
        }

        private static void CopyIfPresent(string source, string destination)
        {
            if (File.Exists(source)) File.Copy(source, Path.Combine(destination, Path.GetFileName(source)), true);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
#endif
