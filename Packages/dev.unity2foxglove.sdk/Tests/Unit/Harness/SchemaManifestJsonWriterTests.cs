// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Regression coverage for schema-manifest JSON writer edge cases.

using System;
using System.Text.Json;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-050")]
    [Trait("Domain", "Harness")]
    public sealed class SchemaManifestJsonWriterTests
    {
        [Fact]
        public void WriteReportRejectsNullManifestWithParameterName()
        {
            var ex = Assert.Throws<ArgumentNullException>(
                () => Unity2FoxgloveSchemaManifestJsonWriter.WriteReport(null, "2026-01-01T00:00:00Z", null));

            Assert.Equal("manifest", ex.ParamName);
        }

        [Fact]
        public void WriteReportEscapesSupplementaryUnicodeAsValidJson()
        {
            var report = Unity2FoxgloveSchemaManifestJsonWriter.WriteReport(
                EmptyManifest(),
                "2026-01-01T00:00:00Z",
                new[] { "emoji \U0001F600" });

            using var document = JsonDocument.Parse(report);
            Assert.Equal("emoji \U0001F600", document.RootElement.GetProperty("warnings")[0].GetString());
            Assert.Contains("\\ud83d\\ude00", report, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void CurrentFoxRunDescriptorMajorVersionIsReportedAsSix()
        {
            var section = new Unity2FoxgloveFoxRunSummarySection(
                true,
                5,
                FoxRunGenerationDescriptorConstants.DescriptorVersion,
                "hash",
                "contracts",
                1,
                1,
                1,
                "subscriptions");

            var json = Unity2FoxgloveSchemaManifestJsonWriter.WriteFoxRunSectionHashInput(section);

            Assert.Contains("\"generatorMajorVersion\":6", json, StringComparison.Ordinal);
            Assert.Equal(6, FoxRunGenerationDescriptorConstants.DescriptorVersion);
        }

        private static Unity2FoxgloveSchemaManifest EmptyManifest()
        {
            var sections = new Unity2FoxgloveSchemaManifestSections(
                new Unity2FoxgloveFoxRunSummarySection(false, 0, 0, "", "", 0, 0, 0, ""),
                new Unity2FoxgloveProtobufRegistrySection("protobuf", "", "", 0, Array.Empty<Unity2FoxgloveProtobufRegistryEntry>()),
                new Unity2FoxgloveSdkTypedPublishersSection(0, Array.Empty<Unity2FoxgloveSdkTypedPublisherEntry>()));

            return new Unity2FoxgloveSchemaManifest(
                2,
                "Unity2Foxglove",
                new Unity2FoxgloveSchemaManifestGeneratorInfo("test", 1),
                sections,
                new Unity2FoxgloveSchemaManifestSectionHashes("", "", ""),
                "hash");
        }
    }
}
