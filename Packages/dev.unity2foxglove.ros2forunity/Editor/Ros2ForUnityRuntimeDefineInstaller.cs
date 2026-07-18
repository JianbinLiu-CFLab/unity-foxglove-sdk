// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Enables the optional ROS2 For Unity compile path when the runtime package is installed.

#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    [InitializeOnLoad]
    internal static class Ros2ForUnityRuntimeDefineInstaller
    {
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

        public static void ReconcileCompileSymbolForEditor()
        {
            ReconcileCompileSymbolSafely();
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
            var status = Ros2ForUnityRuntimeSelection.GetStatus();
            var target = NamedBuildTarget.Standalone;
            var symbols = PlayerSettings.GetScriptingDefineSymbols(target);
            var parts = symbols
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .ToList();

            var changed = false;
            changed |= RemoveStaleRuntimePackageSymbols(parts);

            if (status.HasSelection)
            {
                changed |= EnsureSymbol(parts, Ros2ForUnityRuntimeSelection.BaseCompileSymbol);
            }
            else
            {
                changed |= RemoveSymbol(parts, Ros2ForUnityRuntimeSelection.BaseCompileSymbol);
            }

            var customTypesupport = status.HasSelection
                ? Ros2ForUnityRuntimeSelection.GetActiveCustomTypesupportSelection(
                    Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication())
                : null;
            if (customTypesupport?.IsReady == true)
            {
                changed |= EnsureSymbol(parts, Ros2ForUnityRuntimeSelection.CustomTypesupportCompileSymbol);
            }
            else
            {
                changed |= RemoveSymbol(parts, Ros2ForUnityRuntimeSelection.CustomTypesupportCompileSymbol);
            }

            if (!changed)
                return;

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", parts));
            Debug.Log(status.HasSelection
                ? "Unity2Foxglove enabled " + Ros2ForUnityRuntimeSelection.BaseCompileSymbol
                  + " for active ROS2 For Unity runtime "
                  + status.SelectedRuntime.PackageName
                  + (customTypesupport?.IsReady == true
                      ? " and " + Ros2ForUnityRuntimeSelection.CustomTypesupportCompileSymbol
                      + " for " + customTypesupport.ActiveAddOnPackage + "."
                      : ".")
                : "Unity2Foxglove removed " + Ros2ForUnityRuntimeSelection.BaseCompileSymbol
                  + ": " + status.Diagnostic);
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

        private static bool RemoveStaleRuntimePackageSymbols(System.Collections.Generic.List<string> parts)
        {
            return parts.RemoveAll(value =>
                value.StartsWith(Ros2ForUnityRuntimeSelection.BaseCompileSymbol + "_", StringComparison.Ordinal)
                && value.EndsWith("_PACKAGE", StringComparison.Ordinal)) > 0;
        }

        private static string FormatFailureMessage(string context, Exception ex)
        {
            return "Unity2Foxglove ROS2 For Unity compile symbol " + context
                   + " failed for " + Ros2ForUnityRuntimeSelection.BaseCompileSymbol + ": "
                   + ex.GetType().Name + ": " + ex.Message;
        }
    }
}
#endif
