// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 106 ROS2 For Unity standalone interop spike validation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase106Validation
    {
        private const string Define = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";
        private const string AcceptancePath = "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase106Ros2ForUnityAcceptance.cs";
        private const string OutTopic = "/unity2foxglove/phase106/out";
        private const string InTopic = "/unity2foxglove/phase106/in";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 106: ROS2 For Unity Standalone Interop Spike ===");
            _passed = 0;

            VerifyLocalAssetIgnoreRules();
            VerifyPackageHasNoHardR2fuDependency();
            VerifyAcceptanceComponent();
            VerifyTrackedAssetBoundary();
            VerifyDocsBoundary();
            VerifyPlanReplacementIfPresent();

            Console.WriteLine($"Phase 106: {_passed} checks passed.");
        }

        private static void VerifyLocalAssetIgnoreRules()
        {
            var gitignore = ReadRepoText(".gitignore");
            Check(gitignore.Contains("Unity2Foxglove/Assets/Ros2ForUnity/", StringComparison.Ordinal)
                  && gitignore.Contains("Unity2Foxglove/Assets/Ros2ForUnity.meta", StringComparison.Ordinal),
                "106A-1: local ROS2 For Unity asset import is ignored");
        }

        private static void VerifyPackageHasNoHardR2fuDependency()
        {
            var root = RepoRoot();
            var packageRoot = Path.Combine(root, "Packages", "dev.unity2foxglove.sdk");
            var scanRoots = new[]
            {
                Path.Combine(packageRoot, "Runtime"),
                Path.Combine(packageRoot, "Editor"),
                Path.Combine(packageRoot, "Samples~")
            };

            var forbidden = new[]
            {
                "using ROS2;",
                "namespace ROS2",
                "ROS2UnityComponent",
                "Ros2ForUnity"
            };

            var hits = scanRoots
                .Where(Directory.Exists)
                .SelectMany(rootDir => Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories))
                .Where(path => HasTextExtension(path))
                .SelectMany(path => forbidden
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Path.GetRelativePath(root, path).Replace('\\', '/') + " -> " + token))
                .ToList();

            Check(hits.Count == 0,
                "106B-1: package Runtime/Editor/Samples have no hard ROS2 For Unity dependency"
                + (hits.Count == 0 ? string.Empty : " (" + string.Join(", ", hits) + ")"));
        }

        private static void VerifyAcceptanceComponent()
        {
            var text = ReadRepoText(AcceptancePath);

            Check(text.Contains(Define, StringComparison.Ordinal),
                "106C-1: acceptance component is guarded by the ROS2 For Unity scripting define");
            Check(text.Contains(OutTopic, StringComparison.Ordinal)
                  && text.Contains(InTopic, StringComparison.Ordinal),
                "106C-2: acceptance component uses deterministic Phase106 topics");
            Check(text.Contains("GetComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && text.Contains("gameObject.AddComponent<ROS2UnityComponent>()", StringComparison.Ordinal),
                "106C-3: acceptance component gets or adds ROS2UnityComponent on the same GameObject");
            Check(text.Contains("ros2Unity.Ok()", StringComparison.Ordinal)
                  && !text.Contains("ROS2UnityComponent.Ok()", StringComparison.Ordinal),
                "106C-4: acceptance component uses the ROS2UnityComponent instance Ok() method");
            Check(text.Contains("CreateNode(\"unity2foxglove_phase106\")", StringComparison.Ordinal),
                "106C-5: acceptance component creates the expected ROS2 node");
            Check(text.Contains("CreatePublisher<std_msgs.msg.String>(\"" + OutTopic + "\")", StringComparison.Ordinal),
                "106C-6: acceptance publisher uses default QoS");
            Check(text.Contains("CreateSubscription<std_msgs.msg.String>", StringComparison.Ordinal)
                  && text.Contains("\"" + InTopic + "\"", StringComparison.Ordinal),
                "106C-7: acceptance subscriber uses default QoS");
            Check(!text.Contains("QualityOfServiceProfile", StringComparison.Ordinal),
                "106C-8: acceptance component does not construct custom QoS");
            Check(!text.Contains("SpinOnce", StringComparison.Ordinal),
                "106C-9: acceptance component does not manually spin ROS2");
            Check(AllR2fuReferencesAreGuarded(text),
                "106C-10: ROS2 For Unity API references stay inside compile guard");
        }

        private static void VerifyTrackedAssetBoundary()
        {
            var tracked = GitLsFiles();
            const string runtimePackage = "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/";
            Check(!tracked.Any(path => path.StartsWith("Unity2Foxglove/Assets/Ros2ForUnity", StringComparison.Ordinal)),
                "106D-1: extracted ROS2 For Unity assets are not tracked");
            var disallowedArtifacts = tracked
                .Where(path => !path.StartsWith(runtimePackage, StringComparison.Ordinal))
                .Where(path => path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith("Ros2ForUnity_humble_standalone_windows11.zip", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith("metadata_ros2cs.xml", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith("metadata_ros2_for_unity.xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Check(disallowedArtifacts.Count == 0,
                "106D-2: ROS2 For Unity artifacts are tracked only inside the explicit runtime package"
                + (disallowedArtifacts.Count == 0 ? string.Empty : " (" + string.Join(", ", disallowedArtifacts) + ")"));
        }

        private static void VerifyDocsBoundary()
        {
            var readme = ReadRepoText("README.md");
            var roadmap = ReadRepoText("ROADMAP.md");
            var combined = readme + "\n" + roadmap;

            Check(readme.Contains("normal Foxglove WebSocket streaming, MCAP recording, or replay", StringComparison.Ordinal)
                  && readme.Contains("ROS2 For Unity", StringComparison.Ordinal)
                  && readme.Contains("optional", StringComparison.OrdinalIgnoreCase),
                "106E-1: README preserves no-ROS default while naming optional ROS2 For Unity evaluation");
            Check(combined.Contains("RobotecAI ROS2 For Unity", StringComparison.Ordinal)
                  && combined.Contains("Apache-2.0", StringComparison.Ordinal),
                "106E-2: docs attribute RobotecAI ROS2 For Unity before any bundled adoption");
            Check(roadmap.Contains("supersedes the embedded rclcpp spike route", StringComparison.Ordinal)
                  && roadmap.Contains("Jazzy", StringComparison.Ordinal)
                  && roadmap.Contains("Humble", StringComparison.Ordinal),
                "106E-3: roadmap records Jazzy-first, Humble-fallback ROS2 For Unity direction");
        }

        private static void VerifyPlanReplacementIfPresent()
        {
            var root = RepoRoot();
            var oldPlan = Path.Combine(root, "Plan", "106_PHASE106_ROS2_STANDARD_MAPPING_PROFILES_PLAN.md");
            var newPlan = Path.Combine(root, "Plan", "106_PHASE106_ROS2_FOR_UNITY_STANDALONE_INTEROP_SPIKE_PLAN.md");

            if (File.Exists(oldPlan))
                throw new InvalidOperationException("106F-1: obsolete Phase106 standard mapping plan still exists");

            if (!File.Exists(newPlan))
            {
                Check(true, "106F-1: Phase106 private plan is absent from clean tracked checkout");
                return;
            }

            var text = File.ReadAllText(newPlan);
            Check(text.Contains("ROS2 For Unity Standalone Interop Spike", StringComparison.Ordinal)
                  && !text.Contains("# Phase 106 - ROS2 Standard Mapping Profiles", StringComparison.Ordinal),
                "106F-1: current Phase106 plan is the ROS2 For Unity interop spike");
        }

        private static bool AllR2fuReferencesAreGuarded(string text)
        {
            var tokens = new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "ISubscription<",
                "std_msgs.msg.String",
                "CreatePublisher<",
                "CreateSubscription<"
            };

            var stack = new Stack<bool>();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("#if ", StringComparison.Ordinal))
                {
                    stack.Push(trimmed.Contains(Define, StringComparison.Ordinal));
                    continue;
                }

                if (trimmed.StartsWith("#elif ", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    stack.Push(trimmed.Contains(Define, StringComparison.Ordinal));
                    continue;
                }

                if (trimmed.StartsWith("#else", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    stack.Push(false);
                    continue;
                }

                if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    continue;
                }

                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (tokens.Any(token => line.Contains(token, StringComparison.Ordinal))
                    && !stack.Any(guarded => guarded))
                {
                    throw new InvalidOperationException("Unguarded R2FU reference on line " + (i + 1) + ": " + trimmed);
                }
            }

            return true;
        }

        private static IReadOnlyList<string> GitLsFiles()
        {
            var root = RepoRoot();
            var start = new ProcessStartInfo("git", "ls-files")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(start);
            if (process == null)
                throw new InvalidOperationException("Could not start git ls-files.");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException("git ls-files failed: " + error);

            return output.Replace("\r\n", "\n")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Replace('\\', '/'))
                .ToList();
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

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = RepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase106 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase106 validation.");
            return root;
        }
    }
}
