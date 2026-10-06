// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Register a generated custom typesupport add-on's Editor-native directory before ROS2 loads it.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

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
        private static readonly object processPathMutex = new object();
        private static readonly HashSet<string> ownedProcessPathEntries =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string previousProcessPath;
        private static string appliedProcessPath;
        private static string pendingRestorePath;
        private static bool hasPendingRestorePath;
        private static bool processPathOwned;
        private static bool processPathRestorePending;

#if UNITY_EDITOR_WIN
        [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int _wputenv_s(string name, string value);
#endif

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
            lock (processPathMutex)
            {
                if (processPathRestorePending)
                    throw new InvalidOperationException(
                        "The custom typesupport process PATH lease is still pending cleanup.");

                var currentPath = Environment.GetEnvironmentVariable("PATH");
                var currentPathForSplit = currentPath ?? string.Empty;
                foreach (var entry in currentPathForSplit.Split(Path.PathSeparator))
                {
                    if (string.Equals(entry.Trim(), pluginDirectory, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                if (!processPathOwned)
                {
                    previousProcessPath = currentPath;
                    ownedProcessPathEntries.Clear();
                }

                var updatedPath = string.IsNullOrEmpty(currentPath)
                    ? pluginDirectory
                    : currentPath + Path.PathSeparator + pluginDirectory;
                var wasOwned = processPathOwned;
                ownedProcessPathEntries.Add(pluginDirectory);
                appliedProcessPath = updatedPath;
                processPathOwned = true;
                try
                {
                    SetProcessPath(updatedPath);
                }
                catch
                {
                    try
                    {
                        SetProcessPath(currentPath);
                        if (wasOwned)
                        {
                            ownedProcessPathEntries.Remove(pluginDirectory);
                            appliedProcessPath = currentPath;
                            processPathRestorePending = false;
                            hasPendingRestorePath = false;
                            pendingRestorePath = null;
                        }
                        else
                        {
                            ClearProcessPathOwnership();
                        }
                    }
                    catch
                    {
                        processPathRestorePending = true;
                        hasPendingRestorePath = true;
                        pendingRestorePath = currentPath;
                    }
                    throw;
                }
            }
        }

        /// <summary>
        /// Restores the process PATH only when it still contains the value last
        /// written by this bootstrap. A different owner therefore wins.
        /// </summary>
        public static bool RestoreEditorProcessPath()
        {
            lock (processPathMutex)
            {
                if (!processPathOwned)
                    return true;

                var currentPath = Environment.GetEnvironmentVariable("PATH");
                if (processPathRestorePending
                    && hasPendingRestorePath
                    && string.Equals(currentPath, pendingRestorePath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        SetProcessPathNative(pendingRestorePath);
                        ClearProcessPathOwnership();
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                string targetPath;
                if (!string.Equals(currentPath, appliedProcessPath, StringComparison.OrdinalIgnoreCase)
                    && TryRemoveOwnedPathEntries(currentPath, out targetPath))
                {
                    try
                    {
                        SetProcessPath(targetPath);
                        ClearProcessPathOwnership();
                        return true;
                    }
                    catch
                    {
                        processPathRestorePending = true;
                        hasPendingRestorePath = true;
                        pendingRestorePath = targetPath;
                        return false;
                    }
                }
                else if (!string.Equals(currentPath, appliedProcessPath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // Preserve the caller-managed PATH while synchronizing the
                        // native CRT view before releasing this lease.
                        SetProcessPathNative(currentPath);
                        ClearProcessPathOwnership();
                        return true;
                    }
                    catch
                    {
                        processPathRestorePending = true;
                        hasPendingRestorePath = true;
                        pendingRestorePath = currentPath;
                        return false;
                    }
                }
                else
                {
                    if (TryRemoveOwnedPathEntries(currentPath, out targetPath))
                    {
                        try
                        {
                            SetProcessPath(targetPath);
                            ClearProcessPathOwnership();
                            return true;
                        }
                        catch
                        {
                            processPathRestorePending = true;
                            hasPendingRestorePath = true;
                            pendingRestorePath = targetPath;
                            return false;
                        }
                    }

                    targetPath = previousProcessPath;
                    try
                    {
                        SetProcessPath(targetPath);
                        ClearProcessPathOwnership();
                        return true;
                    }
                    catch
                    {
                        processPathRestorePending = true;
                        hasPendingRestorePath = true;
                        pendingRestorePath = targetPath;
                        return false;
                    }
                }
            }
        }

        private static void ClearProcessPathOwnership()
        {
            processPathOwned = false;
            processPathRestorePending = false;
            hasPendingRestorePath = false;
            ownedProcessPathEntries.Clear();
            previousProcessPath = null;
            appliedProcessPath = null;
            pendingRestorePath = null;
        }

        private static bool TryRemoveOwnedPathEntries(string currentPath, out string restoredPath)
        {
            restoredPath = currentPath;
            if (string.IsNullOrEmpty(currentPath) || ownedProcessPathEntries.Count == 0)
                return false;

            var kept = new System.Collections.Generic.List<string>();
            var remainingOwned = new HashSet<string>(
                ownedProcessPathEntries,
                StringComparer.OrdinalIgnoreCase);
            var removed = false;
            foreach (var entry in currentPath.Split(Path.PathSeparator))
            {
                if (remainingOwned.Remove(entry.Trim()))
                {
                    removed = true;
                    continue;
                }
                kept.Add(entry);
            }

            if (!removed)
                return false;

            restoredPath = kept.Count == 0 ? string.Empty : string.Join(Path.PathSeparator.ToString(), kept);
            return !string.Equals(restoredPath, currentPath, StringComparison.OrdinalIgnoreCase);
        }

        private static void SetProcessPath(string value)
        {
            Environment.SetEnvironmentVariable("PATH", value);
#if UNITY_EDITOR_WIN
            SetProcessPathNative(value);
#endif
        }

        private static void SetProcessPathNative(string value)
        {
#if UNITY_EDITOR_WIN
            var result = _wputenv_s("PATH", value ?? string.Empty);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    "Failed to set Windows CRT environment variable 'PATH' (ucrtbase _wputenv_s returned "
                    + result
                    + ").");
            }
#endif
        }
#endif

    }
}
#endif
