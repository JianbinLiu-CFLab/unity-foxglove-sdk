// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-89/90/91/92/94 runtime validation optimization checks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Tests;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "192")]
    [Trait("Domain", "Harness")]
    public sealed class FoxgloveRuntimeSourceCompositionTests
    {
        [Theory]
        [InlineData("Phase163_40Validation.cs", "Scripts/release/bump_version.py", "def resolve_repo_root()")]
        [InlineData("Phase164_40Validation.cs", "Scripts/release/bump_version.py", "def run(self)")]
        [InlineData("Phase164_32Validation.cs", "Scripts/smoke/ros2/phase162_lyrical_zenoh_player_smoke.py", "def wait_for_marker(")]
        [InlineData("Phase164_35Validation.cs", "Scripts/smoke/test_core_smoke_scripts.py", "def load_smoke_module(")]
        [InlineData("Phase164_41Validation.cs", "Scripts/smoke/test_core_smoke_scripts.py", "def load_smoke_module(")]
        [InlineData("Phase164_30Validation.cs", "Scripts/smoke/ros2/phase160_humble_lidar_deskew_acceptance.py", "def write_probe_script(")]
        [InlineData("Phase163_33Validation.cs", "Scripts/smoke/ros2/phase160_humble_lidar_deskew_acceptance.py", "def pointcloud_display(")]
        [InlineData("R2fuHumbleRuntimePackageValidation.cs", "Scripts/smoke/ros2/phase160_humble_lidar_deskew_acceptance.py", "def write_probe_script(")]
        [InlineData("R2fuLyricalRuntimePackageValidation.cs", "Scripts/smoke/ros2/phase162_lyrical_zenoh_player_smoke.py", "def wait_for_marker(")]
        public void SplitPythonScannersUseDeclaredSourceSections(string validator, string facade, string signature)
        {
            Assert.Contains("PhaseValidationSourceHelpers.ReadSplitPythonSource", TestSources.Runtime(validator), StringComparison.Ordinal);
            var source = PhaseValidationSourceHelpers.ReadSplitPythonSource(facade);
            Assert.Contains(signature, source, StringComparison.Ordinal);
        }

        [Fact]
        public void RequiredRepoTextReadsPythonFacadesWithTheirSections()
        {
            var cases = new[]
            {
                ("Scripts/mcap/conformance/run_phase121_conformance.py", "OFFICIAL_REPOSITORY_URL"),
                ("Scripts/smoke/ros2/phase128_rviz2_acceptance.py", "pixi"),
                ("Scripts/smoke/ros2/phase129_pointcloud2_acceptance.py", "pixi"),
                ("Scripts/smoke/ros2/phase130_markerarray_acceptance.py", "pixi"),
                ("Scripts/smoke/ros2/phase131_standard_visualization_acceptance.py", "_ros2_windows_env"),
                ("Scripts/smoke/ros2/_ros2_windows_env.py", "ros2_opt_bin_paths"),
            };

            foreach (var (facade, signature) in cases)
                Assert.Contains(signature, PhaseValidationSourceHelpers.ReadRequiredRepoText(facade), StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimePythonReadersUseTheSplitAwareSourceEntryPoint()
        {
            var root = Path.Combine(
                PhaseValidationSourceHelpers.FindRequiredRepoRoot(),
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime");
            var facadePaths = new HashSet<string>(StringComparer.Ordinal)
            {
                "Scripts/mcap/conformance/run_phase121_conformance.py",
                "Scripts/smoke/ros2/phase128_rviz2_acceptance.py",
                "Scripts/smoke/ros2/phase129_pointcloud2_acceptance.py",
                "Scripts/smoke/ros2/phase130_markerarray_acceptance.py",
                "Scripts/smoke/ros2/phase131_standard_visualization_acceptance.py",
                "Scripts/smoke/ros2/_ros2_windows_env.py",
            };
            var violations = new List<string>();
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(path);
                foreach (Match match in Regex.Matches(
                             source,
                             "(?:ReadRepoText|Read)\\(\\s*\\\"(?<path>Scripts/[^\\\"]+\\.py)\\\"",
                             RegexOptions.CultureInvariant))
                {
                    if (!facadePaths.Contains(match.Groups["path"].Value))
                        continue;
                    if (!source.Contains("PhaseValidationSourceHelpers.ReadRequiredRepoText", StringComparison.Ordinal)
                        && !source.Contains("PhaseValidationSourceHelpers.ReadSplitPythonSource", StringComparison.Ordinal))
                        violations.Add(path + ": Python reader bypasses the split-aware helper");
                }
            }

            Assert.Empty(violations);
        }

        [Fact]
        public void FoxgloveRuntimeCompositionFailsClosedAndPreservesTrailingGateOrder()
        {
            var helper = TestSources.ExtractMethod(
                TestSources.Runtime("PhaseValidationSourceHelpers.cs"),
                "public static string ReadFoxgloveRuntimeSources()");
            Assert.Contains(
                "Directory.GetFiles(dir, \"FoxgloveRuntime*.cs\", SearchOption.TopDirectoryOnly)",
                helper,
                StringComparison.Ordinal);
            Assert.Contains("expected.SetEquals(actual)", helper, StringComparison.Ordinal);
            Assert.DoesNotContain(".Where(File.Exists)", helper, StringComparison.Ordinal);

            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var start = source.IndexOf("public void Start(", StringComparison.Ordinal);
            var gate = source.IndexOf("ClientEventGenerationGate", StringComparison.Ordinal);
            Assert.True(start >= 0 && gate > start, "ClientEventGenerationGate must remain after runtime methods.");
        }

        [Fact]
        public void Ros2ForUnitySubscriptionHubCompositionPreservesSplitMethods()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionHub.cs");

            var parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(
                "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                "UNITY_EDITOR_WIN");
            var diagnostics = CSharpSyntaxTree.ParseText(source, parseOptions)
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.Empty(diagnostics);
            Assert.Equal(1, Count(source, "private void Update()"));
            Assert.Equal(1, Count(source, "private void BeginShutdown()"));
            Assert.Contains("private void ScanAndReconcile()", source, StringComparison.Ordinal);
        }

        [Fact]
        public void SharedRos2ForUnitySubscriptionHubHelperRequiresWrapperTrimmingAndActiveSymbols()
        {
            var helper = TestSources.Runtime("Ros2ForUnitySubscriptionHubSourceComposer.cs");
            Assert.Contains(
                "TrimWrapperEnd",
                helper,
                StringComparison.Ordinal);
            Assert.Contains(
                "for (var brace = 0; brace < 2; brace++)",
                helper,
                StringComparison.Ordinal);
            var validation = TestSources.Runtime("R2fuNativeBridgeHotPathLifecycleValidation.cs");
            Assert.Contains("WithPreprocessorSymbols(", validation, StringComparison.Ordinal);
            Assert.Contains("\"UNITY2FOXGLOVE_ROS2_FOR_UNITY\"", validation, StringComparison.Ordinal);
            Assert.Contains("\"UNITY_EDITOR_WIN\"", validation, StringComparison.Ordinal);
        }

        [Fact]
        public void Ros2CustomCdrEmitterCompositionIgnoresTrailingBlankLines()
        {
            var helper = TestSources.Runtime("PhaseValidationSourceHelpers.cs");
            var method = TestSources.ExtractMethod(
                helper,
                "private static int FindRos2CustomCdrEmitterBodyEnd");

            Assert.Contains(
                "string.IsNullOrWhiteSpace(lines[end - 1])",
                method,
                StringComparison.Ordinal);
            Assert.Contains(
                "for (var brace = 0; brace < 2; brace++)",
                method,
                StringComparison.Ordinal);
            Assert.Contains(
                "ReadRos2CustomCdrEmitterFacadeBody",
                helper,
                StringComparison.Ordinal);
            Assert.Contains(
                "ReadRos2CustomCdrEmitterFragmentBody",
                helper,
                StringComparison.Ordinal);
            Assert.Contains(
                "SearchOption.AllDirectories",
                helper,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Ros2BridgeRuntimeCompositionRejectsUnregisteredRootSiblingPartial()
        {
            var root = CreateSourceFixture(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge",
                "Ros2BridgeRuntime.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.QueueAndStats.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.WorkerLifecycle.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.ConnectionAndPreparation.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.RetirementAndNested.cs");
            var runtimeRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Runtime",
                "Ros2Bridge");
            var extra = Path.Combine(runtimeRoot, "Ros2BridgeRuntime.Extra.cs");
            try
            {
                File.WriteAllText(extra, "internal sealed partial class Ros2BridgeWorkerLease { }");
                Assert.Throws<InvalidDataException>(
                    () => PhaseValidationSourceHelpers.ReadRos2BridgeRuntimeSources(root));
            }
            finally
            {
                DeleteSourceFixture(root);
            }
        }

        [Fact]
        public void Ros2BridgeRuntimeCompositionRejectsUnregisteredRootPartialWithDifferentFileName()
        {
            var root = CreateSourceFixture(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge",
                "Ros2BridgeRuntime.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.QueueAndStats.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.WorkerLifecycle.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.ConnectionAndPreparation.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.RetirementAndNested.cs");
            var runtimeRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Runtime",
                "Ros2Bridge");
            try
            {
                File.WriteAllText(
                    Path.Combine(runtimeRoot, "ExtraRuntime.cs"),
                    "internal sealed partial class Ros2BridgeWorkerLease { }");
                Assert.Throws<InvalidDataException>(
                    () => PhaseValidationSourceHelpers.ReadRos2BridgeRuntimeSources(root));
            }
            finally
            {
                DeleteSourceFixture(root);
            }
        }

        [Fact]
        public void Ros2BridgeRuntimeCompositionRejectsConditionalRootPartial()
        {
            var root = CreateSourceFixture(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge",
                "Ros2BridgeRuntime.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.QueueAndStats.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.WorkerLifecycle.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.ConnectionAndPreparation.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.RetirementAndNested.cs");
            var runtimeRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Runtime",
                "Ros2Bridge");
            try
            {
                File.WriteAllText(
                    Path.Combine(runtimeRoot, "ConditionalWorker.cs"),
                    "#if UNDECLARED_RUNTIME_SYMBOL\ninternal sealed partial class Ros2BridgeWorkerLease { }\n#endif");
                Assert.Throws<InvalidDataException>(
                    () => PhaseValidationSourceHelpers.ReadRos2BridgeRuntimeSources(root));
            }
            finally
            {
                DeleteSourceFixture(root);
            }
        }

        [Fact]
        public void Ros2BridgeRuntimeCompositionRejectsConjunctiveConditionalRootPartial()
        {
            var root = CreateSourceFixture(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge",
                "Ros2BridgeRuntime.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.QueueAndStats.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.WorkerLifecycle.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.ConnectionAndPreparation.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.RetirementAndNested.cs");
            var runtimeRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Runtime",
                "Ros2Bridge");
            try
            {
                File.WriteAllText(
                    Path.Combine(runtimeRoot, "ConjunctiveWorker.cs"),
                    "#if EXTRA_WORKER_A && EXTRA_WORKER_B\ninternal sealed partial class Ros2BridgeWorkerLease { }\n#endif");
                Assert.Throws<InvalidDataException>(
                    () => PhaseValidationSourceHelpers.ReadRos2BridgeRuntimeSources(root));
            }
            finally
            {
                DeleteSourceFixture(root);
            }
        }

        [Fact]
        public void Ros2BridgeRuntimeCompositionRejectsNegatedConjunctiveConditionalRootPartial()
        {
            var root = CreateSourceFixture(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge",
                "Ros2BridgeRuntime.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.QueueAndStats.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.WorkerLifecycle.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.ConnectionAndPreparation.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.RetirementAndNested.cs");
            var runtimeRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Runtime",
                "Ros2Bridge");
            try
            {
                File.WriteAllText(
                    Path.Combine(runtimeRoot, "NegatedConjunctiveWorker.cs"),
                    "#if EXTRA_WORKER_A && !EXTRA_WORKER_B\ninternal sealed partial class Ros2BridgeWorkerLease { }\n#endif");
                Assert.Throws<InvalidDataException>(
                    () => PhaseValidationSourceHelpers.ReadRos2BridgeRuntimeSources(root));
            }
            finally
            {
                DeleteSourceFixture(root);
            }
        }

        [Fact]
        public void Ros2BridgeRuntimeCompositionRejectsMissingRegisteredFragment()
        {
            var root = CreateSourceFixture(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge",
                "Ros2BridgeRuntime.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.QueueAndStats.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.WorkerLifecycle.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.ConnectionAndPreparation.cs",
                "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.RetirementAndNested.cs");
            var fragment = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Runtime",
                "Ros2Bridge",
                "Decomposed",
                "Ros2BridgeRuntime",
                "Ros2BridgeRuntime.QueueAndStats.cs");
            try
            {
                File.Delete(fragment);
                Assert.Throws<FileNotFoundException>(
                    () => PhaseValidationSourceHelpers.ReadRos2BridgeRuntimeSources(root));
            }
            finally
            {
                DeleteSourceFixture(root);
            }
        }

        [Fact]
        public void Ros2CustomCdrEmitterCompositionRejectsNestedDuplicateFragmentName()
        {
            var root = CreateSourceFixture(
                "Packages/dev.unity2foxglove.ros2bridge/Editor/FoxRun",
                "Ros2CustomCdrEmitter.cs",
                "Decomposed/Ros2CustomCdrEmitter/Generation.cs",
                "Decomposed/Ros2CustomCdrEmitter/Readers.cs",
                "Decomposed/Ros2CustomCdrEmitter/Writers.cs",
                "Decomposed/Ros2CustomCdrEmitter/ValidationAndProjection.cs",
                "Decomposed/Ros2CustomCdrEmitter/NamingAndUtilities.cs",
                "Decomposed/Ros2CustomCdrEmitter/Shapes.cs");
            var fragmentDirectory = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Editor",
                "FoxRun",
                "Decomposed",
                "Ros2CustomCdrEmitter");
            var nestedDirectory = Path.Combine(fragmentDirectory, "Nested");
            var duplicate = Path.Combine(nestedDirectory, "Generation.cs");
            try
            {
                Directory.CreateDirectory(nestedDirectory);
                File.WriteAllText(duplicate, "internal partial class FoxRunBridgeSourceEmitter { }");
                Assert.Throws<InvalidDataException>(
                    () => PhaseValidationSourceHelpers.ReadRos2CustomCdrEmitterSources(root));
            }
            finally
            {
                DeleteSourceFixture(root);
            }
        }

        [Fact]
        public void Ros2CustomCdrEmitterCompositionWithTrailingBlanksParses()
        {
            var root = CreateSourceFixture(
                "Packages/dev.unity2foxglove.ros2bridge/Editor/FoxRun",
                "Ros2CustomCdrEmitter.cs",
                "Decomposed/Ros2CustomCdrEmitter/Generation.cs",
                "Decomposed/Ros2CustomCdrEmitter/Readers.cs",
                "Decomposed/Ros2CustomCdrEmitter/Writers.cs",
                "Decomposed/Ros2CustomCdrEmitter/ValidationAndProjection.cs",
                "Decomposed/Ros2CustomCdrEmitter/NamingAndUtilities.cs",
                "Decomposed/Ros2CustomCdrEmitter/Shapes.cs");
            var paths = new[]
            {
                Path.Combine(
                    root,
                    "Packages",
                    "dev.unity2foxglove.ros2bridge",
                    "Editor",
                    "FoxRun",
                    "Ros2CustomCdrEmitter.cs"),
                Path.Combine(
                    root,
                    "Packages",
                    "dev.unity2foxglove.ros2bridge",
                    "Editor",
                    "FoxRun",
                    "Decomposed",
                    "Ros2CustomCdrEmitter",
                    "Generation.cs"),
            };
            try
            {
                foreach (var path in paths)
                    File.AppendAllText(
                        path,
                        Environment.NewLine + Environment.NewLine,
                        Encoding.UTF8);

                var source = PhaseValidationSourceHelpers.ReadRos2CustomCdrEmitterSources(root);
                var diagnostics = CSharpSyntaxTree.ParseText(source)
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .ToArray();
                Assert.Empty(diagnostics);
            }
            finally
            {
                DeleteSourceFixture(root);
            }
        }

        private static string CreateSourceFixture(string relativeRoot, params string[] relativeFiles)
        {
            var repositoryRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var fixtureRoot = Path.Combine(
                Path.GetTempPath(),
                "phase192-source-fixture-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var relativeFile in relativeFiles)
                {
                    var source = Path.Combine(
                        repositoryRoot,
                        relativeRoot.Replace('/', Path.DirectorySeparatorChar),
                        relativeFile.Replace('/', Path.DirectorySeparatorChar));
                    var target = Path.Combine(
                        fixtureRoot,
                        relativeRoot.Replace('/', Path.DirectorySeparatorChar),
                        relativeFile.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(source, target);
                }
            }
            catch
            {
                DeleteSourceFixture(fixtureRoot);
                throw;
            }
            return fixtureRoot;
        }

        private static void DeleteSourceFixture(string fixtureRoot)
        {
            if (Directory.Exists(fixtureRoot))
                Directory.Delete(fixtureRoot, true);
        }

        private static int Count(string source, string value)
            => source.Split(new[] { value }, StringSplitOptions.None).Length - 1;
    }

    [Trait("Phase", "140-89")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2BridgeSchemaOptimizationTests
    {
        [Fact]
        public void Phase90CachesStableRelativePathsBeforeHashing()
        {
            var method = TestSources.Slice(TestSources.Runtime("Phase90Validation.cs"), "private static string ComputeSourceTreeSha256", "        private static string ToStableRelativePath");

            Assert.Contains("Select(path => new SourceFilePath(path, ToStableRelativePath(sourceRoot, path)))", method, StringComparison.Ordinal);
            Assert.Contains("OrderBy(file => file.RelativePath", method, StringComparison.Ordinal);
            Assert.Contains("Encoding.UTF8.GetBytes(file.RelativePath)", method, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase91ReusesPointCloudFrameAcrossBuilderChecks()
        {
            var source = TestSources.Runtime("Phase91Validation.cs");
            var verifyMethod = TestSources.Slice(source, "private static void VerifyMessageBuilders", "        private static void VerifyFrameTransformBuilder");

            Assert.Contains("var pointFrame = BuildPointCloudFrame();", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("VerifyPointCloudBuilder(pointFrame);", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("VerifyCompressedPointCloudBuilder(pointFrame);", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("private static void VerifyPointCloudSharedPacking(PointCloudFrame frame)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase100DelegatesMethodExtractionToSharedRoslynParser()
        {
            var source = TestSources.Runtime("Phase100Validation.cs");
            var method = TestSources.Slice(source, "private static string ExtractMethodBody", "        private static string ReadRepoText");

            Assert.Contains("PhaseValidationSourceHelpers.SourceMethod(source, methodName)", method, StringComparison.Ordinal);
            Assert.DoesNotContain("FindMethodSignature", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MethodSignatureRegexes", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14089MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_89Validation.cs", "--phase140-89", "Phase140_89Validation.Validate");
    }






class TestSources
    {
        private const string FoxgloveRuntimeTrailingGateMarker =
            "    /// <summary>Shared executable generation predicate for main-thread client events.";
        private static readonly string[] FoxgloveRuntimeSourceFileNames =
        {
            "FoxgloveRuntime.cs",
            "FoxgloveRuntime.Lifecycle.cs",
            "FoxgloveRuntime.Publishing.cs",
            "FoxgloveRuntime.RecordingAndReplay.cs",
            "FoxgloveRuntime.RuntimeAndSchema.cs",
            "FoxgloveRuntime.ReplaySuppression.cs",
            "FoxgloveRuntime.SchemaRegistration.cs",
        };
        private static readonly string CachedRepoRoot = FindRepoRoot();

        public static string Runtime(string fileName)
            => Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + fileName);

        public static string Text(string relativePath)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs",
                    StringComparison.Ordinal))
                return McapReplayEngineSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs",
                    StringComparison.Ordinal))
                return FoxgloveRuntimeSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionHub.cs",
                    StringComparison.Ordinal))
                return Ros2ForUnitySubscriptionHubSources();

            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "Source file not found: " + relativePath + " (" + path + ")");
            return ReadLogicalSource(path);
        }

        private static string ReadLogicalSource(string facadePath)
        {
            var source = File.ReadAllText(facadePath);
            var package = Path.Combine(
                Path.GetDirectoryName(facadePath),
                Path.GetFileNameWithoutExtension(facadePath));
            if (!Directory.Exists(package))
                return source;

            var sections = Directory.GetFiles(package, "*.py", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(Path.GetFileName(path), "__init__.py", StringComparison.Ordinal)
                    && !string.Equals(Path.GetFileName(path), "__main__.py", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText);
            return source + Environment.NewLine + string.Join(Environment.NewLine, sections);
        }

        private static string FoxgloveRuntimeSources()
        {
            var root = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Core",
                "Runtime");
            var fileNames = FoxgloveRuntimeSourceFileNames;
            var expected = new HashSet<string>(fileNames, StringComparer.Ordinal);
            var actual = Directory.GetFiles(root, "FoxgloveRuntime*.cs", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);
            Assert.True(
                expected.SetEquals(actual),
                "FoxgloveRuntime source composition is incomplete or contains unregistered partial files. " +
                "Missing: " + string.Join(", ", expected.Except(actual).OrderBy(name => name, StringComparer.Ordinal)) +
                "; unexpected: " + string.Join(", ", actual.Except(expected).OrderBy(name => name, StringComparer.Ordinal)));

            var facade = File.ReadAllText(Path.Combine(root, fileNames[0]));
            var trailingGateStart = facade.LastIndexOf(
                FoxgloveRuntimeTrailingGateMarker,
                StringComparison.Ordinal);
            Assert.True(trailingGateStart >= 0, "FoxgloveRuntime facade is missing ClientEventGenerationGate.");

            var sources = new[] { facade.Substring(0, trailingGateStart).TrimEnd() }
                .Concat(fileNames.Skip(1).Select(name => File.ReadAllText(Path.Combine(root, name))))
                .Concat(new[] { facade.Substring(trailingGateStart).TrimStart() });
            return string.Join(Environment.NewLine, sources);
        }

        private static string McapReplayEngineSources()
        {
            var paths = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/Load.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/History.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/TryReadIndexedBoundedHistory.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/HistoryCandidate.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/SortPending.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/FinishTickResult.cs"
            };

            var facade = TextWithoutComposition(paths[0]);
            var fragments = paths.Skip(1).Select(path => TextWithoutComposition(path));
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                new[] { facade }.Concat(fragments))
                + Environment.NewLine
                + "    }"
                + Environment.NewLine
                + "}";
        }

        private static string Ros2ForUnitySubscriptionHubSources()
            => Unity.FoxgloveSDK.Tests.Ros2ForUnitySubscriptionHubSourceComposer.Compose(RepoRoot);

        private static string TextWithoutComposition(string relativePath)
        {
            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "Source file not found: " + relativePath + " (" + path + ")");
            var lines = File.ReadAllLines(path);
            if (relativePath.EndsWith("McapReplayEngine.cs", StringComparison.Ordinal))
            {
                Assert.True(lines.Length >= 3, "MCAP replay facade is too short: " + relativePath);
                return string.Join(Environment.NewLine, lines.Take(lines.Length - 2));
            }

            var classLine = Array.FindIndex(
                lines,
                line => line.Contains("partial class McapReplayEngine", StringComparison.Ordinal));
            Assert.True(classLine >= 0, "MCAP replay fragment class is missing: " + relativePath);
            var openBrace = Array.FindIndex(
                lines,
                classLine,
                line => line.Trim() == "{");
            Assert.True(openBrace >= 0 && lines.Length - openBrace >= 4, "MCAP replay fragment is too short: " + relativePath);
            return string.Join(Environment.NewLine, lines.Skip(openBrace + 1).Take(lines.Length - openBrace - 3));
        }

        public static string SourceGeneratorSources()
        {
            var dir = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "SourceGenerators",
                "src");
            Assert.True(Directory.Exists(dir), "Source generator src directory not found: " + dir);

            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        public static string ManagerEditorSources()
        {
            var dir = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "Manager");
            Assert.True(Directory.Exists(dir), "Manager editor directory not found: " + dir);

            var files = Directory.GetFiles(dir, "FoxgloveManagerEditor*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        public static string ManagerPublishingSources()
        {
            var dir = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Components",
                "Manager");
            Assert.True(Directory.Exists(dir), "Manager directory not found: " + dir);

            var files = Directory.GetFiles(dir, "FoxgloveManager.Publishing*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        public static void AssertConsolePhaseRemoved(string validationFile, string flag, string entryPoint)
        {
            Assert.DoesNotContain(validationFile, Runtime("FoxgloveSdk.Tests.csproj"), StringComparison.Ordinal);
            var removedPath = Path.Combine(RepoRoot, "Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime", validationFile);
            Assert.False(File.Exists(removedPath), "Migrated console validation source still exists: " + removedPath);
            var registry = Runtime("PhaseValidationRegistry.cs");
            Assert.DoesNotContain("\"" + flag + "\"", registry, StringComparison.Ordinal);
            Assert.DoesNotContain(entryPoint, registry, StringComparison.Ordinal);
        }

        public static string Slice(string source, string startText, string endText)
        {
            var normalized = NormalizeLineEndings(source);
            var normalizedStart = NormalizeLineEndings(startText);
            var normalizedEnd = NormalizeLineEndings(endText);
            var start = normalized.IndexOf(normalizedStart, StringComparison.Ordinal);
            Assert.True(start >= 0, "Could not locate source slice start: " + startText);
            var end = normalized.IndexOf(normalizedEnd, start + normalizedStart.Length, StringComparison.Ordinal);
            Assert.True(end >= 0, "Could not locate source slice end: " + endText);
            return normalized.Substring(start, end - start);
        }

        public static string ExtractMethod(string source, string signature)
        {
            var index = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(index >= 0, "Could not locate method signature: " + signature);
            var brace = source.IndexOf('{', index);
            Assert.True(brace >= 0, "Could not locate method body: " + signature);

            var depth = 0;
            var inString = false;
            var inVerbatimString = false;
            var inCharacter = false;
            var inLineComment = false;
            var inBlockComment = false;
            var inBacktickString = false;
            var rawStringDelimiterLength = 0;
            for (var i = brace; i < source.Length; i++)
            {
                var current = source[i];
                var next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (inLineComment)
                {
                    if (current == '\n')
                        inLineComment = false;
                    continue;
                }

                if (inBlockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }

                if (rawStringDelimiterLength > 0)
                {
                    if (current == '"' && CountRun(source, i, '"') >= rawStringDelimiterLength)
                    {
                        i += rawStringDelimiterLength - 1;
                        rawStringDelimiterLength = 0;
                    }
                    continue;
                }

                if (inString)
                {
                    if (inVerbatimString && current == '"' && next == '"')
                    {
                        i++;
                        continue;
                    }

                    if (current == '"' && (inVerbatimString || !IsEscaped(source, i)))
                    {
                        inString = false;
                        inVerbatimString = false;
                    }
                    continue;
                }

                if (inCharacter)
                {
                    if (current == '\'' && !IsEscaped(source, i))
                        inCharacter = false;
                    continue;
                }

                if (inBacktickString)
                {
                    if (current == '`' && !IsEscaped(source, i))
                        inBacktickString = false;
                    continue;
                }

                if (current == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (current == '"')
                {
                    var quoteRun = CountRun(source, i, '"');
                    if (quoteRun >= 3)
                    {
                        rawStringDelimiterLength = quoteRun;
                        i += quoteRun - 1;
                    }
                    else
                    {
                        inString = true;
                        inVerbatimString = i > 0 && source[i - 1] == '@';
                    }
                    continue;
                }

                if (current == '\'')
                {
                    inCharacter = true;
                    continue;
                }

                if (current == '`')
                {
                    inBacktickString = true;
                    continue;
                }

                if (current == '{') depth++;
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(index, i - index + 1);
                }
            }

            return source.Substring(index);
        }

        private static int CountRun(string source, int index, char value)
        {
            var count = 0;
            while (index + count < source.Length && source[index + count] == value)
                count++;
            return count;
        }

        private static bool IsEscaped(string source, int index)
        {
            var slashCount = 0;
            for (var i = index - 1; i >= 0 && source[i] == '\\'; i--)
                slashCount++;
            return slashCount % 2 == 1;
        }

        public static int Count(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string NormalizeLineEndings(string text)
            => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

        private static string RepoRoot
            => CachedRepoRoot;

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }

}
