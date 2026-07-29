// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Anti-vacuity, sample, package, analyzer, and batch-artifact release gate.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxRunMessagePackArtifactAcceptanceValidation
    {
        private static readonly IReadOnlyDictionary<string, (string Name, ValidationEvidence Evidence)> ExpectedSelectors =
            new Dictionary<string, (string, ValidationEvidence)>(StringComparer.Ordinal)
            {
                ["--phase185a"] = (
                    "FoxRun MessagePack public contract and source shape",
                    ValidationEvidence.Structural),
                ["--phase185b"] = (
                    "FoxRun MessagePack generated publish and fanout integration",
                    ValidationEvidence.Structural),
                ["--phase185c"] = (
                    "FoxRun MessagePack generated bounded input integration",
                    ValidationEvidence.Structural),
                ["--phase185d"] = (
                    "FoxRun MessagePack duplex tooling and MCAP compatibility",
                    ValidationEvidence.Structural | ValidationEvidence.Conformance),
                ["--phase185e"] = (
                    "FoxRun MessagePack artifact sample and package acceptance",
                    ValidationEvidence.Structural)
            };

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun MessagePack artifact acceptance validation ---");
            _passed = 0;

            VerifyCompiledTypesAndDefaultRegistry();
            VerifyExactProjectInclusionsAndMetas();
            VerifyAnalyzerFreshnessContract();
            VerifyGeneratedArtifacts();
            VerifyFullDemoParity();
            VerifyEditorBatchCommand();

            Console.WriteLine("FoxRun MessagePack artifact acceptance: " + _passed + " checks passed.\n");
        }

        private static void VerifyCompiledTypesAndDefaultRegistry()
        {
            var types = new[]
            {
                typeof(FoxRunMessagePackPublicContractValidation),
                typeof(FoxRunMessagePackPublishFanoutValidation),
                typeof(FoxRunMessagePackBoundedInputValidation),
                typeof(FoxRunMessagePackDuplexToolingValidation),
                typeof(FoxRunMessagePackArtifactAcceptanceValidation)
            };
            var expectedTypeNames = new HashSet<string>(
                new[]
                {
                    "FoxRunMessagePackPublicContractValidation",
                    "FoxRunMessagePackPublishFanoutValidation",
                    "FoxRunMessagePackBoundedInputValidation",
                    "FoxRunMessagePackDuplexToolingValidation",
                    "FoxRunMessagePackArtifactAcceptanceValidation"
                },
                StringComparer.Ordinal);
            Check(
                expectedTypeNames.SetEquals(types.Select(type => type.Name)),
                "185E-1: all five descriptively named validation types are compiled into this assembly");

            var registered = PhaseValidationRegistry.All
                .Where(item => item.Flag != null && ExpectedSelectors.ContainsKey(item.Flag))
                .ToArray();
            Check(
                registered.Length == ExpectedSelectors.Count
                && registered.All(item => item.IncludeInDefault)
                && registered.All(item =>
                {
                    var expected = ExpectedSelectors[item.Flag];
                    return string.Equals(item.Name, expected.Name, StringComparison.Ordinal)
                           && item.Evidence == expected.Evidence;
                }),
                "185E-2: all five selectors use exact labels, evidence classes, and default inclusion");
        }

        private static void VerifyExactProjectInclusionsAndMetas()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            foreach (var typeName in ExpectedTypeNames())
            {
                var fileName = typeName + ".cs";
                var exactInclude = "<Compile Include=\"" + fileName + "\" />";
                Check(
                    Count(project, exactInclude) == 1,
                    "185E-project: exactly one compile include owns " + fileName);
                Check(
                    Exists("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + fileName + ".meta"),
                    "185E-meta: Unity meta exists for " + fileName);
            }
        }

        private static void VerifyAnalyzerFreshnessContract()
        {
            var validator = Read("Scripts/package/validate_source_generator_dll.py");
            Check(
                ContainsAll(
                    validator,
                    "FoxgloveLogSourceGenerator.dll",
                    "built_hash = sha256",
                    "checked_hash = sha256",
                    "Checked-in source generator artifact is stale"),
                "185E-3: repository analyzer freshness tool performs a fresh Release hash comparison");
            Check(
                Exists("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll")
                && Exists("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll.meta"),
                "185E-4: checked-in analyzer and Unity meta are present");
        }

        private static void VerifyGeneratedArtifacts()
        {
            var generatedSource = Read("Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs");
            var descriptor = Read("Unity2Foxglove/Assets/Generated/FoxRun/foxrun.generation-descriptor.json");
            var manifest = Read("Unity2Foxglove/Assets/Generated/FoxRun/foxrun.manifest.json");
            Check(
                generatedSource.Contains("/phase185/messagepack/full-duplex", StringComparison.Ordinal)
                && generatedSource.Contains("__BuildFoxRunMessagePack_", StringComparison.Ordinal)
                && generatedSource.Contains("FoxgloveInput_TryStageTransaction", StringComparison.Ordinal)
                && generatedSource.Contains("__FoxRunFlushMessagePackTransactions", StringComparison.Ordinal)
                && Exists("Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs.meta"),
                "185E-5: controlled generated source/meta contains duplex MessagePack output and input");
            Check(
                descriptor.Contains("\"descriptorVersion\":6", StringComparison.Ordinal)
                && descriptor.Contains("\"generatorVersion\":\"6.0.0\"", StringComparison.Ordinal)
                && descriptor.Contains("\"encoding\":\"msgpack\"", StringComparison.Ordinal)
                && manifest.Contains("\"msgpack\"", StringComparison.Ordinal),
                "185E-6: controlled descriptor/manifest evidence records current typed MessagePack");
        }

        private static void VerifyFullDemoParity()
        {
            const string liveSource = "Unity2Foxglove/Assets/Scripts/FullDemoVisualization/TestLog.MessagePack.cs";
            const string liveMeta = liveSource + ".meta";
            const string packageSource =
                "Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scripts/TestLog.MessagePack.cs";
            const string packageMeta = packageSource + ".meta";
            Check(
                FileBytesEqual(liveSource, packageSource)
                && FileBytesEqual(liveMeta, packageMeta),
                "185E-7: canonical live and packaged Full Demo MessagePack source/meta pairs are byte-identical");

            var evidenceRoot = Environment.GetEnvironmentVariable("PHASE185_EVIDENCE_ROOT");
            if (string.IsNullOrWhiteSpace(evidenceRoot))
            {
                Check(
                    Read("Scripts/samples/sync_full_demo.py").Contains(
                        "TestLog.MessagePack.cs",
                        StringComparison.Ordinal),
                    "185E-8: imported parity is owned by the maintained Full Demo sync map");
                return;
            }

            const string importedRoot =
                "build/phase185/imported-full-demo/Assets/Samples/Unity2Foxglove SDK/1.9.6/Full Demo Visualization/Scripts";
            Check(
                FileBytesEqual(packageSource, importedRoot + "/TestLog.MessagePack.cs")
                && FileBytesEqual(packageMeta, importedRoot + "/TestLog.MessagePack.cs.meta"),
                "185E-8: packaged and scratch-imported Full Demo MessagePack source/meta pairs are byte-identical");
        }

        private static void VerifyEditorBatchCommand()
        {
            const string sourcePath =
                "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunMessagePackArtifactBatchCommand.cs";
            var source = Read(sourcePath);
            var asmdef = Read("Packages/dev.unity2foxglove.sdk/Editor/Unity.FoxgloveSDK.Editor.asmdef");
            Check(
                ContainsAll(
                    source,
                    "GenerateControlledArtifacts",
                    "FoxrunCodeGenerator.GenerateSourceFiles",
                    "PHASE185_EVIDENCE_ROOT",
                    "PHASE185_BATCH_GENERATOR_PASS")
                && asmdef.Contains("\"name\": \"Unity.FoxgloveSDK.Editor\"", StringComparison.Ordinal)
                && Exists(sourcePath + ".meta"),
                "185E-9: real Editor assembly owns the no-argument maintained-generator batch command and meta");

            var evidenceRoot = Environment.GetEnvironmentVariable("PHASE185_EVIDENCE_ROOT");
            if (string.IsNullOrWhiteSpace(evidenceRoot))
                return;

            var evidencePath = Path.Combine(evidenceRoot, "phase185-generator-evidence.json");
            if (!File.Exists(evidencePath))
                throw new InvalidOperationException("[FAIL] 185E-10: bounded Editor generator evidence is missing.");
            var evidence = File.ReadAllText(evidencePath);
            var logs = Directory.GetFiles(evidenceRoot, "unity-generate-*.log");
            Check(
                evidence.Length <= 256 * 1024
                && evidence.Contains("\"verdict\": \"PASS\"", StringComparison.Ordinal)
                && logs.Any(path => File.ReadAllText(path).Contains(
                    "PHASE185_BATCH_GENERATOR_PASS",
                    StringComparison.Ordinal)),
                "185E-10: exited Unity batch run left bounded PASS evidence and the terminal log marker");
        }

        private static IEnumerable<string> ExpectedTypeNames()
            => new[]
            {
                "FoxRunMessagePackPublicContractValidation",
                "FoxRunMessagePackPublishFanoutValidation",
                "FoxRunMessagePackBoundedInputValidation",
                "FoxRunMessagePackDuplexToolingValidation",
                "FoxRunMessagePackArtifactAcceptanceValidation"
            };

        private static int Count(string source, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }
            return count;
        }

        private static bool FileBytesEqual(string left, string right)
        {
            var leftPath = Path.Combine(Root(), left.Replace('/', Path.DirectorySeparatorChar));
            var rightPath = Path.Combine(Root(), right.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(leftPath)
                   && File.Exists(rightPath)
                   && File.ReadAllBytes(leftPath).SequenceEqual(File.ReadAllBytes(rightPath));
        }

        private static string Root() => FoxRunMessagePackPublicContractValidation.Root();
        private static string Read(string path) => FoxRunMessagePackPublicContractValidation.Read(path);
        private static bool Exists(string path) => FoxRunMessagePackPublicContractValidation.Exists(path);
        private static bool ContainsAll(string source, params string[] values)
            => FoxRunMessagePackPublicContractValidation.ContainsAll(source, values);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
