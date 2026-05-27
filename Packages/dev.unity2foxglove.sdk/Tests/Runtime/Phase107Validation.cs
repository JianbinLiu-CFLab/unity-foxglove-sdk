// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 107 ROS2 For Unity optional package distribution gate validation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase107Validation
    {
        private const string OptionalPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string RuntimePackage = "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
        private const string CorePackageJson = "Packages/dev.unity2foxglove.sdk/package.json";
        private const string Manifest = OptionalPackage + "/Compliance/ros2-for-unity-adoption-manifest.json";
        private const string OptionalPackageValidator = "Scripts/release/validate_ros2forunity_package.py";
        private const string JazzyArtifactName = "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip";
        // Mirrors the adoption manifest and release-side hash sidecar; this check catches accidental artifact swaps.
        private const string JazzyArtifactSha256 = "22baf2b624b0fb171efc94b403876491a66e57b39b6f747a3c2e30644ce32188";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 107: ROS2 For Unity Optional Package Distribution Gate ===");
            _passed = 0;

            VerifyOptionalPackageSkeleton();
            VerifyAdoptionManifest();
            VerifyCorePackageBoundary();
            VerifyDocsBoundary();
            VerifyValidationWiring();
            VerifyBinaryBoundary();

            Console.WriteLine($"Phase 107: {_passed} checks passed.");
        }

        private static void VerifyOptionalPackageSkeleton()
        {
            Check(RepoDirectoryExists(OptionalPackage), "107-A1: optional ROS2 For Unity package folder exists");

            var packageJson = LoadJsonObject(OptionalPackage + "/package.json");
            Check((string)packageJson["name"] == "dev.unity2foxglove.ros2forunity",
                "107-A2: optional package name is dev.unity2foxglove.ros2forunity");
            Check((string)packageJson["version"] == "0.1.0-preview.1",
                "107-A3: optional package version is 0.1.0-preview.1");
            Check((string)packageJson["displayName"] == "Unity2Foxglove ROS2 For Unity",
                "107-A4: optional package display name is stable");
            Check((string)packageJson["license"] == "Apache-2.0",
                "107-A5: optional package license is Apache-2.0");
            Check(packageJson["dependencies"] is JObject dependencies && dependencies.Count == 0,
                "107-A6: optional package has no Phase107 dependencies");

            var requiredFiles = new[]
            {
                OptionalPackage + "/README.md",
                OptionalPackage + "/LICENSE",
                OptionalPackage + "/THIRD_PARTY_NOTICES.md",
                OptionalPackage + "/Upstream/LICENSE.AL2",
                Manifest
            };

            foreach (var required in requiredFiles)
                Check(RepoFileExists(required), "107-A7: required optional package file exists: " + required);

            VerifyOptionalEditorBoundary();
            VerifyOptionalRuntimeBoundary();
        }

        private static void VerifyOptionalEditorBoundary()
        {
            var editorRoot = Path.Combine(RepoRoot(), OptionalPackage.Replace('/', Path.DirectorySeparatorChar), "Editor");
            if (!Directory.Exists(editorRoot))
            {
                Check(true, "107-A8: optional package Editor surface is absent or package-activation-only");
                return;
            }

            var invalidFiles = Directory.GetFiles(editorRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => !HasTextExtension(path)
                               && !Path.GetExtension(path).Equals(".meta", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/'))
                .ToList();
            var tokenHits = new List<string>();
            foreach (var path in Directory.GetFiles(editorRoot, "*.*", SearchOption.AllDirectories).Where(HasTextExtension))
            {
                var text = File.ReadAllText(path);
                tokenHits.AddRange(OptionalEditorForbiddenTokens()
                    .Where(token => text.Contains(token, StringComparison.Ordinal))
                    .Select(token => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/') + " -> " + token));
            }
            var installer = Path.Combine(editorRoot, "Ros2ForUnityRuntimeDefineInstaller.cs");
            var installerText = File.Exists(installer) ? File.ReadAllText(installer) : string.Empty;

            Check(invalidFiles.Count == 0
                  && tokenHits.Count == 0
                  && installerText.Contains("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", StringComparison.Ordinal)
                  && installerText.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && installerText.Contains("NamedBuildTarget.Standalone", StringComparison.Ordinal),
                "107-A8: optional package Editor surface only auto-enables the runtime compile symbol"
                + (invalidFiles.Count == 0 && tokenHits.Count == 0 ? string.Empty : " (" + string.Join(", ", invalidFiles.Concat(tokenHits)) + ")"));
        }

        private static void VerifyAdoptionManifest()
        {
            var manifest = LoadJsonObject(Manifest);

            Check((string)manifest["upstreamName"] == "RobotecAI ROS2 For Unity",
                "107-B1: manifest records upstream name");
            Check((string)manifest["upstreamRepository"] == "https://github.com/RobotecAI/ros2-for-unity",
                "107-B2: manifest records upstream repository");
            Check((string)manifest["upstreamLicense"] == "Apache-2.0"
                  && (string)manifest["upstreamLicenseFile"] == "Upstream/LICENSE.AL2",
                "107-B3: manifest records upstream license and copied license path");
            Check((int?)manifest["schemaVersion"] == 2,
                "107-B4: manifest records schema version 2");

            var currentRuntime = (JObject)manifest["currentRecommendedRuntime"]!;
            Check((string)currentRuntime["packageName"] == "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
                  && (string)currentRuntime["rosDistro"] == "jazzy"
                  && (string)currentRuntime["supportLevel"] == "Recommended"
                  && (string)currentRuntime["distributionLevel"] == "BundleCandidate",
                "107-B5: manifest records Jazzy runtime package candidate");
            Check((string)currentRuntime["artifact"] == JazzyArtifactName
                  && (string)currentRuntime["artifactSha256"] == JazzyArtifactSha256,
                "107-B6: manifest records Jazzy runtime artifact identity");

            var legacyRuntime = (JObject)manifest["legacyRuntime"]!;
            Check((string)legacyRuntime["releaseAsset"] == "Ros2ForUnity_humble_standalone_windows11.zip"
                  && ((string)legacyRuntime["releaseAssetUrl"]!).Contains("Ros2ForUnity_humble_standalone_windows11.zip", StringComparison.Ordinal)
                  && (string)legacyRuntime["supportLevel"] == "LegacySupported"
                  && (string)legacyRuntime["distributionLevel"] == "ExternalOnly",
                "107-B7: manifest keeps Humble as legacy external evidence");
            Check(((string)legacyRuntime["evidence"]!).Contains("WINDOWS_ROS2_GREEN", StringComparison.Ordinal)
                  && ((string)legacyRuntime["evidence"]!).Contains("WSL_ROS2_DISCOVERY_BLOCKED", StringComparison.Ordinal),
                "107-B8: manifest records legacy ROS2 For Unity verdicts");
            Check(((string)manifest["upstreamSupportStatus"]).Contains("AWSIM/Autoware", StringComparison.Ordinal)
                  && ((string)manifest["upstreamSupportStatus"]).Contains("general community", StringComparison.Ordinal),
                "107-B9: manifest preserves upstream support caveat");
            var distributionPolicy = (string)manifest["distributionPolicy"];
            Check((string)manifest["bundleStatus"] == "not_bundled"
                  && distributionPolicy == "runtime_artifacts_live_in_separate_runtime_packages"
                  && (string)manifest["distributionModel"] == "one_repo_multi_package_release_artifacts",
                "107-B10: manifest records not-bundled multi-package distribution policy");
            Check((string)manifest["knownRuntimeRmw"] == "rmw_fastrtps_cpp"
                  && (string)manifest["knownRuntimeRosDistro"] == "jazzy"
                  && (string)manifest["activeRuntimePolicy"] == "one_runtime_package_per_project",
                "107-B11: manifest records Jazzy runtime and one-runtime policy");
            Check(manifest["packageComposition"] is JObject composition
                  && composition["adapterAlone"] != null
                  && composition["runtimeAlone"] != null
                  && composition["adapterPlusRuntime"] != null,
                "107-B12: manifest records standalone and combined package composition");
            Check(manifest["modifications"] is JArray modifications && modifications.Count == 0,
                "107-B13: manifest records no upstream modifications");
        }

        private static void VerifyCorePackageBoundary()
        {
            var core = LoadJsonObject(CorePackageJson);
            var dependencies = core["dependencies"] as JObject;
            Check(dependencies == null || dependencies["dev.unity2foxglove.ros2forunity"] == null,
                "107-C1: core SDK package does not depend on optional R2FU package");

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
                "Ros2ForUnity",
                "dev.unity2foxglove.ros2forunity"
            };

            var hits = new List<string>();
            foreach (var path in scanRoots
                         .Where(Directory.Exists)
                         .SelectMany(rootDir => Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories))
                         .Where(HasTextExtension))
            {
                var text = File.ReadAllText(path);
                hits.AddRange(forbidden
                    .Where(token => text.Contains(token, StringComparison.Ordinal))
                    .Select(token => Path.GetRelativePath(root, path).Replace('\\', '/') + " -> " + token));
            }

            Check(hits.Count == 0,
                "107-C2: core package Runtime/Editor/Samples have no hard R2FU dependency"
                + (hits.Count == 0 ? string.Empty : " (" + string.Join(", ", hits) + ")"));
        }

        private static void VerifyDocsBoundary()
        {
            var readme = ReadRepoText("README.md");
            var roadmap = ReadRepoText("ROADMAP.md");
            var optionalReadme = ReadRepoText(OptionalPackage + "/README.md");
            var notices = ReadRepoText(OptionalPackage + "/THIRD_PARTY_NOTICES.md");
            var combined = readme + "\n" + roadmap + "\n" + optionalReadme + "\n" + notices;

            Check(readme.Contains("Packages/dev.unity2foxglove.sdk", StringComparison.Ordinal)
                  && readme.Contains("Packages/dev.unity2foxglove.ros2forunity", StringComparison.Ordinal)
                  && readme.Contains("Unity2Foxglove demo project", StringComparison.Ordinal),
                "107-D1: README describes core, optional ROS2, and demo project package model");
            Check(readme.Contains("normal Foxglove WebSocket streaming, MCAP recording, or replay", StringComparison.Ordinal)
                  && readme.Contains("no ROS2", StringComparison.OrdinalIgnoreCase),
                "107-D2: README keeps no-ROS default prominent");
            Check(combined.Contains("WSL2 NAT", StringComparison.Ordinal)
                  && combined.Contains("real LAN", StringComparison.Ordinal)
                  && combined.Contains("bridged", StringComparison.OrdinalIgnoreCase),
                "107-D3: docs state WSL2 NAT is not the remote Linux acceptance gate");
            Check(roadmap.Contains("one-repo, multi-package", StringComparison.Ordinal)
                  && roadmap.Contains("R2FU adapter/runtime package line", StringComparison.Ordinal)
                  && roadmap.Contains("170-series", StringComparison.Ordinal),
                "107-D4: roadmap marks R2FU optional packages as ROS2 mainline and defers old ROS2 plans");
            Check(optionalReadme.Contains("runtime binaries are not bundled", StringComparison.OrdinalIgnoreCase)
                  && (optionalReadme.Contains("future adapter", StringComparison.OrdinalIgnoreCase)
                      || optionalReadme.Contains("external adapter", StringComparison.OrdinalIgnoreCase)),
                "107-D5: optional package README preserves runtime non-bundling boundary");
            Check(notices.Contains("RobotecAI ROS2 For Unity", StringComparison.Ordinal)
                  && notices.Contains("ros2cs", StringComparison.Ordinal)
                  && notices.Contains("not bundle", StringComparison.OrdinalIgnoreCase)
                  && notices.Contains("complete transitive inventory", StringComparison.OrdinalIgnoreCase),
                "107-D6: optional package notices distinguish upstream ownership and future binary inventory");
        }

        private static void VerifyValidationWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("--phase107", StringComparison.Ordinal)
                  && registry.Contains("Phase107Validation.Validate", StringComparison.Ordinal),
                "107-E1: registry wires --phase107");
            Check(project.Contains("Phase107Validation.cs", StringComparison.Ordinal),
                "107-E2: test project compiles Phase107Validation");
            Check(RepoFileExists(OptionalPackageValidator),
                "107-E3: optional package release validator exists");
        }

        private static void VerifyBinaryBoundary()
        {
            var tracked = GitLsFiles();
            var forbiddenTracked = tracked
                .Where(path => !IsAllowedRuntimePackageFile(path))
                .Where(path => path.StartsWith("Unity2Foxglove/Assets/Ros2ForUnity", StringComparison.Ordinal)
                               || IsForbiddenR2fuArtifact(path)
                               || IsOptionalPackageRuntimeBinary(path))
                .ToList();

            Check(forbiddenTracked.Count == 0,
                "107-F1: tracked files contain no R2FU imported assets, raw artifacts, or adapter runtime binaries outside runtime packages"
                + (forbiddenTracked.Count == 0 ? string.Empty : " (" + string.Join(", ", forbiddenTracked) + ")"));

            var optionalRoot = Path.Combine(RepoRoot(), OptionalPackage.Replace('/', Path.DirectorySeparatorChar));
            var optionalRuntimeFiles = Directory.Exists(optionalRoot)
                ? Directory.GetFiles(optionalRoot, "*.*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/'))
                    .Where(IsOptionalPackageRuntimeBinary)
                    .ToList()
                : new List<string>();

            Check(optionalRuntimeFiles.Count == 0,
                "107-F2: optional package working tree contains no runtime binaries while bundleStatus is not_bundled"
                + (optionalRuntimeFiles.Count == 0 ? string.Empty : " (" + string.Join(", ", optionalRuntimeFiles) + ")"));
        }

        private static void VerifyOptionalRuntimeBoundary()
        {
            var runtimeRoot = Path.Combine(RepoRoot(), OptionalPackage.Replace('/', Path.DirectorySeparatorChar), "Runtime");
            if (!Directory.Exists(runtimeRoot))
            {
                Check(true, "107-A9: optional package Runtime is absent or facade-only");
                return;
            }

            var offenders = Directory.GetFiles(runtimeRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => !IsAllowedRuntimeSource(path)
                               || ContainsForbiddenRuntimeToken(path))
                .Select(path => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/'))
                .ToList();

            Check(offenders.Count == 0,
                "107-A9: optional package Runtime contains only facade source and no upstream ROS2/R2FU API references"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
        }

        private static bool IsAllowedRuntimeSource(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".meta", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsForbiddenRuntimeToken(string path)
        {
            if (!HasTextExtension(path))
                return false;

            var text = File.ReadAllText(path);
            return OptionalEditorForbiddenTokens().Any(token => text.Contains(token, StringComparison.Ordinal));
        }

        private static IReadOnlyList<string> OptionalEditorForbiddenTokens()
        {
            return new[]
            {
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

        private static bool IsForbiddenR2fuArtifact(string path)
        {
            return PhaseRos2ForUnityValidationHelpers.IsForbiddenR2fuArtifact(path);
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

        private static bool IsAllowedRuntimePackageFile(string path)
        {
            if (!path.StartsWith(RuntimePackage + "/", StringComparison.Ordinal))
                return false;

            return !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                   && !path.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                   && !path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase);
        }

        private static JObject LoadJsonObject(string relativePath)
        {
            var text = ReadRepoText(relativePath);
            return JObject.Parse(text);
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

        private static bool RepoDirectoryExists(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(path);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = RepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase107 file: " + relativePath, path);
            return File.ReadAllText(path);
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
                throw new DirectoryNotFoundException("Could not find repository root for Phase107 validation.");
            return root;
        }
    }
}
