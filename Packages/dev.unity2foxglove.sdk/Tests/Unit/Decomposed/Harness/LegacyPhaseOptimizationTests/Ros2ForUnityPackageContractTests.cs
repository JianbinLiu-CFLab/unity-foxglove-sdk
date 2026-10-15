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

    [Trait("Phase", "140-24")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2ForUnityPackageContractTests
    {
        private const string BaseSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";
        [Fact]
        public void NativeBridgeDefinesAndDocsStayPackageScoped()
        {
            var asmdef = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Unity2Foxglove.Ros2ForUnity.Native.asmdef");
            var installer = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs");
            var selection = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var readme = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/README.md");
            var contextInterface = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/IUnity2FoxgloveRos2Context.cs");
            var unavailable = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2FoxgloveRos2UnavailableContext.cs");

            Assert.Contains("\"" + BaseSymbol + "\"", asmdef, StringComparison.Ordinal);
            Assert.DoesNotContain("JAZZY_WIN64_PACKAGE", asmdef, StringComparison.Ordinal);
            Assert.DoesNotContain("LYRICAL_WIN64_PACKAGE", asmdef, StringComparison.Ordinal);
            Assert.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime\"", asmdef, StringComparison.Ordinal);
            Assert.Contains("BaseCompileSymbol", installer, StringComparison.Ordinal);
            Assert.Contains("Ros2ForUnityRuntimeSelection.GetStatus()", installer, StringComparison.Ordinal);
            Assert.Contains("EnsureSymbol(parts, Ros2ForUnityRuntimeSelection.BaseCompileSymbol)", installer, StringComparison.Ordinal);
            Assert.DoesNotContain("RuntimeCompileSymbols", installer, StringComparison.Ordinal);
            Assert.Contains("DiscoverCandidateRuntimes", selection, StringComparison.Ordinal);
            Assert.Contains("SwitchActiveRuntimePackage", selection, StringComparison.Ordinal);
            Assert.DoesNotContain("ProjectSettings/Unity2FoxgloveRos2ForUnitySettings.json", selection, StringComparison.Ordinal);
            Assert.DoesNotContain("set the symbol manually for external imports", readme, StringComparison.Ordinal);
            Assert.DoesNotContain("For an external, non-package ROS2 For Unity import, add that symbol manually.", readme, StringComparison.Ordinal);
            Assert.Contains("manifest.json", readme, StringComparison.Ordinal);
            Assert.Contains("candidate runtime packages", readme, StringComparison.Ordinal);
            Assert.Contains("external source-only adapter samples", readme, StringComparison.Ordinal);
            Assert.Contains("Null or whitespace node names must be normalized", contextInterface, StringComparison.Ordinal);
            Assert.Contains("Unavailable subscriptions preserve the topic but intentionally do not invoke callbacks", unavailable, StringComparison.Ordinal);
        }

        [Fact]
        public void ComplianceHashesAreLowercase()
        {
            var manifest = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Compliance/ros2-for-unity-adoption-manifest.json");
            var matches = Regex.Matches(manifest, "\"(?:artifactSha256|releaseAssetSha256)\"\\s*:\\s*\"([0-9A-Fa-f]{64})\"");
            Assert.True(matches.Count >= 2);

            foreach (Match match in matches)
            {
                var value = match.Groups[1].Value;
                Assert.Equal(value.ToLowerInvariant(), value);
            }
        }

        [Fact]
        public void LyricalNoticesHashMatchesManifest()
        {
            var manifest = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Compliance/ros2-for-unity-adoption-manifest.json");
            var notices = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Compliance/r2fu-lyrical-win64-runtime-notices.md");
            var manifestMatch = Regex.Match(
                manifest,
                "\"runtimeId\"\\s*:\\s*\"r2fu-lyrical-win64\"[\\s\\S]*?\"artifactSha256\"\\s*:\\s*\"([0-9a-f]{64})\"");
            var noticeMatch = Regex.Match(notices, "\\| SHA-256 \\| `([0-9a-f]{64})` \\|");

            Assert.True(manifestMatch.Success);
            Assert.True(noticeMatch.Success);
            Assert.Equal(manifestMatch.Groups[1].Value, noticeMatch.Groups[1].Value);
        }

        [Fact]
        public void Phase14024MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_24Validation.cs", "--phase140-24", "Phase140_24Validation.Validate");
    }
}
