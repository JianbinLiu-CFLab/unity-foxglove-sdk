// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Explicit Editor and -executeMethod entrypoint for static ROS2 interface generation.

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// This command is intentionally explicit: source generation, Play Mode,
    /// package import, and Player startup never invoke it. The source package
    /// is changed only by an operator request or a batch -executeMethod call.
    /// </summary>
    public static class FoxRunRos2InterfacePackageCommand
    {
        [MenuItem("Foxglove/FoxRun/Generate ROS2 Interface Source Package")]
        public static void GenerateFromMenu()
        {
            Run(generate: true, checkOnly: false, nextRevision: null, exitWhenBatch: false);
        }

        /// <summary>
        /// Unity batch entry point. Accepts exactly one of <c>--check</c> or
        /// <c>--generate</c>, plus the optional explicit
        /// <c>--next-revision unity2foxglove_foxrun_interfaces_vN</c> argument.
        /// </summary>
        public static void ExecuteFromCommandLine()
        {
            var arguments = Environment.GetCommandLineArgs();
            var check = arguments.Any(argument => string.Equals(argument, "--check", StringComparison.Ordinal));
            var generate = arguments.Any(argument => string.Equals(argument, "--generate", StringComparison.Ordinal));
            var nextRevision = ReadOption(arguments, "--next-revision");
            var exitCode = check == generate
                ? ReportArgumentError("Specify exactly one of --check or --generate.")
                : Run(generate, check, nextRevision, exitWhenBatch: true);

            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }

        internal static string GetSourcePackageRoot()
            => Path.Combine(GetRepositoryRoot(), "Packages", Unity.FoxgloveSDK.Components.FoxRunRos2InterfaceIdentity.UnityPackageId);

        internal static string GetRepositoryRoot()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.Parent;
            if (projectRoot == null
                || !Directory.Exists(Path.Combine(projectRoot.FullName, "Packages", "dev.unity2foxglove.sdk")))
            {
                throw new DirectoryNotFoundException("Could not locate the Unity2Foxglove repository root from Application.dataPath.");
            }

            return projectRoot.FullName;
        }

        private static int Run(bool generate, bool checkOnly, string nextRevision, bool exitWhenBatch)
        {
            try
            {
                if (!string.IsNullOrEmpty(nextRevision) && !generate)
                    return ReportArgumentError("--next-revision is valid only with --generate.");

                var repoRoot = GetRepositoryRoot();
                var packageRoot = GetSourcePackageRoot();
                var model = FoxrunCodeGenerator.CollectReflectionGenerationModelForRos2InterfacePackage();

                if (checkOnly)
                {
                    var result = FoxRunRos2InterfacePackagePreflight.Evaluate(packageRoot, model);
                    if (!result.IsReady)
                    {
                        Debug.LogError("[FoxRun ROS2 interfaces] " + result.DiagnosticCode + ": " + result.Action);
                        return 1;
                    }

                    Debug.Log("[FoxRun ROS2 interfaces] " + result.State + " " + result.RosPackageName + " " + result.ShortDigest);
                    return 0;
                }

                var selectedPackageName = FoxRunRos2InterfaceProjectSettings.ResolveRosPackageName(packageRoot);
                var hasCurrentLock = File.Exists(Path.Combine(
                    packageRoot,
                    "RuntimeSupport",
                    "foxrun-ros2-interface-lock.json"));
                var resultAfterWrite = FoxRunRos2InterfacePackageWriter.Generate(
                    repoRoot,
                    packageRoot,
                    model,
                    nextRevision ?? (hasCurrentLock ? null : selectedPackageName));
                var preflight = FoxRunRos2InterfacePackagePreflight.Evaluate(packageRoot, model);
                if (!preflight.IsReady)
                {
                    Debug.LogError("[FoxRun ROS2 interfaces] generation completed but preflight is "
                                   + preflight.State + ": " + preflight.Action);
                    return 1;
                }

                AssetDatabase.Refresh();
                Debug.Log("[FoxRun ROS2 interfaces] generated " + resultAfterWrite.Lock.RosPackageName
                          + " " + resultAfterWrite.Lock.InterfaceDigest.Substring(0, 12)
                          + (resultAfterWrite.Changed ? "." : " (already current)."));
                return 0;
            }
            catch (Exception exception)
            {
                Debug.LogError("[FoxRun ROS2 interfaces] " + exception.Message);
                if (!exitWhenBatch)
                    Debug.LogException(exception);
                return 1;
            }
        }

        private static int ReportArgumentError(string message)
        {
            Debug.LogError("[FoxRun ROS2 interfaces] " + message);
            return 2;
        }

        private static string ReadOption(string[] arguments, string option)
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                if (!string.Equals(arguments[i], option, StringComparison.Ordinal))
                    continue;
                if (i + 1 >= arguments.Length || arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return string.Empty;
                return arguments[i + 1];
            }

            return null;
        }
    }
}
#endif
