// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text.RegularExpressions;
using System.Text.Json;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-102")]
    public sealed class Phase173102ReviewTests
    {
        [Fact]
        public void ProtobufPublisherCachesResolvedSchemaName()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/ProtobufPublisher.cs");

            Assert.Contains("private string _cachedSchemaName", source, StringComparison.Ordinal);
            Assert.Contains("if (_cachedSchemaName != null)", source, StringComparison.Ordinal);
            Assert.Contains("return _cachedSchemaName;", source, StringComparison.Ordinal);
        }

        [Fact]
        public void GenericPublisherWarnsOnceWhenMsgPackPayloadIsMissing()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisher.cs");

            Assert.Contains("_warnedMissingMsgPackPayload", source, StringComparison.Ordinal);
            Assert.Contains("selected MsgPack encoding but did not provide a MessagePack payload", source, StringComparison.Ordinal);
            Assert.Contains("Debug.LogWarning", source, StringComparison.Ordinal);
        }

        [Fact]
        public void VirtualLidarInspectorExposesProfileExtrinsicsAndPerformanceFields()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Sensors/VirtualLidarEditor.cs");

            Assert.Contains("_profileSource", source, StringComparison.Ordinal);
            Assert.Contains("_metadataJson", source, StringComparison.Ordinal);
            Assert.Contains("_customPixelsPerColumn", source, StringComparison.Ordinal);
            Assert.Contains("_overrideTIl", source, StringComparison.Ordinal);
            Assert.Contains("_maxRaycastCommandsPerFixedUpdate", source, StringComparison.Ordinal);
            Assert.Contains("DrawProfileSection", source, StringComparison.Ordinal);
            Assert.Contains("DrawTIlSection", source, StringComparison.Ordinal);
        }

        [Fact]
        public void DotnetWorkflowRunsLocalEntrypointValidation()
        {
            var source = TestSources.Text(".github/workflows/dotnet-tests.yml");
            var packageLane = TestSources.Text("Scripts/release/run_ci/cli.py");

            Assert.Contains("python3 -B Scripts/release/run_ci.py --only packages", source, StringComparison.Ordinal);
            Assert.Contains("\"Scripts/package/validate_local_entrypoints.py\"", packageLane, StringComparison.Ordinal);
            Assert.Contains("results[\"validate-entrypoints\"]", packageLane, StringComparison.Ordinal);
        }

        [Fact]
        public void MissingMcapReportDoesNotAddASecondaryWorkflowFailure()
        {
            var source = TestSources.Text(".github/workflows/dotnet-tests.yml");
            var uploadStart = source.IndexOf("- name: Upload MCAP differential report", StringComparison.Ordinal);
            Assert.True(uploadStart >= 0, "MCAP differential report upload step is missing.");
            var uploadEnd = source.IndexOf("\n      - ", uploadStart + 1, StringComparison.Ordinal);
            var uploadStep = source.Substring(
                uploadStart,
                (uploadEnd >= 0 ? uploadEnd : source.Length) - uploadStart);

            Assert.Contains("if-no-files-found: warn", uploadStep, StringComparison.Ordinal);
            Assert.DoesNotContain("if-no-files-found: error", uploadStep, StringComparison.Ordinal);
        }

        [Fact]
        public void AnalyzerFreshnessUsesGitLongPathsBeforeCheckoutAndValidatesWin64TypesupportIdentity()
        {
            var source = TestSources.Text(".github/workflows/dotnet-tests.yml");
            var analyzerStart = source.IndexOf("analyzer-freshness:", StringComparison.Ordinal);
            var longPathStep = source.IndexOf("Enable Git long-path support", StringComparison.Ordinal);
            var checkoutMatch = Regex.Match(source.Substring(analyzerStart), @"uses: actions/checkout@(?:v4|[0-9a-f]{40})");
            var checkout = checkoutMatch.Success ? analyzerStart + checkoutMatch.Index : -1;

            Assert.True(analyzerStart >= 0, "The Windows analyzer job is missing.");
            Assert.True(longPathStep > analyzerStart && longPathStep < checkout, "Git long-path support must be enabled before checkout.");
            Assert.Contains("git config --global core.longpaths true", source, StringComparison.Ordinal);
            Assert.Contains("Validate Phase181 Win64 custom ROS2 typesupport identities", source, StringComparison.Ordinal);
            Assert.Contains("validate_foxrun_custom_typesupport_addon.py --distro lyrical --require-rmw rmw_zenoh_cpp", source, StringComparison.Ordinal);
        }

        [Fact]
        public void PerformanceThresholdsKeepGen2CollectionsMeaningful()
        {
            var json = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Performance/performance-thresholds.json");
            var runner = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Performance/PerformanceRunner.cs");

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.Equal(10, root.GetProperty("defaults").GetProperty("maxGen2Collections").GetInt32());
            Assert.Equal(10, root.GetProperty("modes").GetProperty("quick").GetProperty("defaults").GetProperty("maxGen2Collections").GetInt32());
            Assert.Equal(50, root.GetProperty("modes").GetProperty("full").GetProperty("defaults").GetProperty("maxGen2Collections").GetInt32());
            Assert.Equal(2000, root.GetProperty("scenarios").GetProperty("LargePayloadFanout").GetProperty("maxGen2Collections").GetInt32());
            Assert.Equal(2000, root.GetProperty("modes").GetProperty("quick").GetProperty("scenarios").GetProperty("LargePayloadFanout").GetProperty("maxGen2Collections").GetInt32());
            Assert.Equal(2000, root.GetProperty("modes").GetProperty("full").GetProperty("scenarios").GetProperty("LargePayloadFanout").GetProperty("maxGen2Collections").GetInt32());
            Assert.Contains("maxGen2Collections = isFull ? 50 : 10", runner, StringComparison.Ordinal);
            Assert.DoesNotContain("\"maxGen2Collections\": 1000", json, StringComparison.Ordinal);
        }

        [Fact]
        public void IdentityPoseFactoryDocumentsFreshMutableInstances()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/FoxgloveProtoBuilderUtil.cs");

            Assert.Contains("Returns a fresh mutable object", source, StringComparison.Ordinal);
            Assert.Contains("do not replace with a shared cached instance", source, StringComparison.Ordinal);
        }
    }
}
