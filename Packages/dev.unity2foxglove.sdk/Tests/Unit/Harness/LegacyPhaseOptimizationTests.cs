// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-21/22/23/24/27 legacy phase optimization checks.

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-21")]
    [Trait("Domain", "Harness")]
    public sealed class FoxRunGenerationParityTests
    {
        [Fact]
        public void SharedValidatorReportsHostIndependentDiagnostics()
        {
            var conflicts = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "Conflicts", new[]
                {
                    Member("_value", "/demo/conflict", "schema.A", policy: 0),
                    Member("__value", "/demo/conflict", "schema.B", policy: 1)
                })
            });

            var conflictDiagnostics = FoxRunGenerationModelValidator.Validate(conflicts);
            Assert.Contains(conflictDiagnostics, d => d.Id == "FOXRUN002" && d.Severity == "Error");
            Assert.Contains(conflictDiagnostics, d => d.Id == "FOXRUN003");
            Assert.Contains(conflictDiagnostics, d => d.Id == "FOXRUN005");

            var policy = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "Policy", new[]
                {
                    Member("_nanHz", "/demo/nan", hz: float.NaN),
                    Member("_infTolerance", "/demo/inf_tolerance", tolerance: float.PositiveInfinity)
                })
            });

            var policyDiagnostics = FoxRunGenerationModelValidator.Validate(policy);
            Assert.Contains(policyDiagnostics, d => d.Id == "FOXRUN009" && d.MemberName == "_nanHz");
            Assert.Contains(policyDiagnostics, d => d.Id == "FOXRUN009" && d.MemberName == "_infTolerance");
        }

        [Fact]
        public void FoxRunGenerationSourcesKeepParityGuards()
        {
            var codegen = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var memberData = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunMemberData.cs");
            var scanner = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunAssemblyScanner.cs");
            var generator = TestSources.SourceGeneratorSources();
            var build = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            var hook = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestPlayModeHook.cs");
            var methodComment = TestSources.Slice(codegen, "/// Refresh canonical FoxRun manifest artifacts", "public static FoxRunCanonicalManifest GenerateManifestFilesOnly()");

            Assert.Contains("typeof(IList<>)", memberData, StringComparison.Ordinal);
            Assert.Contains("case '\\t':", generator, StringComparison.Ordinal);
            Assert.Contains("case '\\b':", generator, StringComparison.Ordinal);
            Assert.Contains("ToString(\"x4\", CultureInfo.InvariantCulture)", generator, StringComparison.Ordinal);
            Assert.Contains("var emitted =", generator, StringComparison.Ordinal);
            Assert.Contains("emitted,", generator, StringComparison.Ordinal);
            Assert.Contains("WriteSourceFileIfChanged", codegen, StringComparison.Ordinal);
            Assert.DoesNotContain("File.WriteAllBytes(absolutePath, sourceBytes)", codegen, StringComparison.Ordinal);
            Assert.Contains("WriteTextIfChanged", build, StringComparison.Ordinal);
            Assert.DoesNotContain("File.WriteAllText(linkPath, linkXml)", build, StringComparison.Ordinal);
            Assert.Contains("schema info", methodComment, StringComparison.Ordinal);
            Assert.Contains("generation descriptor", methodComment, StringComparison.Ordinal);
            Assert.Contains("artifact set", hook, StringComparison.Ordinal);
            Assert.Contains("next Play attempt", hook, StringComparison.Ordinal);
            Assert.Contains("ReflectionTypeLoadException ex", scanner, StringComparison.Ordinal);
            Assert.Contains("Debug.LogWarning", scanner, StringComparison.Ordinal);
            Assert.Contains("LoaderExceptions", scanner, StringComparison.Ordinal);
            Assert.DoesNotContain("const string OutputDir", codegen, StringComparison.Ordinal);
            Assert.Contains("diagnostic.MemberName", generator, StringComparison.Ordinal);
            Assert.Contains("TryGetValue", generator, StringComparison.Ordinal);
            Assert.DoesNotContain("foreach (var pair in memberLocations)", generator, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14021MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_21Validation.cs", "--phase140-21", "Phase140_21Validation.Validate");

        private static FoxRunGenerationMember Member(
            string name,
            string topic,
            string schemaName = "",
            float hz = 10f,
            int policy = 0,
            float tolerance = 0f)
        {
            return new FoxRunGenerationMember(
                "Demo",
                "Probe",
                name,
                "field",
                "System.Single",
                "float",
                true,
                false,
                string.Empty,
                topic,
                hz,
                schemaName,
                policy,
                tolerance,
                "Test",
                0,
                string.Empty);
        }
    }




}
