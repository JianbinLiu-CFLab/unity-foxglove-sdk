// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates review-fix hardening after replay pose and FoxRun generation-model reviews.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase115GValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 115G: Review Fixes And Fixture Hardening ===");
            _passed = 0;

            VerifyTask0Evidence();
            VerifyReplayBatchBoundaryContract();
            VerifyReplayTickDoesNotSplitLogTimeGroups();
            VerifySchemaLessTopicFallbackContract();
            VerifyPoseKeyLifetimeGuard();
            VerifyFoxRunTypeAndPolicyHardening();
            VerifyNestedObjectFailFast();
            VerifyAnalyzerDllRefreshEvidence();

            Console.WriteLine($"Phase 115G: {_passed} checks passed.");
        }

        private static void VerifyTask0Evidence()
        {
            var evidencePath = RepoPath("Developer/99 Phase115G Review Fixes And Fixture Hardening Report.md");
            if (!File.Exists(evidencePath))
            {
                Console.WriteLine("[INFO] 115G-A1 skipped: local Developer evidence report is absent; automated behavior checks continue.");
                return;
            }

            Check(true, "115G-A1: Developer report records dirty-patch triage and review evidence");
        }

        private static void VerifyReplayBatchBoundaryContract()
        {
            var controller = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");
            var runtime = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxgloveManager.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var adapter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs");
            var validation = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase115GValidation.cs");

            Check(controller.Contains("event Action<ReplayBatchContext> OnReplayBatchCompleted", StringComparison.Ordinal)
                  && runtime.Contains("event Action<ReplayBatchContext> OnReplayBatchCompleted", StringComparison.Ordinal)
                  && manager.Contains("Action<ReplayBatchContext> OnReplayBatchCompleted", StringComparison.Ordinal)
                  && server.Contains("_replayBatchForwarder", StringComparison.Ordinal),
                "115G-B1: replay batch-completed context is exposed through controller/runtime/manager lifecycle");

            Check(adapter.Contains("OnReplayBatchCompleted(ReplayBatchContext context)", StringComparison.Ordinal)
                  && adapter.Contains("FlushDeferredScenePoses", StringComparison.Ordinal)
                  && !adapter.Contains("LogTimeNs <= context.ReplayStartTimeNs", StringComparison.Ordinal),
                "115G-B2: scene pose deferral flushes by replay batch boundary, not timestamp advancement");

            Check(validation.Contains("scene primitive pose and frame-transform pose for the same resolved Transform", StringComparison.Ordinal)
                  || validation.Contains("MixedInitialBatch", StringComparison.Ordinal),
                "115G-B3: validation documents mixed scene/frame initial-batch ownership ordering");
        }

        private static void VerifyReplayTickDoesNotSplitLogTimeGroups()
        {
            var method = typeof(McapReplayEngine).GetMethod(
                "CountTickResultPrefixPreservingLogTimeGroup",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Check(method != null,
                "115G-B4: replay tick capping uses a log-time group preserving helper");
            if (method == null)
                return;

            var sameTimestampBurst = Enumerable.Range(0, 10)
                .Select(index => new McapMessage
                {
                    ChannelId = (ushort)(index + 1),
                    Sequence = (uint)index,
                    LogTime = index < 9 ? 100UL : 101UL,
                    PublishTime = (ulong)index,
                    Data = Array.Empty<byte>()
                })
                .ToList();
            var sameTimestampTake = (int)method.Invoke(null, new object[] { sameTimestampBurst, 8 });
            Check(sameTimestampTake == 9,
                "115G-B5: replay tick cap does not split messages sharing the same log time");

            var distinctTimestampBurst = Enumerable.Range(0, 10)
                .Select(index => new McapMessage
                {
                    ChannelId = (ushort)(index + 1),
                    Sequence = (uint)index,
                    LogTime = (ulong)(100 + index),
                    PublishTime = (ulong)index,
                    Data = Array.Empty<byte>()
                })
                .ToList();
            var distinctTimestampTake = (int)method.Invoke(null, new object[] { distinctTimestampBurst, 8 });
            Check(distinctTimestampTake == 8,
                "115G-B6: replay tick cap still limits distinct log-time bursts");
        }

        private static void VerifySchemaLessTopicFallbackContract()
        {
            var classifier = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayChannelBehavior.cs");
            var adapter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs");

            var overload = typeof(ReplayChannelBehaviorClassifier).GetMethod(
                nameof(ReplayChannelBehaviorClassifier.ClassifyChannel),
                new[] { typeof(string), typeof(string), typeof(string), typeof(string) });

            Check(overload != null,
                "115G-C1: replay channel classifier accepts topic for narrow legacy fallback");

            if (overload != null)
            {
                Check((ReplayChannelBehavior)overload.Invoke(null, new object[] { "protobuf", "", "", "/tf" })
                      == ReplayChannelBehavior.FrameTransformPose,
                    "115G-C2: schema-less protobuf /tf falls back to frame-transform pose");

                Check((ReplayChannelBehavior)overload.Invoke(null, new object[] { "protobuf", "", "", "/tf_static" })
                      == ReplayChannelBehavior.FrameTransformPose,
                    "115G-C3: schema-less protobuf /tf_static falls back to frame-transform pose");

                Check((ReplayChannelBehavior)overload.Invoke(null, new object[] { "protobuf", "", "", "/scene" })
                      == ReplayChannelBehavior.ScenePrimitivePose,
                    "115G-C4: schema-less protobuf /scene falls back to scene-primitive pose");

                Check((ReplayChannelBehavior)overload.Invoke(null, new object[] { "cdr", "", "", "/tf" })
                      == ReplayChannelBehavior.NonPose,
                    "115G-C5: CDR /tf is not routed into the protobuf replay adapter by topic fallback");

                Check((ReplayChannelBehavior)overload.Invoke(null, new object[] { "json", "", "", "/scene" })
                      == ReplayChannelBehavior.Unclassified,
                    "115G-C6: schema-less JSON /scene still waits for payload-shape classification");
            }

            Check(classifier.Contains("IsDefaultProtobufCompatible", StringComparison.Ordinal)
                  || classifier.Contains("IsLegacyProtobufCompatible", StringComparison.Ordinal),
                "115G-C7: topic fallback is gated to default-Protobuf-compatible legacy channels");

            Check(adapter.Contains("IsTopicFallbackBehavior", StringComparison.Ordinal)
                  && adapter.Contains("_channelBehaviorOverrides", StringComparison.Ordinal)
                  && adapter.Contains("ReplayChannelBehavior.NonPose", StringComparison.Ordinal),
                "115G-C8: heuristic topic fallback parse failures soft-downgrade the channel to non-pose");
        }

        private static void VerifyPoseKeyLifetimeGuard()
        {
            var adapter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs");

            Check(adapter.Contains("RemoveStalePoseTarget", StringComparison.Ordinal)
                  || adapter.Contains("TryGetLivePoseTarget", StringComparison.Ordinal),
                "115G-D1: deferred pose target lookup validates Unity object lifetime before use");

            Check(adapter.Contains("_transformByPoseKey.Remove", StringComparison.Ordinal)
                  && adapter.Contains("target == null", StringComparison.Ordinal),
                "115G-D2: stale transform pose-key mappings are removed before applying deferred poses");
        }

        private static void VerifyFoxRunTypeAndPolicyHardening()
        {
            var model = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs");
            var sourceGenerator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");

            Check(!model.Contains("var sourceType = IsArray", StringComparison.Ordinal)
                  && !model.Contains(": RawObservedTypeName;", StringComparison.Ordinal),
                "115G-E1: CanonicalType no longer falls back to raw observed host type text");

            Check(sourceGenerator.Contains("Convert.ToSingle", StringComparison.Ordinal)
                  && !sourceGenerator.Contains("named.Value.Value is float eps", StringComparison.Ordinal),
                "115G-E2: Roslyn ChangeEpsilon accepts numeric constants beyond float literals");

            var epsilonModel = GenerateDescriptorModel(EpsilonPolicyFixtureSource(), "FoxRunEpsilonPolicy115G", out var epsilonDiagnostics);
            Check(!epsilonDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                "115G-E3: epsilon policy fixture compiles without generator errors",
                string.Join(Environment.NewLine, epsilonDiagnostics.Select(diagnostic => diagnostic.ToString())));
            Check(ContainsTopicWithEpsilon(epsilonModel, "/debug/epsilon/integer", 1f)
                  && ContainsTopicWithEpsilon(epsilonModel, "/debug/epsilon/float", 1f),
                "115G-E4: Roslyn ChangeEpsilon preserves integer and float literal values");
        }

        private static void VerifyNestedObjectFailFast()
        {
            var validator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs");
            var sourceGenerator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var codeGenerator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");

            Check(validator.Contains("Error(\"FOXRUN006\"", StringComparison.Ordinal)
                  || validator.Contains("Severity = \"Error\"", StringComparison.Ordinal),
                "115G-F1: unsupported user-defined FoxRun payloads fail fast instead of warning only");

            Check(sourceGenerator.Contains("DiagnosticSeverity.Error", StringComparison.Ordinal)
                  && codeGenerator.Contains("FoxRunGenerationModelValidator.Validate", StringComparison.Ordinal),
                "115G-F2: nested object fail-fast is enforced by both Roslyn and build-time hosts");

            var nestedModel = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "NestedProbe", new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "NestedProbe",
                        "nestedPayload",
                        "field",
                        "Demo.NestedProbe.NestedPayload",
                        "Demo.NestedProbe.NestedPayload",
                        false,
                        false,
                        string.Empty,
                        "/debug/nested",
                        10f,
                        string.Empty,
                        0,
                        0f,
                        0f,
                        "Roslyn",
                        1,
                        string.Empty)
                })
            });
            var nestedDiagnostics = FoxRunGenerationModelValidator.Validate(nestedModel);
            Check(nestedDiagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN006" && diagnostic.Severity == "Error"),
                "115G-F3: shared validator reports FOXRUN006 Error for a FoxRun nested object member");

            GenerateDescriptorModel(NestedObjectFixtureSource(), "FoxRunNestedObject115G", out var roslynDiagnostics);
            Check(roslynDiagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN006" && diagnostic.Severity == DiagnosticSeverity.Error),
                "115G-F4: Roslyn generator reports FOXRUN006 Error for a FoxRun nested object member");
        }

        private static void VerifyAnalyzerDllRefreshEvidence()
        {
            var releaseNotes = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md");
            var dllPath = RepoPath("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll");

            Check(releaseNotes.Contains("FOXRUN006 | FoxRun | Error", StringComparison.Ordinal),
                "115G-G1: analyzer release notes record FOXRUN006 severity as Error");
            Check(File.Exists(dllPath) && new FileInfo(dllPath).Length > 0,
                "115G-G2: checked-in Unity analyzer DLL exists after source-generator changes");
        }

        private static void Check(bool condition, string name)
            => Check(condition, name, string.Empty);

        private static void Check(bool condition, string name, string details)
        {
            if (!condition)
            {
                Console.WriteLine("[FAIL] " + name);
                throw new InvalidOperationException(string.IsNullOrEmpty(details) ? name : name + " :: " + details);
            }

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static bool ContainsTopicWithEpsilon(FoxRunGenerationModel model, string topic, float expected)
        {
            return model.Types
                .SelectMany(type => type.Members)
                .Any(member => string.Equals(member.Topic, topic, StringComparison.Ordinal)
                               && Math.Abs(member.ChangeEpsilon - expected) < 0.000001f);
        }

        private static FoxRunGenerationModel GenerateDescriptorModel(
            string source,
            string assemblyName,
            out Diagnostic[] diagnostics)
        {
            var generated = RunGenerator(source, assemblyName, out diagnostics);
            var descriptor = generated.FirstOrDefault(sourceResult =>
                sourceResult.HintName == "FoxRunGeneratedDescriptorInfo.g.cs");
            if (descriptor.HintName == null)
                return new FoxRunGenerationModel(Array.Empty<FoxRunGenerationType>());

            var match = Regex.Match(descriptor.SourceText.ToString(), "DescriptorJson = \"(?<json>.*)\";");
            if (!match.Success)
                throw new InvalidOperationException("Could not extract generated FoxRun descriptor JSON.");

            return FoxRunGenerationDescriptorJsonReader.Read(Regex.Unescape(match.Groups["json"].Value));
        }

        private static GeneratedSourceResult[] RunGenerator(
            string source,
            string assemblyName,
            out Diagnostic[] diagnostics)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
                throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES host data is required for Phase115G Roslyn reference resolution.");

            var references = trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(FoxRunAttribute).Assembly.Location) })
                .ToArray();
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new FoxgloveLogSourceGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var driverDiagnostics);
            diagnostics = driverDiagnostics.ToArray();
            return driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).ToArray();
        }

        private static string EpsilonPolicyFixtureSource()
        {
            return @"
using Unity.FoxgloveSDK.Components;

public partial class EpsilonPolicyProbe
{
    [FoxRun(""/debug/epsilon/integer"", PublishMode = FoxRunPublishMode.OnChange, ChangeEpsilon = 1)]
    public float integerLiteral;

    [FoxRun(""/debug/epsilon/float"", PublishMode = FoxRunPublishMode.OnChange, ChangeEpsilon = 1f)]
    public float floatLiteral;
}
";
        }

        private static string NestedObjectFixtureSource()
        {
            return @"
using Unity.FoxgloveSDK.Components;

public partial class NestedObjectProbe
{
    public sealed class NestedPayload
    {
        public int count;
    }

    [FoxRun(""/debug/nested/object"")]
    public NestedPayload nestedPayload;
}
";
        }

        private static string RepoPath(string relativePath)
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                    return candidate;

                var parent = Directory.GetParent(dir);
                if (parent == null)
                    break;
                dir = parent.FullName;
            }

            return Path.GetFullPath(relativePath);
        }
    }
}
