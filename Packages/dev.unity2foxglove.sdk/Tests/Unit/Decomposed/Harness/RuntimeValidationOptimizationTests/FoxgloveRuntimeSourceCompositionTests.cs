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
                    ("Scripts/schema/generate_ros2_cdr_serializers.py", "def capacity_hint_for_schema"),
                    ("Scripts/schema/regression_checks/test_schema_tooling/schema_tests.py", "class SchemaToolingTests"),
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
            public void SchemaGeneratorFacadeDoesNotHideSectionMarkers()
            {
                var facade = File.ReadAllText(Path.Combine(
                    PhaseValidationSourceHelpers.FindRequiredRepoRoot(),
                    "Scripts/schema/generate_ros2_cdr_serializers.py"));
                Assert.DoesNotContain("_SOURCE_READER_MARKERS", facade, StringComparison.Ordinal);
                Assert.DoesNotContain("def capacity_hint_for_schema", facade, StringComparison.Ordinal);
                Assert.DoesNotContain("def writer_for_field", facade, StringComparison.Ordinal);
                Assert.DoesNotContain("def generate_serializers", facade, StringComparison.Ordinal);

                var source = PhaseValidationSourceHelpers.ReadSplitPythonSource("Scripts/schema/generate_ros2_cdr_serializers.py");
                Assert.Contains("def capacity_hint_for_schema", source, StringComparison.Ordinal);
                Assert.Contains("def writer_for_field", source, StringComparison.Ordinal);
                Assert.Contains("def generate_serializers", source, StringComparison.Ordinal);

                var sensor = TestSources.Text("Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Harness/SensorRos2OptimizationTests.cs");
                Assert.Contains("Scripts/schema/generate_ros2_cdr_serializers/generation_helpers.py", sensor, StringComparison.Ordinal);
                Assert.Contains("Scripts/schema/generate_ros2_cdr_serializers/serialization.py", sensor, StringComparison.Ordinal);
                Assert.DoesNotContain("Scripts/schema/generate_ros2_cdr_serializers.py\")", sensor, StringComparison.Ordinal);
            }

            [Fact]
            public void MessagePackInputDispatchEmitterCompositionPreservesDeclaredSections()
            {
                const string facadePath = "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/MessagePackInputDispatchEmitter.cs";
                var facade = TestSources.Text(facadePath);
                var source = PhaseValidationSourceHelpers.ReadRequiredRepoText(facadePath);
                foreach (var section in new[]
                {
                    "MessagePackInputDispatchEmitter.Core.cs",
                    "MessagePackInputDispatchEmitter.Readers.cs",
                    "MessagePackInputDispatchEmitter.Shapes.cs",
                })
                {
                    Assert.Contains(section, facade, StringComparison.Ordinal);
                    Assert.Contains(section, source, StringComparison.Ordinal);
                }

                Assert.Contains("IFoxgloveTransactionalInputSource", source, StringComparison.Ordinal);
                Assert.Contains("FoxgloveMsgPackReader", source, StringComparison.Ordinal);
                Assert.Contains("TryReserveInput", source, StringComparison.Ordinal);
            }

            [Fact]
            public void RuntimePythonReadersUseTheSplitAwareSourceEntryPoint()
            {
                var root = Path.Combine(PhaseValidationSourceHelpers.FindRequiredRepoRoot(), "Packages");
                var violations = new List<string>();
                foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                             .Where(path => !IsGeneratedSourcePath(root, path)))
                {
                    var source = File.ReadAllText(path);
                    var logicalSource = source;
                    var fragments = Path.Combine(
                        Path.GetDirectoryName(path)!,
                        "Decomposed",
                        Path.GetFileNameWithoutExtension(path));
                    if (Directory.Exists(fragments))
                    {
                        logicalSource += Environment.NewLine + string.Join(
                            Environment.NewLine,
                            Directory.EnumerateFiles(fragments, "*.cs", SearchOption.AllDirectories)
                                .Select(File.ReadAllText));
                    }

                    var splitAwareReaders = FindSplitAwareReaderMethods(logicalSource);
                    foreach (Match match in Regex.Matches(
                                 logicalSource,
                                 "(?<![\\w.])(?<reader>ReadRequiredRepoText|ReadSplitPythonSource|ReadRepoText|Read)\\(\\s*(?<argument>[^)]*?)\\s*\\)",
                                 RegexOptions.CultureInvariant))
                    {
                        var relativePath = ResolvePythonPath(logicalSource, match.Groups["argument"].Value);
                        if (string.IsNullOrEmpty(relativePath))
                            continue;
                        relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                        var facadePath = Path.Combine(root, "..", relativePath);
                        var packagePath = Path.Combine(
                            Path.GetDirectoryName(facadePath)!,
                            Path.GetFileNameWithoutExtension(facadePath));
                        var decomposedPath = Path.Combine(
                            Path.GetDirectoryName(facadePath)!,
                            "Decomposed",
                            Path.GetFileNameWithoutExtension(facadePath));
                        if (!Directory.Exists(packagePath) && !Directory.Exists(decomposedPath))
                            continue;

                        var reader = match.Groups["reader"].Value;
                        if (!IsSplitAwareReader(reader, splitAwareReaders))
                            violations.Add(path + ": Python reader " + reader + " bypasses the split-aware helper");
                    }
                }

                Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
            }

            private static HashSet<string> FindSplitAwareReaderMethods(string source)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match match in Regex.Matches(
                             source,
                             "(?ms)(?:public|private|internal|protected|static|sealed|async|\\s)+string\\s+(?<name>[A-Za-z_]\\w*)\\s*\\([^)]*\\)\\s*(?:=>\\s*(?<expression>[^;]+);|\\{(?<body>.*?)\\})",
                             RegexOptions.CultureInvariant))
                {
                    var body = match.Groups["expression"].Value + match.Groups["body"].Value;
                    if (body.Contains("PhaseValidationSourceHelpers.ReadRequiredRepoText", StringComparison.Ordinal)
                        || body.Contains("PhaseValidationSourceHelpers.ReadSplitPythonSource", StringComparison.Ordinal)
                        || body.Contains("PhaseValidationSourceHelpers.ReadSplitPythonSourcePath", StringComparison.Ordinal))
                        names.Add(match.Groups["name"].Value);
                }

                if (source.Contains("string ReadRepoText", StringComparison.Ordinal)
                    && source.Contains("PhaseValidationSourceHelpers.ReadSplitPythonSourcePath", StringComparison.Ordinal))
                    names.Add("ReadRepoText");
                return names;
            }

            private static bool IsSplitAwareReader(string reader, HashSet<string> splitAwareReaders)
                => reader is "ReadRequiredRepoText" or "ReadSplitPythonSource"
                    || splitAwareReaders.Contains(reader);

            private static string ResolvePythonPath(string source, string expression)
            {
                var expressions = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match match in Regex.Matches(
                             source,
                             "(?m)(?:const\\s+string|string|var)\\s+(?<name>[A-Za-z_]\\w*)\\s*=\\s*(?<expression>[^;]+);",
                             RegexOptions.CultureInvariant))
                    expressions[match.Groups["name"].Value] = match.Groups["expression"].Value.Trim();

                foreach (Match match in Regex.Matches(
                             source,
                             "(?m)string\\s+(?<name>[A-Za-z_]\\w*)\\s*(?:\\([^)]*\\))?\\s*=>\\s*(?<expression>[^;]+);",
                             RegexOptions.CultureInvariant))
                    expressions[match.Groups["name"].Value] = match.Groups["expression"].Value.Trim();

                for (var pass = 0; pass < expressions.Count + 1; pass++)
                {
                    foreach (var name in expressions.Keys.ToArray())
                    {
                        var value = EvaluateStringExpression(expressions[name], expressions);
                        if (!string.IsNullOrEmpty(value))
                            expressions[name] = value;
                    }
                }

                var resolved = EvaluateStringExpression(expression, expressions);
                return resolved.StartsWith("Scripts/", StringComparison.Ordinal)
                       && resolved.EndsWith(".py", StringComparison.Ordinal)
                    ? resolved
                    : string.Empty;
            }

            private static string EvaluateStringExpression(
                string expression,
                IReadOnlyDictionary<string, string> values)
            {
                var text = expression.Trim();
                while (text.Length >= 2 && text[0] == '(' && text[^1] == ')')
                    text = text[1..^1].Trim();

                var parts = SplitTopLevelExpression(text, '+');
                if (parts.Count > 1)
                {
                    var evaluated = parts.Select(part => EvaluateStringExpression(part, values)).ToArray();
                    return evaluated.Any(string.IsNullOrEmpty) ? string.Empty : string.Concat(evaluated);
                }

                if (text.StartsWith("Path.Combine(", StringComparison.Ordinal)
                    && text.EndsWith(")", StringComparison.Ordinal))
                {
                    var arguments = SplitTopLevelExpression(text[13..^1], ',');
                    var evaluated = arguments.Select(part => EvaluateStringExpression(part, values)).ToArray();
                    return evaluated.Any(string.IsNullOrEmpty)
                        ? string.Empty
                        : string.Join("/", evaluated.Select(part => part.Trim('/')));
                }

                if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
                    return text[1..^1]
                        .Replace("\\\"", "\"")
                        .Replace("\\\\", "\\");
                return values.TryGetValue(text, out var value) ? value : string.Empty;
            }

            private static List<string> SplitTopLevelExpression(string expression, char separator)
            {
                var parts = new List<string>();
                var start = 0;
                var depth = 0;
                var quoted = false;
                for (var index = 0; index < expression.Length; index++)
                {
                    var character = expression[index];
                    if (character == '"' && (index == 0 || expression[index - 1] != '\\'))
                        quoted = !quoted;
                    else if (!quoted && character == '(')
                        depth++;
                    else if (!quoted && character == ')')
                        depth--;
                    else if (!quoted && depth == 0 && character == separator)
                    {
                        parts.Add(expression[start..index].Trim());
                        start = index + 1;
                    }
                }
                parts.Add(expression[start..].Trim());
                return parts;
            }

            private static bool IsGeneratedSourcePath(string root, string path)
            {
                var relative = Path.GetRelativePath(root, path);
                return relative
                    .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
            }

            [Fact]
            public void GeneratedSourcePathFilterIsPlatformIndependent()
            {
                var root = Path.Combine(Path.GetTempPath(), "phase192-reader-scan");
                Assert.True(IsGeneratedSourcePath(root, Path.Combine(root, "bin", "Generated.cs")));
                Assert.True(IsGeneratedSourcePath(root, Path.Combine(root, "obj", "Generated.cs")));
                Assert.True(IsGeneratedSourcePath(root, Path.Combine(root, "Nested", "obj", "Generated.cs")));
                Assert.False(IsGeneratedSourcePath(root, Path.Combine(root, "Runtime", "Source.cs")));
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
            public void Ros2ForUnitySubscriptionHubCompositionFallsBackToMonolithicFacade()
            {
                var root = CreateSourceFixture(
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun",
                    "FoxRunRos2SubscriptionHub.cs");
                try
                {
                    var expected = File.ReadAllText(Path.Combine(
                        root,
                        "Packages",
                        "dev.unity2foxglove.ros2forunity",
                        "Runtime",
                        "Native",
                        "FoxRun",
                        "FoxRunRos2SubscriptionHub.cs"));
                    Assert.Equal(
                        expected,
                        Ros2ForUnitySubscriptionHubSourceComposer.Compose(root));
                }
                finally
                {
                    DeleteSourceFixture(root);
                }
            }

            [Fact]
            public void Ros2ForUnitySubscriptionHubCompositionRejectsMissingFragment()
            {
                var root = CreateSourceFixture(
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun",
                    "FoxRunRos2SubscriptionHub.cs",
                    "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Scanning.cs",
                    "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Lifecycle.cs");
                try
                {
                    Assert.Throws<InvalidDataException>(
                        () => Ros2ForUnitySubscriptionHubSourceComposer.Compose(root));
                }
                finally
                {
                    DeleteSourceFixture(root);
                }
            }

            [Fact]
            public void Ros2ForUnitySubscriptionHubCompositionRejectsUnexpectedFragment()
            {
                var root = CreateSourceFixture(
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun",
                    "FoxRunRos2SubscriptionHub.cs",
                    "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Scanning.cs",
                    "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Lifecycle.cs",
                    "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Bindings.cs");
                var fragmentDirectory = Path.Combine(
                    root,
                    "Packages",
                    "dev.unity2foxglove.ros2forunity",
                    "Runtime",
                    "Native",
                    "FoxRun",
                    "Decomposed",
                    "FoxRunRos2SubscriptionHub");
                try
                {
                    File.WriteAllText(
                        Path.Combine(fragmentDirectory, "FoxRunRos2SubscriptionHub.Legacy.cs"),
                        "internal sealed partial class FoxRunRos2SubscriptionHub { }");
                    Assert.Throws<InvalidDataException>(
                        () => Ros2ForUnitySubscriptionHubSourceComposer.Compose(root));
                }
                finally
                {
                    DeleteSourceFixture(root);
                }
            }

            [Fact]
            public void Ros2ForUnitySubscriptionHubCompositionRejectsMalformedFragmentWrapper()
            {
                var root = CreateSourceFixture(
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun",
                    "FoxRunRos2SubscriptionHub.cs",
                    "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Scanning.cs",
                    "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Lifecycle.cs",
                    "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Bindings.cs");
                var fragment = Path.Combine(
                    root,
                    "Packages",
                    "dev.unity2foxglove.ros2forunity",
                    "Runtime",
                    "Native",
                    "FoxRun",
                    "Decomposed",
                    "FoxRunRos2SubscriptionHub",
                    "FoxRunRos2SubscriptionHub.Bindings.cs");
                try
                {
                    File.WriteAllText(fragment, "internal sealed partial class NotTheSubscriptionHub { }");
                    Assert.Throws<InvalidDataException>(
                        () => Ros2ForUnitySubscriptionHubSourceComposer.Compose(root));
                }
                finally
                {
                    DeleteSourceFixture(root);
                }
            }

            [Fact]
            public void Ros2ForUnitySubscriptionBindingReaderRejectsUnexpectedFragment()
            {
                var root = CreateSourceFixture(
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun",
                    "FoxRunRos2SubscriptionBinding.cs");
                var fragmentDirectory = Path.Combine(
                    root,
                    "Packages",
                    "dev.unity2foxglove.ros2forunity",
                    "Runtime",
                    "Native",
                    "FoxRun",
                    "Decomposed",
                    "FoxRunRos2SubscriptionBinding");
                try
                {
                    Directory.CreateDirectory(fragmentDirectory);
                    File.WriteAllText(
                        Path.Combine(fragmentDirectory, "Unregistered.cs"),
                        "internal sealed partial class FoxRunRos2SubscriptionBinding<T> { }");
                    Assert.Throws<InvalidDataException>(
                        () => PhaseValidationSourceHelpers.ReadFoxRunRos2SubscriptionBindingSources(root));
                }
                finally
                {
                    DeleteSourceFixture(root);
                }
            }

            [Fact]
            public void SharedRos2ForUnitySubscriptionHubHelperReadsMonolithicFacade()
            {
                var helper = TestSources.Runtime("Ros2ForUnitySubscriptionHubSourceComposer.cs");
                Assert.Contains(
                    "SourceFileNames",
                    helper,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "TrimWrapperEnd",
                    helper,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "TrimFragment",
                    helper,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "Decomposed",
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
                    "SchemaGeneratorFacadeDoesNotHideSectionMarkers",
                    "MessagePackInputDispatchEmitterCompositionPreservesDeclaredSections",
                    "RuntimePythonReadersUseTheSplitAwareSourceEntryPoint",
                    "GeneratedSourcePathFilterIsPlatformIndependent",
                    "FoxgloveRuntimeCompositionFailsClosedAndPreservesTrailingGateOrder",
                    "Ros2ForUnitySubscriptionHubCompositionPreservesSplitMethods",
                    "Ros2ForUnitySubscriptionHubCompositionFallsBackToMonolithicFacade",
                    "Ros2ForUnitySubscriptionHubCompositionRejectsMissingFragment",
                    "Ros2ForUnitySubscriptionHubCompositionRejectsUnexpectedFragment",
                    "Ros2ForUnitySubscriptionHubCompositionRejectsMalformedFragmentWrapper",
                    "Ros2ForUnitySubscriptionBindingReaderRejectsUnexpectedFragment",
                    "SharedRos2ForUnitySubscriptionHubHelperReadsMonolithicFacade",
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
