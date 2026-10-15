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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Tests;
using Xunit;


namespace Unity.FoxgloveSDK.UnitTests.Harness
{
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

            [Fact]
            public void RestoredSdkTestIdSetRemainsExplicit()
            {
                var source = TestSources.Text(
                    "Packages/dev.unity2foxglove.sdk/Tests/Unit/Decomposed/Harness/RuntimeValidationOptimizationTests/FoxgloveRuntimeSourceCompositionTests.cs");
                var expected = new HashSet<string>(new[]
                {
                    "SplitPythonScannersUseDeclaredSourceSections",
                    "RequiredRepoTextReadsPythonFacadesWithTheirSections",
                    "RuntimePythonReadersUseTheSplitAwareSourceEntryPoint",
                    "FoxgloveRuntimeCompositionFailsClosedAndPreservesTrailingGateOrder",
                    "Ros2ForUnitySubscriptionHubCompositionPreservesSplitMethods",
                    "SharedRos2ForUnitySubscriptionHubHelperRequiresWrapperTrimmingAndActiveSymbols",
                    "Ros2CustomCdrEmitterCompositionIgnoresTrailingBlankLines",
                    "Ros2BridgeRuntimeCompositionRejectsUnregisteredRootSiblingPartial",
                    "Ros2BridgeRuntimeCompositionRejectsUnregisteredRootPartialWithDifferentFileName",
                    "Ros2BridgeRuntimeCompositionRejectsConditionalRootPartial",
                    "Ros2BridgeRuntimeCompositionRejectsConjunctiveConditionalRootPartial",
                    "Ros2BridgeRuntimeCompositionRejectsNegatedConjunctiveConditionalRootPartial",
                    "Ros2BridgeRuntimeCompositionRejectsMissingRegisteredFragment",
                    "Ros2CustomCdrEmitterCompositionRejectsNestedDuplicateFragmentName",
                    "Ros2CustomCdrEmitterCompositionWithTrailingBlanksParses",
                }, StringComparer.Ordinal);
                var actual = CSharpSyntaxTree.ParseText(source)
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Where(method => method.AttributeLists
                        .SelectMany(list => list.Attributes)
                        .Any(attribute => attribute.Name.ToString() is "Fact" or "Theory"))
                    .Where(method => method.Identifier.ValueText != nameof(RestoredSdkTestIdSetRemainsExplicit))
                    .Select(method => method.Identifier.ValueText)
                    .ToHashSet(StringComparer.Ordinal);

                Assert.True(expected.SetEquals(actual),
                    "Restored SDK test IDs changed. Expected: "
                    + string.Join(", ", expected.OrderBy(name => name, StringComparer.Ordinal))
                    + "; actual: "
                    + string.Join(", ", actual.OrderBy(name => name, StringComparer.Ordinal)));
                Assert.Equal(9, Count(source, "[InlineData(") - 1);

                foreach (var (path, methodName) in new[]
                {
                    ("Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/McapReplayEngineReviewTests.cs", "ReplaySourceCompositionPreservesOriginalMemberOrder"),
                    ("Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/RuntimeHarnessTests.cs", "FoxgloveSessionCompositionHasValidSyntaxAndContainsAllMethods"),
                })
                {
                    var standalone = TestSources.Text(path);
                    Assert.Contains("public ", standalone, StringComparison.Ordinal);
                    Assert.Contains(methodName, standalone, StringComparison.Ordinal);
                }
            }

            private static int Count(string source, string value)
                => source.Split(new[] { value }, StringSplitOptions.None).Length - 1;
        }
}
