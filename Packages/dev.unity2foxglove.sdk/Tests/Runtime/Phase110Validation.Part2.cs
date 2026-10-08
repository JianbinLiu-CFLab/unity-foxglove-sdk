// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 110 ROS2 For Unity external adapter sample validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase110Validation.
    /// </summary>
    public static partial class Phase110Validation
    {
        private static void VerifyStringSmokeBatchAcceptance()
        {
            var batch = ReadRepoText(StringSmokeBatchPath);

            Check(batch.Contains("Phase110Ros2ForUnityStringSmoke", StringComparison.Ordinal)
                  && batch.Contains("Assets/Scenes/Phase110Acceptance.unity", StringComparison.Ordinal),
                "110-J1: batch acceptance drives the actual Phase110Acceptance String Smoke component");
            Check(batch.Contains("_directMode", StringComparison.Ordinal)
                  && batch.Contains("UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_DIRECT", StringComparison.Ordinal)
                  && batch.Contains("DIRECT_MODE=True", StringComparison.Ordinal)
                  && batch.Contains("DIRECT_MODE=False", StringComparison.Ordinal),
                "110-J2: batch acceptance explicitly supports direct-runtime and facade String Smoke modes");
            Check(batch.Contains(OutTopic, StringComparison.Ordinal)
                  && batch.Contains(InTopic, StringComparison.Ordinal)
                  && batch.Contains("unity2foxglove_phase110", StringComparison.Ordinal),
                "110-J3: batch acceptance uses the same String Smoke topics and node as the Unity scene");
            Check(batch.Contains("UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_GREEN", StringComparison.Ordinal)
                  && batch.Contains("received=", StringComparison.Ordinal)
                  && batch.Contains("ReceivedCount", StringComparison.Ordinal),
                "110-J4: batch acceptance fails unless Unity records inbound String Smoke messages");
            Check(batch.Contains("UNITY2FOXGLOVE_PHASE110_INITIAL_PATH_CLEAN", StringComparison.Ordinal)
                  && batch.Contains("ContainsMachineRosPath", StringComparison.Ordinal),
                "110-J5: batch acceptance records whether Unity inherited machine ROS2 PATH entries");
        }

        private static void VerifyStringSmokeRos2AcceptanceScript()
        {
            var script = ReadRepoText(StringSmokeRos2ScriptPath);

            Check((script.Contains(@"ros2-windows\ros2_jazzy", StringComparison.Ordinal)
                  || script.Contains(@"ros2-windows\\ros2_jazzy", StringComparison.Ordinal))
                  && script.Contains("DEFAULT_ROS2_ROOT", StringComparison.Ordinal)
                  && script.Contains("default=str(DEFAULT_ROS2_ROOT)", StringComparison.Ordinal)
                  && script.Contains("ros2_root", StringComparison.Ordinal)
                  && script.Contains("validate_ros2_root", StringComparison.Ordinal)
                  && script.Contains("ros2env.run_ros2", StringComparison.Ordinal),
                "110-K1: Python manual acceptance script defaults to the shared Windows ROS2 Jazzy root");
            Check(script.Contains("has_positive_subscription_count", StringComparison.Ordinal)
                  && script.Contains("\"topic\"", StringComparison.Ordinal)
                  && script.Contains("\"info\"", StringComparison.Ordinal)
                  && script.Contains(InTopic, StringComparison.Ordinal)
                  && script.Contains("Subscription count", StringComparison.Ordinal)
                  && script.Contains("Node name: {NODE_NAME}", StringComparison.Ordinal),
                "110-K2: Python manual acceptance script waits for Unity's String Smoke subscription");
            Check(script.Contains("\"echo\"", StringComparison.Ordinal)
                  && script.Contains(OutTopic, StringComparison.Ordinal)
                  && script.Contains("unity2foxglove string tick", StringComparison.Ordinal),
                "110-K3: Python manual acceptance script echoes Unity's String Smoke publisher");
            Check(script.Contains("\"pub\"", StringComparison.Ordinal)
                  && script.Contains("--wait-matching-subscriptions", StringComparison.Ordinal)
                  && script.Contains("--keep-alive", StringComparison.Ordinal)
                  && !script.Contains("--no-daemon\",\n            \"--times", StringComparison.Ordinal),
                "110-K4: Python manual acceptance script publishes inbound messages without ros2 pub --no-daemon");
        }

        private static void VerifyValidationWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("--phase110", StringComparison.Ordinal)
                  && registry.Contains("Phase110Validation.Validate", StringComparison.Ordinal),
                "110-L1: registry wires --phase110");
            Check(project.Contains("Phase110Validation.cs", StringComparison.Ordinal),
                "110-L2: test project compiles Phase110Validation");
        }

        private static bool AllR2fuReferencesAreGuarded(string text, out string error)
        {
            return PhaseRos2ForUnityValidationHelpers.AllR2fuReferencesAreGuarded(text, Define, R2fuReferenceTokens, out error);
        }

        private static string FirstGuardError(params string[] errors)
        {
            return errors.FirstOrDefault(error => !string.IsNullOrEmpty(error)) ?? string.Empty;
        }

        private static IEnumerable<string> ExistingTextFilesOrSingleFile(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
                return HasTextExtension(path) ? new[] { path } : Array.Empty<string>();
            if (Directory.Exists(path))
                return TextFiles(relativePath);
            return Array.Empty<string>();
        }

        private static IEnumerable<string> TextFiles(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(path))
                return Array.Empty<string>();

            return Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(HasTextExtension);
        }

        private static bool IsForbiddenR2fuArtifact(string path)
        {
            return PhaseRos2ForUnityValidationHelpers.IsForbiddenR2fuArtifact(path);
        }

        private static bool IsOptionalPackageRuntimeBinary(string path)
        {
            if (IsOwnedOptionalEditorAnalyzer(path))
                return false;

            if (!path.StartsWith(OptionalPackage + "/", StringComparison.Ordinal))
                return false;

            var extension = Path.GetExtension(path);
            return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".so", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".unitypackage", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("metadata_ros2cs.xml", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("metadata_ros2_for_unity.xml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOwnedOptionalEditorAnalyzer(string path)
            => path.Equals(
                OptionalPackage
                + "/Editor/SourceGenerators/analyzers/dotnet/cs/"
                + "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll",
                StringComparison.Ordinal);

        private static bool IsAllowedRuntimePackageFile(string path)
        {
            if (!path.StartsWith(RuntimePackage + "/", StringComparison.Ordinal))
                return false;

            return !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                   && !path.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                   && !path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasTextExtension(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RepoFileExists(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = RepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase110 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string Rel(string path)
        {
            return Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');
        }

        private static bool IsAllowedRuntimeToken(
            string path,
            string token,
            string text)
        {
            var relative = Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');
            if (relative.StartsWith(OptionalRuntimeNative, StringComparison.Ordinal))
                return true;

            return relative.Equals(
                       OptionalRuntime + "/FoxRunRos2Qos.cs",
                       StringComparison.Ordinal)
                   && token.Equals(
                       "UnityEngine",
                       StringComparison.Ordinal)
                   && text.Contains(
                       "#if UNITY_5_3_OR_NEWER",
                       StringComparison.Ordinal);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

}
}
