// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-68/69/70/71/72/73 generation and editor optimization checks.

using System;
using System.IO;
using System.Text.Json;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-68")]
    [Trait("Domain", "Harness")]
    public sealed class FoxRunSharedEmitterOptimizationTests
    {
        [Fact]
        public void SharedEmitterHotPathsAvoidRedundantWork()
        {
            var validator = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs");
            var model = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs");
            var formatter = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunEmissionTypeNameFormatter.cs");
            var comparer = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorComparer.cs");
            var reconciler = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGeneratedSourceReconciler.cs");
            var writer = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorJsonWriter.cs");

            Assert.Contains("private static readonly string[] UnityNativeContainerPrefixes", validator, StringComparison.Ordinal);
            Assert.DoesNotContain("new[]", TestSources.ExtractMethod(validator, "private static bool IsUnityNativeContainerTypeName"), StringComparison.Ordinal);
            Assert.Contains("? ElementTypeName", TestSources.ExtractMethod(model, "private string SelectCanonicalSourceType()"), StringComparison.Ordinal);
            Assert.DoesNotContain("NormalizeCSharpTypeName(ElementTypeName)", TestSources.ExtractMethod(model, "private string SelectCanonicalSourceType()"), StringComparison.Ordinal);
            Assert.Contains("IndexOf(\"global::\", StringComparison.Ordinal)", formatter, StringComparison.Ordinal);
            Assert.Contains("IndexOf('+')", formatter, StringComparison.Ordinal);
            Assert.Contains("CompareSortedMemberKeys", comparer, StringComparison.Ordinal);
            Assert.DoesNotContain(".Except(", comparer, StringComparison.Ordinal);
            Assert.DoesNotContain(".Intersect(", comparer, StringComparison.Ordinal);
            Assert.Contains("AsReadOnly()", comparer, StringComparison.Ordinal);
            Assert.Contains("copyInputs: false", comparer, StringComparison.Ordinal);
            Assert.Contains("StreamReader", reconciler, StringComparison.Ordinal);
            Assert.Contains("ReadLine()", reconciler, StringComparison.Ordinal);
            Assert.DoesNotContain("File.ReadAllText(path)", reconciler, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Linq", reconciler, StringComparison.Ordinal);
            Assert.Contains("new StringBuilder(EstimateCapacity(model))", writer, StringComparison.Ordinal);
        }

        [Fact]
        public void SharedEmitterOutputSemanticsArePreserved()
        {
            var model = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType(
                    "Demo",
                    "Car",
                    new[]
                    {
                        new FoxRunGenerationMember(
                            "Demo",
                            "Car",
                            "speed",
                            "field",
                            "System.Single",
                            "float",
                            "float",
                            true,
                            false,
                            "",
                            "/vehicle/speed",
                            30f,
                            "unity2foxglove.Float32",
                            3,
                            0.001f,
                            "reflection",
                            7,
                            "UNITY_EDITOR"),
                        new FoxRunGenerationMember(
                            "Demo",
                            "Car",
                            "samples",
                            "property",
                            "System.Single[]",
                            "float[]",
                            "",
                            false,
                            true,
                            "global::System.Single",
                            "/vehicle/samples",
                            0f,
                            "unity2foxglove.Float32",
                            3,
                            0f,
                            "roslyn",
                            8,
                            "")
                    })
            });

            var json = FoxRunGenerationDescriptorJsonWriter.Write(model);
            var comparison = FoxRunGenerationDescriptorComparer.Compare(model, model);

            Assert.Contains("\"descriptorVersion\":6", json, StringComparison.Ordinal);
            Assert.Contains("\"elementTypeName\":\"float\"", json, StringComparison.Ordinal);
            Assert.Contains("\"topic\":\"/vehicle/samples\"", json, StringComparison.Ordinal);
            Assert.True(comparison.IsSemanticEqual);
            Assert.True(comparison.IsProvenanceEqual);
        }

        [Fact]
        public void DescriptorWriterRejectsLoneSurrogatesAndEscapesPairs()
        {
            var lone = CreateDescriptorModel("\uD800");
            Assert.Throws<InvalidOperationException>(() => FoxRunGenerationDescriptorJsonWriter.Write(lone));

            var paired = CreateDescriptorModel("face\U0001F600");
            var json = FoxRunGenerationDescriptorJsonWriter.Write(paired);

            Assert.Contains("\\ud83d\\ude00", json, StringComparison.Ordinal);
            using var _ = JsonDocument.Parse(json);
        }

        [Fact]
        [Trait("Phase", "187")]
        public void DescriptorWriterReportsTheCurrentVersionForMissingSchedules()
        {
            var model = CreateDescriptorModel("value");
            var member = model.Types[0].Members[0];
            var schedule = typeof(FoxRunGenerationMember).GetField(
                nameof(FoxRunGenerationMember.NormalizedSchedule));
            Assert.NotNull(schedule);
            schedule.SetValue(member, null);

            var exception = Assert.Throws<InvalidOperationException>(
                () => FoxRunGenerationDescriptorJsonWriter.Write(model));

            Assert.Contains(
                "descriptor v" + FoxRunGenerationDescriptorConstants.DescriptorVersion,
                exception.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedSourceOwnershipStillUsesHeaderSentinels()
        {
            var temp = Path.Combine(Path.GetTempPath(), "u2f_phase140_68_" + Guid.NewGuid().ToString("N") + ".g.cs");
            try
            {
                File.WriteAllText(
                    temp,
                    "// <auto-generated/>\n// " + FoxRunGeneratedSourceReconciler.GeneratedSourceSentinel + "\npublic class A {}\n");
                Assert.True(FoxRunGeneratedSourceReconciler.IsOwnedGeneratedSourceFile(temp));
                File.WriteAllText(temp, "public class B {}\n");
                Assert.False(FoxRunGeneratedSourceReconciler.IsOwnedGeneratedSourceFile(temp));
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        [Fact]
        public void Phase14068MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_68Validation.cs", "--phase140-68", "Phase140_68Validation.Validate");

        private static FoxRunGenerationModel CreateDescriptorModel(string jsonFieldName)
            => new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType(
                    "Demo",
                    "Text",
                    new[]
                    {
                        new FoxRunGenerationMember(
                            "Demo",
                            "Text",
                            "value",
                            "field",
                            "System.String",
                            "string",
                            "string",
                            false,
                            false,
                            "",
                            "/demo/text",
                            0f,
                            "unity2foxglove.String",
                            1,
                            0f,
                            "reflection",
                            1,
                            "",
                            jsonFieldName: jsonFieldName)
                    })
            });

        [Fact]
        public void TestSourceSlicesNormalizeLineEndings()
        {
            var source = "alpha\r\nbeta\r\ngamma";

            var slice = TestSources.Slice(source, "alpha\n", "\ngamma");

            Assert.Equal("alpha\nbeta", slice);
        }

        [Fact]
        [Trait("Phase", "187-R4-C5")]
        public void TestSourceSlicesRejectMissingEndMarkers()
        {
            var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
                () => TestSources.Slice("alpha\nbeta", "alpha", "missing"));

            Assert.Contains(
                "Could not locate source slice end: missing",
                error.Message,
                StringComparison.Ordinal);
        }
    }





}
