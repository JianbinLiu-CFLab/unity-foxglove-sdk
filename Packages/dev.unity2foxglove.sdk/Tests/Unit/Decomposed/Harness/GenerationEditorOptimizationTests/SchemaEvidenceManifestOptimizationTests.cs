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

    [Trait("Phase", "140-71")]
    [Trait("Domain", "Harness")]
    public sealed class SchemaEvidenceManifestOptimizationTests
    {
        [Fact]
        public void SchemaEvidencePathsAndSettingsCacheProjectState()
        {
            var paths = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");
            var projectRoot = TestSources.Slice(paths, "private static string ProjectRoot", "        private static string ResolveProjectRoot()");
            var settings = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidenceSettings.cs");
            var drawSettings = TestSources.Slice(settings, "private static void DrawSettings()", "        private static void SaveAndSync()");

            Assert.Contains("private static readonly Lazy<string> CachedProjectRoot = new Lazy<string>(ResolveProjectRoot);", paths, StringComparison.Ordinal);
            Assert.Contains("private static string ProjectRoot => CachedProjectRoot.Value;", paths, StringComparison.Ordinal);
            Assert.DoesNotContain("Application.dataPath", projectRoot, StringComparison.Ordinal);
            Assert.DoesNotContain("Directory.GetParent", projectRoot, StringComparison.Ordinal);
            Assert.Contains("var resolvedRoot = ResolveCurrentEvidenceRootCached();", drawSettings, StringComparison.Ordinal);
            Assert.Contains("TryNormalizeAssetsRootCached(root", drawSettings, StringComparison.Ordinal);
            Assert.Contains("private static string s_resolvedRootCacheKey;", settings, StringComparison.Ordinal);
            Assert.Contains("private static bool TryNormalizeAssetsRootCached", settings, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(settings, "Unity2FoxgloveSchemaEvidencePaths.ResolveCurrentEvidenceRoot()"));
            Assert.Contains("Directory.CreateDirectory(resolvedRoot)", drawSettings, StringComparison.Ordinal);
            Assert.Contains("EditorUtility.RevealInFinder(resolvedRoot)", drawSettings, StringComparison.Ordinal);
        }

        [Fact]
        public void SchemaManifestBuilderAvoidsRepeatedSortAndAllocation()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestBuilder.cs");
            var buildFoxRun = TestSources.Slice(source, "private static Unity2FoxgloveFoxRunSummarySection BuildFoxRunSection", "        private static Unity2FoxgloveProtobufRegistrySection BuildProtobufRegistrySection()");
            var buildSdk = TestSources.Slice(source, "private static Unity2FoxgloveSdkTypedPublishersSection BuildSdkTypedPublishersSection()", "        private static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> GetSortedSdkTypedPublisherEntries()");
            var getSorted = TestSources.Slice(source, "private static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> GetSortedSdkTypedPublisherEntries()", "        private static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> BuildSortedSdkTypedPublisherEntries()");
            var buildSorted = TestSources.Slice(source, "private static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> BuildSortedSdkTypedPublisherEntries()", "        private static void ValidatePublisherCatalog");

            Assert.Contains("foreach (var type in types)", buildFoxRun, StringComparison.Ordinal);
            Assert.Contains("contracts += type.Contracts.Count;", buildFoxRun, StringComparison.Ordinal);
            Assert.Contains("fields += contract.Fields.Count;", buildFoxRun, StringComparison.Ordinal);
            Assert.DoesNotContain(".Sum(", buildFoxRun, StringComparison.Ordinal);
            Assert.DoesNotContain("private static string Sha256Hex(byte[] bytes)", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunManifestHasher.Sha256Hex", source, StringComparison.Ordinal);
            Assert.Contains("private static readonly Lazy<IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry>> SortedSdkTypedPublisherEntries", source, StringComparison.Ordinal);
            Assert.Contains("var entries = GetSortedSdkTypedPublisherEntries();", buildSdk, StringComparison.Ordinal);
            Assert.Contains("entries.Count", buildSdk, StringComparison.Ordinal);
            Assert.DoesNotContain("OrderBy", buildSdk, StringComparison.Ordinal);
            Assert.Contains("return SortedSdkTypedPublisherEntries.Value;", getSorted, StringComparison.Ordinal);
            Assert.Contains("ValidatePublisherCatalog(entries)", buildSorted, StringComparison.Ordinal);

            var hasher = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestHasher.cs");
            Assert.Contains("using var sha = SHA256.Create();", hasher, StringComparison.Ordinal);
            Assert.DoesNotContain("ThreadLocal<SHA256>", hasher, StringComparison.Ordinal);

            var memberData = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunMemberData.cs");
            Assert.Contains("LooksLikeArrayType(rawType)", memberData, StringComparison.Ordinal);
            Assert.Contains("use the Type-based constructor", memberData, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14071MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_71Validation.cs", "--phase140-71", "Phase140_71Validation.Validate");
    }
}
