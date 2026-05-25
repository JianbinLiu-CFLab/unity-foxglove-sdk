// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 109 ROS2 For Unity bidirectional string smoke boundary validation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase109Validation
    {
        private const string Define = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";
        private const string OptionalPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string OptionalRuntime = OptionalPackage + "/Runtime";
        private const string ManualAcceptance = "Unity2Foxglove/Assets/Scripts/ManualAcceptance";
        private const string FactoryPath = ManualAcceptance + "/Phase109Ros2ForUnityContextFactory.cs";
        private const string ContextPath = ManualAcceptance + "/Phase109Ros2ForUnityContext.cs";
        private const string SmokePath = ManualAcceptance + "/Phase109Ros2ForUnityStringSmoke.cs";
        private const string OutTopic = "/unity2foxglove/phase109/out";
        private const string InTopic = "/unity2foxglove/phase109/in";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 109: ROS2 For Unity Bidirectional Topic Smoke ===");
            _passed = 0;

            VerifyPrivatePlanIfPresent();
            VerifyFacadeBoundary();
            VerifyManualAdapter();
            VerifySmokeComponent();
            VerifyPackageBoundaries();
            VerifyValidationWiring();
            VerifyDocsBoundary();
            VerifyTrackedAssetBoundary();

            Console.WriteLine($"Phase 109: {_passed} checks passed.");
        }

        private static void VerifyPrivatePlanIfPresent()
        {
            var planPath = Path.Combine(RepoRoot(), "Plan", "109_PHASE109_ROS2_FOR_UNITY_BIDIRECTIONAL_TOPIC_SMOKE_PLAN.md");
            if (!File.Exists(planPath))
            {
                Check(true, "109-A1: private Phase109 plan may be absent in clean tracked checkout");
                return;
            }

            var plan = File.ReadAllText(planPath);
            Check(plan.Contains("ROS2 For Unity Bidirectional Topic Smoke", StringComparison.Ordinal)
                  && plan.Contains("Do not use WSL2 NAT as the GREEN gate", StringComparison.Ordinal)
                  && plan.Contains("optional package Runtime facade", StringComparison.Ordinal),
                "109-A1: private Phase109 plan records the current smoke and boundary contract");
        }

        private static void VerifyFacadeBoundary()
        {
            var factory = ReadRepoText(OptionalRuntime + "/Unity2FoxgloveRos2ContextFactory.cs");
            Check(factory.Contains("public static IUnity2FoxgloveRos2Context Create()", StringComparison.Ordinal)
                  && !factory.Contains("GameObject", StringComparison.Ordinal)
                  && !factory.Contains("UnityEngine", StringComparison.Ordinal),
                "109-B1: optional package factory remains host-agnostic");
            Check(factory.Contains("Unity2FoxgloveRos2UnavailableContext.Instance", StringComparison.Ordinal),
                "109-B2: optional package factory remains unavailable by default");

            var offenders = TextFiles(OptionalRuntime)
                .SelectMany(path => OptionalRuntimeForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(offenders.Count == 0,
                "109-B3: optional Runtime has no UnityEngine, ROS2, or generated message references"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
        }

        private static void VerifyManualAdapter()
        {
            foreach (var path in new[] { FactoryPath, ContextPath })
                Check(RepoFileExists(path), "109-C1: manual adapter file exists: " + path);

            var factory = ReadRepoText(FactoryPath);
            var context = ReadRepoText(ContextPath);
            var combined = factory + "\n" + context;

            Check(factory.Contains("Create(GameObject host)", StringComparison.Ordinal)
                  && factory.Contains("Phase109Ros2ForUnityContext", StringComparison.Ordinal),
                "109-C2: manual adapter exposes a demo-project host factory");
            Check(combined.Contains(Define, StringComparison.Ordinal),
                "109-C3: manual adapter is guarded by the R2FU scripting define");
            Check(AllR2fuReferencesAreGuarded(factory) && AllR2fuReferencesAreGuarded(context),
                "109-C4: manual adapter R2FU references stay inside compile guard");
            Check(combined.Contains("typeof(T) == typeof(std_msgs.msg.String)", StringComparison.Ordinal)
                  && combined.Contains("Unsupported Phase109 ROS2 message type", StringComparison.Ordinal),
                "109-C5: manual adapter supports only std_msgs.msg.String and rejects other message types");
            Check(combined.Contains("GetComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && combined.Contains("AddComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && combined.Contains(".Ok()", StringComparison.Ordinal),
                "109-C6: manual adapter gets or adds ROS2UnityComponent and waits for Ok()");
            Check(combined.Contains("var normalizedName = NormalizeName(nodeName);", StringComparison.Ordinal)
                  && combined.Contains("_ros2Unity.CreateNode(normalizedName)", StringComparison.Ordinal)
                  && combined.Contains("new Phase109Ros2ForUnityNode(_ros2Unity, ros2Node, normalizedName)", StringComparison.Ordinal),
                "109-C7: manual adapter passes the requested normalized node name to ROS2 For Unity");
            Check(!combined.Contains("SpinOnce", StringComparison.Ordinal)
                  && !combined.Contains("QualityOfServiceProfile", StringComparison.Ordinal),
                "109-C8: manual adapter does not manually spin or construct QoS");
            Check(combined.Contains("Queue<", StringComparison.Ordinal)
                  && combined.Contains("DrainPendingCallbacks", StringComparison.Ordinal),
                "109-C9: manual adapter queues subscription callbacks for Unity main-thread drain");
        }

        private static void VerifySmokeComponent()
        {
            Check(RepoFileExists(SmokePath), "109-D1: manual string smoke component exists");
            var text = ReadRepoText(SmokePath);

            Check(text.Contains(OutTopic, StringComparison.Ordinal)
                  && text.Contains(InTopic, StringComparison.Ordinal),
                "109-D2: smoke component uses locked Phase109 topics");
            Check(text.Contains("phase109 unity tick", StringComparison.Ordinal),
                "109-D3: smoke component publishes deterministic Phase109 tick payloads");
            Check(text.Contains("[Phase109Ros2ForUnityStringSmoke] received:", StringComparison.Ordinal),
                "109-D4: smoke component logs received messages with the expected prefix");
            Check(text.Contains("_publishedCount", StringComparison.Ordinal)
                  && text.Contains("_receivedCount", StringComparison.Ordinal)
                  && text.Contains("_lastReceived", StringComparison.Ordinal)
                  && text.Contains("_statusMessage", StringComparison.Ordinal),
                "109-D5: smoke component exposes counters and status fields");
            Check(text.Contains(Define, StringComparison.Ordinal)
                  && AllR2fuReferencesAreGuarded(text),
                "109-D6: smoke component guards R2FU references behind the scripting define");
            var subscriptionIndex = text.IndexOf("CreateSubscription<std_msgs.msg.String>", StringComparison.Ordinal);
            var publisherIndex = text.IndexOf("CreatePublisher<std_msgs.msg.String>", StringComparison.Ordinal);
            Check(subscriptionIndex >= 0 && publisherIndex >= 0 && subscriptionIndex < publisherIndex,
                "109-D7: smoke component creates subscription before publisher so R2FU/Jazzy exposes both graph endpoints");
        }

        private static void VerifyPackageBoundaries()
        {
            var coreProductionFiles = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime",
                "Packages/dev.unity2foxglove.sdk/Editor",
                "Packages/dev.unity2foxglove.sdk/Samples~",
                "Packages/dev.unity2foxglove.sdk/package.json"
            };

            var coreHits = coreProductionFiles
                .SelectMany(ExistingTextFilesOrSingleFile)
                .SelectMany(path => CoreProductionForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(coreHits.Count == 0,
                "109-E1: core SDK production surface has no hard R2FU dependency"
                + (coreHits.Count == 0 ? string.Empty : " (" + string.Join(", ", coreHits) + ")"));
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(program.Contains("--phase109", StringComparison.Ordinal)
                  && program.Contains("RunPhase109Only", StringComparison.Ordinal)
                  && program.Contains("Phase109Validation.Validate()", StringComparison.Ordinal),
                "109-F1: Program.cs wires --phase109");
            Check(project.Contains("Phase109Validation.cs", StringComparison.Ordinal),
                "109-F2: test project compiles Phase109Validation");
        }

        private static void VerifyDocsBoundary()
        {
            var readme = ReadRepoText(OptionalPackage + "/README.md");
            Check(readme.Contains("External Adapter Sample", StringComparison.Ordinal)
                  && readme.Contains("std_msgs/msg/String", StringComparison.Ordinal)
                  && readme.Contains("not bundled", StringComparison.OrdinalIgnoreCase)
                  && readme.Contains("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", StringComparison.Ordinal),
                "109-G1: optional package README documents the external adapter string smoke boundary");
        }

        private static void VerifyTrackedAssetBoundary()
        {
            var forbiddenTracked = GitLsFiles()
                .Where(path => path.StartsWith("Unity2Foxglove/Assets/Ros2ForUnity", StringComparison.Ordinal)
                               || IsForbiddenR2fuArtifact(path)
                               || IsOptionalPackageRuntimeBinary(path))
                .ToList();

            Check(forbiddenTracked.Count == 0,
                "109-H1: tracked files contain no R2FU assets, packages, metadata, or optional runtime binaries"
                + (forbiddenTracked.Count == 0 ? string.Empty : " (" + string.Join(", ", forbiddenTracked) + ")"));
        }

        private static IEnumerable<string> OptionalRuntimeForbiddenTokens()
        {
            return new[]
            {
                "UnityEngine",
                "GameObject",
                "using ROS2;",
                "namespace ROS2",
                "ROS2UnityComponent",
                "ROS2Node",
                "std_msgs",
                "ros2cs"
            };
        }

        private static IEnumerable<string> CoreProductionForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "ROS2Node",
                "std_msgs",
                "Ros2ForUnity"
            };
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
                "std_msgs"
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
            if (path.StartsWith("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/", StringComparison.Ordinal))
                return false;

            return path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("Ros2ForUnity_humble_standalone_windows11.zip", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("metadata_ros2cs.xml", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("metadata_ros2_for_unity.xml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOptionalPackageRuntimeBinary(string path)
        {
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
                throw new FileNotFoundException("Missing required Phase109 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string Rel(string path)
        {
            return Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase109 validation.");
            return root;
        }
    }
}
