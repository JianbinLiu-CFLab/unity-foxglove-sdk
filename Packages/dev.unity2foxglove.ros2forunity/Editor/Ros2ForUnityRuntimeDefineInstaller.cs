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
        private const string CompileSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";

        static Ros2ForUnityRuntimeDefineInstaller()
        {
            EditorApplication.delayCall += ReconcileCompileSymbol;
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

            var hasSymbol = parts.Contains(CompileSymbol, StringComparer.Ordinal);
            if (runtimeInstalled && hasSymbol)
                return;
            if (!runtimeInstalled && !hasSymbol)
                return;

            if (runtimeInstalled)
                parts.Add(CompileSymbol);
            else
                parts.RemoveAll(value => string.Equals(value, CompileSymbol, StringComparison.Ordinal));

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", parts));
            Debug.Log(runtimeInstalled
                ? "Unity2Foxglove enabled " + CompileSymbol + " because " + RuntimePackageName + " is installed."
                : "Unity2Foxglove removed " + CompileSymbol + " because " + RuntimePackageName + " is not installed.");
        }

        private static bool IsRuntimePackageInstalled()
        {
            var manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
                return false;

            var manifest = File.ReadAllText(manifestPath);
            var dependencyPattern = "\"" + Regex.Escape(RuntimePackageName) + "\"\\s*:";
            return Regex.IsMatch(manifest, dependencyPattern);
        }
    }
}
#endif
