// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Phase173109ReviewTests
    {
        [Fact]
        public void RemoteGatewayNoticesCarryPreviewRedistributionBoundary()
        {
            var notice = TestSources.Text(
                "Packages/dev.unity2foxglove.remotegateway.win64/THIRD_PARTY_NOTICES.md");

            Assert.Contains("Preview redistribution boundary", notice);
            Assert.DoesNotContain("Before publishing this package", notice);
        }

        [Fact]
        public void Ros2RuntimePackagesDeclareSiblingConflictMetadata()
        {
            var humble = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/package.json");
            var jazzy = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/package.json");
            var lyrical = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/package.json");

            Assert.Contains("unity2foxgloveConflicts", humble);
            Assert.Contains("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", humble);
            Assert.Contains("dev.unity2foxglove.ros2forunity.runtime.humble.win64", jazzy);
            Assert.Contains("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", lyrical);
        }

        [Fact]
        public void FoxRunGeneratedLinkXmlIsIgnoredAndDemoLinkCopyIsRemoved()
        {
            var gitignore = TestSources.Text(".gitignore");

            Assert.Contains("Unity2Foxglove/Assets/FoxRun_link.xml", gitignore);
            Assert.False(File.Exists(Path.Combine(FindRepoRoot(), "Unity2Foxglove", "Assets", "link.xml")));
        }

        [Fact]
        public void BackfillQueryDefaultsAvoidFilterListAllocation()
        {
            var query = new McapDataLoaderBackfillQuery();

            Assert.Null(query.ChannelIds);
            Assert.Null(query.Topics);

            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            Assert.DoesNotContain("query = query ?? new McapDataLoaderBackfillQuery()", source);
            Assert.Contains("EndTimeNs = query?.TimeNs ?? ulong.MaxValue", source);
        }

        [Fact]
        public void LoggerPrefixesAvoidInterpolationSyntax()
        {
            var unityLogger = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Logging/FoxgloveLogger.cs");
            var coreLogger = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Abstractions/IFoxgloveLogger.cs");

            Assert.Contains("string.Concat(Prefix, message)", unityLogger);
            Assert.Contains("string.Concat(WarningPrefix, message)", coreLogger);
            Assert.DoesNotContain("$\"[Foxglove]", unityLogger);
            Assert.DoesNotContain("$\"[Foxglove]", coreLogger);
        }

        [Fact]
        public void FoxServiceDtoSideRejectsUnknownFutureValues()
        {
            Assert.Equal(FoxServiceDtoRules.RequestSide, FoxServiceDtoSide.Request.ToRuleSide());
            Assert.Equal(FoxServiceDtoRules.ResponseSide, FoxServiceDtoSide.Response.ToRuleSide());
            Assert.Throws<ArgumentOutOfRangeException>(() => ((FoxServiceDtoSide)99).ToRuleSide());
        }

        [Fact]
        public void SensitiveManagerFieldsHaveCredentialSafetyTooltips()
        {
            var manager = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");

            Assert.Contains("Avoid committing machine-local or private certificate paths", manager);
            Assert.Contains("Prefer FOXGLOVE_CERTIFICATE_PASSWORD", manager);
            Assert.Contains("Prefer FOXGLOVE_SHARED_TOKEN", manager);
            Assert.Contains("Prefer FOXGLOVE_REPLAY_CURSOR_TOKEN", manager);
        }

        [Fact]
        public void TrailerFieldsAndValidationCategoriesStayExplicit()
        {
            var trailer = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapTrailerInfo.cs");
            var categories = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/ValidationCategory.cs");

            Assert.DoesNotContain("public ulong FooterOffset", trailer);
            Assert.Contains("internal ulong FooterOffset", trailer);
            Assert.Contains("No Unity Editor, network, hardware", categories);
            Assert.Contains("Requires human observation", categories);
        }

        [Fact]
        public void NativeBackendPlaceholderIsNotPartialImplementation()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Transport/Native/NativeFoxgloveBackend.cs");

            Assert.Contains("treat this file as a partially implemented backend", source);
            Assert.DoesNotContain("RequiresNativePlugin", source);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }
}
