// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-89/90/91/92/94 runtime validation optimization checks.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{

    [Trait("Phase", "140-94")]
    [Trait("Domain", "Harness")]
    public sealed class VirtualSensorThroughputOptimizationTests
    {
        [Fact]
        public void CameraPublisherReadersUseStringBuilderAndSourceCaches()
        {
            foreach (var file in new[] { "Phase138MValidation.cs", "Phase138PValidation.cs", "Phase138QValidation.cs", "Phase138TValidation.cs" })
            {
                var method = TestSources.ExtractMethod(TestSources.Runtime(file), "private static string ReadCameraPublisherSources()");
                Assert.Contains("StringBuilder", method, StringComparison.Ordinal);
                Assert.Contains("AppendLine(File.ReadAllText(file))", method, StringComparison.Ordinal);
                Assert.DoesNotContain("output += File.ReadAllText(file)", method, StringComparison.Ordinal);
            }

            foreach (var file in new[] { "Phase138HValidation.cs", "Phase138IValidation.cs", "Phase138JValidation.cs", "Phase138MValidation.cs", "Phase138QValidation.cs" })
            {
                var source = TestSources.Runtime(file);
                Assert.Contains("private static readonly Dictionary<string, string>", source, StringComparison.Ordinal);
                Assert.Contains("TryGetValue", source, StringComparison.Ordinal);
                Assert.Contains("File.ReadAllText", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void VirtualSensorValidationHotPathsAvoidExtraReadsAndAllocations()
        {
            var phase138P = TestSources.ExtractMethod(TestSources.Runtime("Phase138PValidation.cs"), "private static SourceHygieneFile ReadSourceHygieneFile(");
            var phase138PSource = TestSources.Runtime("Phase138PValidation.cs");
            var phase138S = TestSources.ExtractMethod(TestSources.Runtime("Phase138SValidation.cs"), "private static string ReadDirectory(");
            var phase138L = TestSources.ExtractMethod(TestSources.Runtime("Phase138LValidation.cs"), "private static bool HasField(");
            var phase138C2 = TestSources.Runtime("Phase138C2Validation.cs");

            Assert.Contains("FileStream", phase138P, StringComparison.Ordinal);
            Assert.Contains("Span<byte>", phase138P, StringComparison.Ordinal);
            Assert.Contains("stream.Read(header)", phase138P, StringComparison.Ordinal);
            Assert.DoesNotContain("File.ReadAllBytes(path)", phase138PSource, StringComparison.Ordinal);
            Assert.Contains("ScanTrackedSourceHygiene", phase138PSource, StringComparison.Ordinal);
            Assert.DoesNotContain("private static bool TrackedSourceHasUtf8Bom()", phase138PSource, StringComparison.Ordinal);
            Assert.Contains("var content = File.ReadAllText(file);", phase138S, StringComparison.Ordinal);
            Assert.Contains("sb.AppendLine(content)", phase138S, StringComparison.Ordinal);
            Assert.Contains("bytes += content.Length", phase138S, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(phase138S, "File.ReadAllText(file)"));
            Assert.Contains("for (var i = 0; i < fields.Length; i++)", phase138L, StringComparison.Ordinal);
            Assert.DoesNotContain(".Any(", phase138L, StringComparison.Ordinal);
            Assert.Contains("private static readonly byte[] OkPayload", phase138C2, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(phase138C2, "Encoding.UTF8.GetBytes(\"{\\\"ok\\\":true}\")"));
            Assert.Contains("session.Publish(77, OkPayload, 1)", phase138C2, StringComparison.Ordinal);
            Assert.Contains("session.Publish(78, OkPayload, 2)", phase138C2, StringComparison.Ordinal);
            Assert.Contains("session.Publish(79, OkPayload, 3)", phase138C2, StringComparison.Ordinal);
        }

        [Fact]
        public void RayUnitChecksUseSquaredMagnitude()
        {
            foreach (var file in new[] { "Phase138Validation.cs", "Phase138BValidation.cs" })
            {
                var source = TestSources.Runtime(file);
                Assert.Contains("LengthSquared()", source, StringComparison.Ordinal);
                Assert.DoesNotContain("var mag = dir.Length();", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Phase14094MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_94Validation.cs", "--phase140-94", "Phase140_94Validation.Validate");

        [Fact]
        public void SharedMethodExtractorIgnoresBracesInsideLiteralsAndComments()
        {
            const string source = @"
private static void Probe()
{
    var json = ""}"";
    var character = '}';
    // }
    /* } */
    KeepThisStatement();
}

private static void Next() { }
";

            var method = TestSources.ExtractMethod(source, "private static void Probe()");

            Assert.Contains("KeepThisStatement();", method, StringComparison.Ordinal);
            Assert.DoesNotContain("private static void Next()", method, StringComparison.Ordinal);
        }
    }
}
