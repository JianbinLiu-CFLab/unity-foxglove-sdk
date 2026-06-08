// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Enables the optional ROS2 For Unity compile path when the runtime package is installed.

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    [InitializeOnLoad]
    internal static class Ros2ForUnityRuntimeDefineInstaller
    {
        private const string RuntimePackageName = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
        private const string BaseCompileSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";
        private const string NativeRuntimePackageCompileSymbol =
            "UNITY2FOXGLOVE_ROS2_FOR_UNITY_JAZZY_WIN64_PACKAGE";

        static Ros2ForUnityRuntimeDefineInstaller()
        {
            EditorApplication.delayCall += ReconcileCompileSymbolSafely;
        }

        public static void ReconcileCompileSymbolForBatch()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException(
                    nameof(ReconcileCompileSymbolForBatch) + " may only be invoked from Unity batch mode.");

            var exitCode = 0;
            try
            {
                ReconcileCompileSymbol();
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                exitCode = 1;
                Debug.LogError(FormatFailureMessage("batch reconciliation", ex));
            }

            EditorApplication.Exit(exitCode);
        }

        private static void ReconcileCompileSymbolSafely()
        {
            try
            {
                ReconcileCompileSymbol();
            }
            catch (Exception ex)
            {
                Debug.LogError(FormatFailureMessage("editor delayCall reconciliation", ex));
            }
        }

        private static void ReconcileCompileSymbol()
        {
            var runtimeInstalled = IsRuntimePackageInstalled();
            var target = NamedBuildTarget.Standalone;
            var symbols = PlayerSettings.GetScriptingDefineSymbols(target);
            var parts = symbols
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .ToList();

            var changed = runtimeInstalled
                ? EnsureSymbol(parts, BaseCompileSymbol) | EnsureSymbol(parts, NativeRuntimePackageCompileSymbol)
                : RemoveSymbol(parts, BaseCompileSymbol) | RemoveSymbol(parts, NativeRuntimePackageCompileSymbol);
            if (!changed)
                return;

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", parts));
            Debug.Log(runtimeInstalled
                ? "Unity2Foxglove enabled " + BaseCompileSymbol + " and " + NativeRuntimePackageCompileSymbol
                  + " because " + RuntimePackageName + " is installed."
                : "Unity2Foxglove removed " + BaseCompileSymbol + " and " + NativeRuntimePackageCompileSymbol
                  + " because " + RuntimePackageName + " is not installed.");
        }

        private static bool EnsureSymbol(System.Collections.Generic.List<string> parts, string symbol)
        {
            if (parts.Contains(symbol, StringComparer.Ordinal))
                return false;

            parts.Add(symbol);
            return true;
        }

        private static bool RemoveSymbol(System.Collections.Generic.List<string> parts, string symbol)
        {
            return parts.RemoveAll(value => string.Equals(value, symbol, StringComparison.Ordinal)) > 0;
        }

        private static bool IsRuntimePackageInstalled()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            var projectDirectory = assetsDirectory.Parent;
            if (projectDirectory == null)
                return false;

            var manifestPath = Path.Combine(projectDirectory.FullName, "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
                return false;

            var manifest = File.ReadAllText(manifestPath);
            if (!ContainsPackageKey(manifest))
                return false;

            var lockPath = Path.Combine(projectDirectory.FullName, "Packages", "packages-lock.json");
            if (!File.Exists(lockPath))
            {
                Debug.LogWarning(
                    "Unity2Foxglove found " + RuntimePackageName
                    + " in manifest.json, but Packages/packages-lock.json is missing. "
                    + "Leaving ROS2 For Unity compile symbols disabled until Unity resolves the runtime package.");
                return false;
            }

            var lockFile = File.ReadAllText(lockPath);
            if (ContainsPackageKey(lockFile))
                return true;

            Debug.LogWarning(
                "Unity2Foxglove found " + RuntimePackageName
                + " in manifest.json, but not in packages-lock.json. "
                + "Leaving ROS2 For Unity compile symbols disabled until the runtime package is resolved.");
            return false;
        }

        private static bool ContainsPackageKey(string json)
        {
            var dependencyPattern = "\"" + Regex.Escape(RuntimePackageName) + "\"\\s*:";
            return Regex.IsMatch(json ?? string.Empty, dependencyPattern);
        }

        private static string FormatFailureMessage(string context, Exception ex)
        {
            return "Unity2Foxglove ROS2 For Unity compile symbol " + context
                   + " failed for " + RuntimePackageName + " / " + BaseCompileSymbol + " / "
                   + NativeRuntimePackageCompileSymbol + ": "
                   + ex.GetType().Name + ": " + ex.Message;
        }
    }
}
#endif
