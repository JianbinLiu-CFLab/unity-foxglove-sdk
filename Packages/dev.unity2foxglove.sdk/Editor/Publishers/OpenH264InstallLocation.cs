// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Safe per-user OpenH264 install root selection.

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public static class OpenH264InstallLocation
    {
        private const string EditorPrefsKey = "Unity2Foxglove.OpenH264.InstallRoot";

        public static string GetPreferredInstallRoot()
        {
            var saved = EditorPrefs.GetString(EditorPrefsKey, "");
            if (!string.IsNullOrWhiteSpace(saved))
                return saved;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                localAppData = Application.temporaryCachePath;

            return Path.Combine(localAppData, "Unity2Foxglove", "OpenH264");
        }

        public static void SavePreferredInstallRoot(string root)
        {
            if (!string.IsNullOrWhiteSpace(root))
                EditorPrefs.SetString(EditorPrefsKey, root);
        }

        public static string GetFinalDllPath(string root)
            => Path.Combine(GetVersionedDirectory(root), OpenH264OfficialBinaryManifest.DllFileName);

        public static string GetFinalHelperPath(string root)
            => Path.Combine(GetVersionedDirectory(root), OpenH264OfficialBinaryManifest.HelperFileName);

        public static string GetVersionedDirectory(string root)
            => Path.Combine(string.IsNullOrWhiteSpace(root) ? GetPreferredInstallRoot() : root, OpenH264OfficialBinaryManifest.Version);

        public static bool IsAllowedInstallRoot(string root, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(root))
            {
                reason = "Choose an OpenH264 install location.";
                return false;
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
            }
            catch (Exception ex)
            {
                reason = "Install location is invalid: " + ex.Message;
                return false;
            }

            if (IsUnderSpecialFolder(fullRoot, Environment.SpecialFolder.ProgramFiles)
                || IsUnderSpecialFolder(fullRoot, Environment.SpecialFolder.ProgramFilesX86))
            {
                reason = "Choose a per-user folder instead of Program Files.";
                return false;
            }

            var projectRoot = GetProjectRoot();
            foreach (var projectChild in new[] { "Assets", "Packages", "ProjectSettings", ".git" })
            {
                if (IsSameOrChild(fullRoot, Path.Combine(projectRoot, projectChild)))
                {
                    reason = "Choose a cache folder outside the Unity project, not " + projectChild + ".";
                    return false;
                }
            }

            return true;
        }

        private static string GetProjectRoot()
        {
            var assets = Application.dataPath;
            return string.IsNullOrEmpty(assets) ? "" : Directory.GetParent(assets)?.FullName ?? "";
        }

        private static bool IsUnderSpecialFolder(string path, Environment.SpecialFolder folder)
        {
            var special = Environment.GetFolderPath(folder);
            return !string.IsNullOrWhiteSpace(special) && IsSameOrChild(path, special);
        }

        private static bool IsSameOrChild(string path, string parent)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parent))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var comparison = Application.platform == RuntimePlatform.WindowsEditor
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return fullPath.Equals(fullParent, comparison)
                    || fullPath.StartsWith(fullParent + Path.DirectorySeparatorChar, comparison)
                    || fullPath.StartsWith(fullParent + Path.AltDirectorySeparatorChar, comparison);
            }
            catch
            {
                return false;
            }
        }
    }
}
