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

    [Trait("Phase", "140-90")]
    [Trait("Domain", "Harness")]
    public sealed class OptionalRos2ValidationOptimizationTests
    {
        [Fact]
        public void Phase115RoslynValidationsCacheMetadataReferences()
        {
            var phase115E = TestSources.Runtime("Phase115EValidation.cs");
            var phase115F = TestSources.Runtime("Phase115FValidation.cs");
            var phase115G = TestSources.Runtime("Phase115GValidation.cs");

            Assert.Contains("private static readonly Lazy<MetadataReference[]> CachedReferences", phase115E, StringComparison.Ordinal);
            Assert.Contains("private static MetadataReference[] References() => CachedReferences.Value;", phase115E, StringComparison.Ordinal);
            Assert.Contains("private static readonly Lazy<MetadataReference[]> CachedReferences", phase115F, StringComparison.Ordinal);
            Assert.Contains("private static MetadataReference[] References() => CachedReferences.Value;", phase115F, StringComparison.Ordinal);
            Assert.Contains("private static readonly Lazy<MetadataReference[]> CachedReferences", phase115G, StringComparison.Ordinal);
            Assert.Contains("CachedReferences.Value", phase115G, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase105ReusesNormalizedSummaryLines()
        {
            var source = TestSources.Runtime("Phase105Validation.cs");

            Assert.Contains("private static readonly Dictionary<string, string[]> SummaryLineCache", source, StringComparison.Ordinal);
            Assert.Contains("SummaryLineCache.Clear();", source, StringComparison.Ordinal);
            Assert.Contains("private static string[] ReadRepoLines", source, StringComparison.Ordinal);
            Assert.Contains("WindowBefore(lines, declaration", source, StringComparison.Ordinal);
        }

        [Fact]
        public void OptionalEditorAndRuntimeScansAvoidRepeatedEnumeration()
        {
            var phase107 = TestSources.Runtime("Phase107Validation.cs");
            var phase108 = TestSources.Runtime("Phase108Validation.cs");

            Assert.Equal(1, TestSources.Count(phase107, "Directory.GetFiles(editorRoot, \"*.*\", SearchOption.AllDirectories)"));
            Assert.Contains("var editorFiles = Directory.GetFiles(editorRoot", phase107, StringComparison.Ordinal);
            Assert.Contains("foreach (var path in editorFiles.Where(HasTextExtension))", phase107, StringComparison.Ordinal);
            Assert.Contains("private static IReadOnlyList<string> _runtimeTextFiles", phase108, StringComparison.Ordinal);
            Assert.Contains("_runtimeTextFiles = null;", phase108, StringComparison.Ordinal);
            Assert.Contains("private static IReadOnlyList<string> RuntimeTextFiles()", phase108, StringComparison.Ordinal);
            Assert.Contains("return _runtimeTextFiles;", phase108, StringComparison.Ordinal);
        }

        [Fact]
        public void HashSidecarsAreDecodedFromSingleByteRead()
        {
            var phase112 = TestSources.Runtime("Phase112Validation.cs");
            var phase115 = TestSources.Runtime("Phase115Validation.cs");

            Assert.DoesNotContain("File.ReadAllText(hashPath)", phase112, StringComparison.Ordinal);
            Assert.Contains("Encoding.ASCII.GetString(hashBytes)", phase112, StringComparison.Ordinal);
            Assert.DoesNotContain("File.ReadAllText(hashPath)", phase115, StringComparison.Ordinal);
            Assert.Contains("Encoding.ASCII.GetString(hashBytes)", phase115, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14090MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_90Validation.cs", "--phase140-90", "Phase140_90Validation.Validate");
    }
}
