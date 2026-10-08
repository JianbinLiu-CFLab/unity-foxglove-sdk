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

    [Trait("Phase", "140-91")]
    [Trait("Domain", "Harness")]
    public sealed class SchemaEvidencePackageOptimizationTests
    {
        [Fact]
        public void Phase110CachesRepoRootAndForbiddenTokenArrays()
        {
            var source = TestSources.Runtime("Phase110Validation.cs");

            Assert.Contains("private static string _repoRoot;", source, StringComparison.Ordinal);
            Assert.Contains("_repoRoot = root;", source, StringComparison.Ordinal);
            Assert.Contains("private static readonly string[] OptionalRuntimeForbiddenTokenList", source, StringComparison.Ordinal);
            Assert.Contains("private static readonly string[] CoreProductionForbiddenTokenList", source, StringComparison.Ordinal);
            Assert.Contains("private static readonly string[] R2fuReferenceTokens", source, StringComparison.Ordinal);
            Assert.DoesNotContain("private static IEnumerable<string> OptionalRuntimeForbiddenTokens()", source, StringComparison.Ordinal);
        }

        [Fact]
        public void FixtureManifestAndReflectionLookupsAreCached()
        {
            var phase113 = TestSources.Runtime("Phase113Validation.cs");
            var phase114 = TestSources.Runtime("Phase114Validation.cs");
            var phase116 = TestSources.Runtime("Phase116Validation.cs");

            Assert.Contains("private static readonly Lazy<FoxRunCanonicalManifest> FixtureManifestCache", phase113, StringComparison.Ordinal);
            Assert.Contains("private static FoxRunCanonicalManifest FixtureManifest() => FixtureManifestCache.Value;", phase113, StringComparison.Ordinal);
            Assert.Contains("private static readonly Lazy<FoxRunCanonicalManifest> FixtureManifestCache", phase114, StringComparison.Ordinal);
            Assert.Contains("private static FoxRunCanonicalManifest FixtureManifest() => FixtureManifestCache.Value;", phase114, StringComparison.Ordinal);
            Assert.Contains("private static readonly Dictionary<string, MethodInfo> MethodCache", phase116, StringComparison.Ordinal);
            Assert.Contains("private static readonly Dictionary<string, MemberInfo> MemberCache", phase116, StringComparison.Ordinal);
            Assert.Contains("MethodCache.TryGetValue", phase116, StringComparison.Ordinal);
            Assert.Contains("MemberCache.TryGetValue", phase116, StringComparison.Ordinal);
            Assert.DoesNotContain(".GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)\r\n                .Where", phase116, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase122ReusesOpcodeCountsPerRecordArray()
        {
            var source = TestSources.Runtime("Phase122Validation.cs");

            Assert.Contains("private static Dictionary<byte, int> OpcodeCounts", source, StringComparison.Ordinal);
            Assert.Contains("counts.TryGetValue(opcode, out var count)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("=> records.Count(r => r.Opcode == opcode);", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14091MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_91Validation.cs", "--phase140-91", "Phase140_91Validation.Validate");
    }
}
