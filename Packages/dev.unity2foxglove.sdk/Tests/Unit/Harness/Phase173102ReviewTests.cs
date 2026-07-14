// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
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

            Assert.Contains("Validate local entrypoints", source, StringComparison.Ordinal);
            Assert.Contains("Scripts/package/validate_local_entrypoints.py", source, StringComparison.Ordinal);
        }

        [Fact]
        public void MissingMcapReportDoesNotAddASecondaryWorkflowFailure()
        {
            var source = TestSources.Text(".github/workflows/dotnet-tests.yml");
            var uploadStart = source.IndexOf("- name: Upload MCAP differential report", StringComparison.Ordinal);
            Assert.True(uploadStart >= 0, "MCAP differential report upload step is missing.");
            var uploadEnd = source.IndexOf("\n\n", uploadStart, StringComparison.Ordinal);
            var uploadStep = source.Substring(
                uploadStart,
                (uploadEnd >= 0 ? uploadEnd : source.Length) - uploadStart);

            Assert.Contains("if-no-files-found: warn", uploadStep, StringComparison.Ordinal);
            Assert.DoesNotContain("if-no-files-found: error", uploadStep, StringComparison.Ordinal);
        }

        [Fact]
        public void PerformanceThresholdsKeepGen2CollectionsMeaningful()
        {
            var json = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Performance/performance-thresholds.json");
            var runner = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Performance/PerformanceRunner.cs");

            Assert.Contains("\"maxGen2Collections\": 10", json, StringComparison.Ordinal);
            Assert.Contains("\"maxGen2Collections\": 50", json, StringComparison.Ordinal);
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
