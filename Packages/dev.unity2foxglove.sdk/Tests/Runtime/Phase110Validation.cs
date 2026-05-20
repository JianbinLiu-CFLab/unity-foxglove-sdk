// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 110 ROS2 For Unity external adapter sample validation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase110Validation
    {
        private const string Define = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";
        private const string OptionalPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string OptionalRuntime = OptionalPackage + "/Runtime";
        private const string SampleName = "ROS2 For Unity External Adapter";
        private const string SamplePath = OptionalPackage + "/Samples~/ROS2 For Unity External Adapter";
        private const string FactoryPath = SamplePath + "/Phase110Ros2ForUnityContextFactory.cs";
        private const string ContextPath = SamplePath + "/Phase110Ros2ForUnityContext.cs";
        private const string SmokePath = SamplePath + "/Phase110Ros2ForUnityStringSmoke.cs";
        private const string SampleReadmePath = SamplePath + "/README.md";
        private const string OutTopic = "/unity2foxglove/ros2forunity/string/out";
        private const string InTopic = "/unity2foxglove/ros2forunity/string/in";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 110: ROS2 For Unity External Productization Gate ===");
            _passed = 0;

            VerifyPrivatePlanIfPresent();
            VerifyPackageMetadata();
            VerifySampleFiles();
            VerifySampleAdapter();
            VerifyFacadeBoundary();
            VerifyPackageBoundaries();
            VerifyComplianceManifest();
            VerifyDocs();
            VerifyTrackedAssetBoundary();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 110: {_passed} checks passed.");
        }

        private static void VerifyPrivatePlanIfPresent()
        {
            var planPath = Path.Combine(RepoRoot(), "Plan", "110_PHASE110_ROS2_FOR_UNITY_EXTERNAL_PRODUCTIZATION_GATE_PLAN.md");
            if (!File.Exists(planPath))
            {
                Check(true, "110-A1: private Phase110 plan may be absent in clean tracked checkout");
                return;
            }

            var plan = File.ReadAllText(planPath);
            Check(plan.Contains("ROS2 For Unity External Productization Gate", StringComparison.Ordinal)
                  && plan.Contains("implementation-ready", StringComparison.Ordinal)
                  && plan.Contains("WSL2 NAT is not a Phase 110 GREEN gate", StringComparison.Ordinal),
                "110-A1: private Phase110 plan records deferred live gate and WSL2 boundary");
        }

        private static void VerifyPackageMetadata()
        {
            using var document = JsonDocument.Parse(ReadRepoText(OptionalPackage + "/package.json"));
            var root = document.RootElement;
            Check(root.TryGetProperty("samples", out var samples) && samples.ValueKind == JsonValueKind.Array,
                "110-B1: optional package declares package samples");

            JsonElement? sample = null;
            foreach (var entry in samples.EnumerateArray())
            {
                if (entry.TryGetProperty("displayName", out var displayName)
                    && displayName.GetString() == SampleName)
                {
                    sample = entry;
                    break;
                }
            }

            Check(sample.HasValue, "110-B2: optional package sample entry exists for ROS2 For Unity External Adapter");
            if (!sample.HasValue)
                return;

            var value = sample.Value;
            Check(value.TryGetProperty("path", out var path)
                  && path.GetString() == "Samples~/ROS2 For Unity External Adapter",
                "110-B3: optional package sample entry points at the external adapter sample");
            Check(value.TryGetProperty("description", out var description)
                  && (description.GetString() ?? string.Empty).Contains("externally imported ROS2 For Unity", StringComparison.Ordinal),
                "110-B4: optional package sample description names the external R2FU import boundary");
        }

        private static void VerifySampleFiles()
        {
            foreach (var path in new[] { SampleReadmePath, FactoryPath, ContextPath, SmokePath })
                Check(RepoFileExists(path), "110-C1: sample file exists: " + path);
        }

        private static void VerifySampleAdapter()
        {
            var factory = ReadRepoText(FactoryPath);
            var context = ReadRepoText(ContextPath);
            var smoke = ReadRepoText(SmokePath);
            var combined = factory + "\n" + context + "\n" + smoke;

            Check(factory.Contains("Create(GameObject host)", StringComparison.Ordinal)
                  && factory.Contains("Phase110Ros2ForUnityContext", StringComparison.Ordinal),
                "110-D1: sample owns the project-side GameObject context factory");
            Check(AllR2fuReferencesAreGuarded(factory)
                  && AllR2fuReferencesAreGuarded(context)
                  && AllR2fuReferencesAreGuarded(smoke),
                "110-D2: sample R2FU references stay inside compile guard");
            Check(context.Contains("typeof(T) == typeof(std_msgs.msg.String)", StringComparison.Ordinal)
                  && context.Contains("Unsupported Phase110 ROS2 message type", StringComparison.Ordinal),
                "110-D3: sample adapter supports only std_msgs/msg/String");
            Check(combined.Contains("GetComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && combined.Contains("AddComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && combined.Contains(".Ok()", StringComparison.Ordinal),
                "110-D4: sample adapter gets or adds ROS2UnityComponent and waits for Ok()");
            Check(smoke.Contains("NodeName = \"unity2foxglove_phase110\"", StringComparison.Ordinal)
                  && smoke.Contains("CreateNode(NormalizeTopic(_nodeName, NodeName))", StringComparison.Ordinal)
                  && context.Contains("_ros2Unity.CreateNode(normalizedName)", StringComparison.Ordinal),
                "110-D5: sample adapter defaults to the Phase110 node name and passes node names through");
            Check(!combined.Contains("SpinOnce", StringComparison.Ordinal)
                  && !combined.Contains("QualityOfServiceProfile", StringComparison.Ordinal),
                "110-D6: sample adapter does not manually spin or construct QoS");
            Check(combined.Contains("Queue<", StringComparison.Ordinal)
                  && combined.Contains("DrainPendingCallbacks", StringComparison.Ordinal),
                "110-D7: sample adapter queues subscription callbacks for Unity main-thread drain");
            Check(smoke.Contains(OutTopic, StringComparison.Ordinal)
                  && smoke.Contains(InTopic, StringComparison.Ordinal),
                "110-D8: sample smoke uses stable Phase110 topics");
            Check(smoke.Contains("phase110 unity tick", StringComparison.Ordinal),
                "110-D9: sample smoke publishes deterministic Phase110 tick payloads");
            Check(smoke.Contains("[Phase110Ros2ForUnityStringSmoke] received:", StringComparison.Ordinal),
                "110-D10: sample smoke logs received strings with the expected prefix");
            Check(smoke.Contains("_publishedCount", StringComparison.Ordinal)
                  && smoke.Contains("_receivedCount", StringComparison.Ordinal)
                  && smoke.Contains("_lastReceived", StringComparison.Ordinal)
                  && smoke.Contains("_lastError", StringComparison.Ordinal)
                  && smoke.Contains("_statusMessage", StringComparison.Ordinal),
                "110-D11: sample smoke exposes status, counters, last received, and last error fields");
        }

        private static void VerifyFacadeBoundary()
        {
            var factory = ReadRepoText(OptionalRuntime + "/Unity2FoxgloveRos2ContextFactory.cs");
            Check(factory.Contains("public static IUnity2FoxgloveRos2Context Create()", StringComparison.Ordinal)
                  && factory.Contains("Unity2FoxgloveRos2UnavailableContext.Instance", StringComparison.Ordinal)
                  && !factory.Contains("GameObject", StringComparison.Ordinal)
                  && !factory.Contains("UnityEngine", StringComparison.Ordinal),
                "110-E1: optional Runtime factory remains host-agnostic and unavailable by default");

            var offenders = TextFiles(OptionalRuntime)
                .SelectMany(path => OptionalRuntimeForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(offenders.Count == 0,
                "110-E2: optional Runtime has no UnityEngine, ROS2, or generated message references"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
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
                "110-F1: core SDK production surface has no hard R2FU dependency"
                + (coreHits.Count == 0 ? string.Empty : " (" + string.Join(", ", coreHits) + ")"));
        }

        private static void VerifyComplianceManifest()
        {
            using var document = JsonDocument.Parse(ReadRepoText(OptionalPackage + "/Compliance/ros2-for-unity-adoption-manifest.json"));
            var root = document.RootElement;
            Check(root.TryGetProperty("bundleStatus", out var bundleStatus)
                  && bundleStatus.GetString() == "not_bundled",
                "110-G1: manifest keeps bundleStatus not_bundled");
            Check(root.TryGetProperty("adapterStatus", out var adapterStatus)
                  && adapterStatus.GetString() == "external_assets_sample",
                "110-G2: manifest records external asset sample adapter status");
            Check(root.TryGetProperty("distributionPolicy", out var distributionPolicy)
                  && distributionPolicy.GetString() == "external_ros2_for_unity_runtime_user_import_required",
                "110-G3: manifest records user-import-required distribution policy");
            Check(root.TryGetProperty("phase110Evidence", out var phase110Evidence)
                  && phase110Evidence.GetString() == "pending",
                "110-G4: manifest records pending Phase110 live evidence");
        }

        private static void VerifyDocs()
        {
            var readme = ReadRepoText(OptionalPackage + "/README.md");
            var sampleReadme = ReadRepoText(SampleReadmePath);
            var combined = readme + "\n" + sampleReadme;

            Check(readme.Contains("external ROS2 For Unity", StringComparison.OrdinalIgnoreCase)
                  && readme.Contains("ROS2 For Unity External Adapter", StringComparison.Ordinal)
                  && readme.Contains("not_bundled", StringComparison.Ordinal),
                "110-H1: optional README documents external-R2FU productization status");
            Check(sampleReadme.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && sampleReadme.Contains("Assets/Ros2ForUnity", StringComparison.Ordinal)
                  && sampleReadme.Contains("Windows ROS2 Jazzy", StringComparison.Ordinal)
                  && sampleReadme.Contains("ros2 topic echo --once " + OutTopic, StringComparison.Ordinal)
                  && sampleReadme.Contains("ros2 topic pub --once " + InTopic, StringComparison.Ordinal),
                "110-H2: sample README gives concrete setup and live smoke commands");
            Check(combined.Contains("WSL2 NAT", StringComparison.Ordinal)
                  && combined.Contains("not a GREEN gate", StringComparison.Ordinal),
                "110-H3: docs keep WSL2 NAT out of the Phase110 GREEN gate");
            Check(combined.Contains("171+", StringComparison.Ordinal)
                  && combined.Contains("standard ROS2 visualization", StringComparison.OrdinalIgnoreCase),
                "110-H4: docs defer standard ROS2 visualization mapping to later phases");
        }

        private static void VerifyTrackedAssetBoundary()
        {
            var forbiddenTracked = GitLsFiles()
                .Where(path => path.Contains("/Assets/Ros2ForUnity/", StringComparison.Ordinal)
                               || path.StartsWith("Unity2Foxglove/Assets/Ros2ForUnity", StringComparison.Ordinal)
                               || IsForbiddenR2fuArtifact(path)
                               || IsOptionalPackageRuntimeBinary(path))
                .ToList();

            Check(forbiddenTracked.Count == 0,
                "110-I1: tracked files contain no R2FU assets, packages, metadata, or optional runtime binaries"
                + (forbiddenTracked.Count == 0 ? string.Empty : " (" + string.Join(", ", forbiddenTracked) + ")"));
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(program.Contains("--phase110", StringComparison.Ordinal)
                  && program.Contains("RunPhase110Only", StringComparison.Ordinal)
                  && program.Contains("Phase110Validation.Validate()", StringComparison.Ordinal),
                "110-J1: Program.cs wires --phase110");
            Check(project.Contains("Phase110Validation.cs", StringComparison.Ordinal),
                "110-J2: test project compiles Phase110Validation");
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
                "IPublisher<",
                "ISubscription<",
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
                throw new FileNotFoundException("Missing required Phase110 file: " + relativePath, path);
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
                throw new DirectoryNotFoundException("Could not find repository root for Phase110 validation.");
            return root;
        }
    }
}
