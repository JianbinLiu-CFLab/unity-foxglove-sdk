// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-030")]
    [Trait("Domain", "Publishing")]
    public sealed class PublisherBaseReviewTests
    {
        [Fact]
        public void PublisherBaseUsesUnitySafeManagerAccessAndReResolution()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var generic = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisher.cs");
            var currentLogTime = SourceProperty(source, "protected ulong CurrentLogTimeNs");
            var ensureManager = SourceMethod(source, "protected bool EnsureManagerAvailable");

            Assert.Contains("if (_manager == null)", currentLogTime, StringComparison.Ordinal);
            Assert.DoesNotContain("_manager?.NowNs", currentLogTime, StringComparison.Ordinal);
            Assert.Contains("_managerWasResolved", ensureManager, StringComparison.Ordinal);
            Assert.Contains("_nextManagerResolveTime", ensureManager, StringComparison.Ordinal);
            Assert.Contains("ResolveManager();", ensureManager, StringComparison.Ordinal);
            Assert.Contains("if (!EnsureManagerAvailable()) return;", generic, StringComparison.Ordinal);
        }

        [Fact]
        public void PublisherBaseCentralizesPublishToggleAndCachesInspectorSummaries()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var shouldPublish = SourceMethod(source, "protected bool ShouldPublishNow");
            var shouldPublishFixed = SourceMethod(source, "protected bool ShouldPublishNowFixed");

            Assert.Contains("if (!_publishOnEnable)", shouldPublish, StringComparison.Ordinal);
            Assert.Contains("if (!_publishOnEnable)", shouldPublishFixed, StringComparison.Ordinal);
            Assert.Contains("_supportedEncodingSummaryCache", source, StringComparison.Ordinal);
            Assert.Contains("get { return _supportedEncodingSummaryCache ??= BuildSupportedEncodingSummary(); }", source, StringComparison.Ordinal);
            Assert.Contains("InvalidateSupportedEncodingSummaryCache();", source, StringComparison.Ordinal);
            Assert.Contains("cache the value locally before", source, StringComparison.Ordinal);
        }

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing method: " + signature);
            return SourceBlock(source, start, signature);
        }

        private static string SourceProperty(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing property: " + signature);
            return SourceBlock(source, start, signature);
        }

        private static string SourceBlock(string source, int start, string label)
        {
            var brace = source.IndexOf('{', start);
            Assert.True(brace >= 0, "Missing body: " + label);

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unterminated body: " + label);
        }
    }
}
