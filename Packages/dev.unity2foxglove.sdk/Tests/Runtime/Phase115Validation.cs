// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 115 validation for the Provider-neutral SDK schema manifest.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase115Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 115: Provider-neutral SDK Schema Manifest ===");
            _passed = 0;

            VerifyCanonicalAggregate();
            VerifyTypedPublisherCatalog();
            VerifyDeterminism();
            VerifyArtifactWriter();
            VerifySourceBoundary();

            Console.WriteLine($"Phase 115: {_passed} checks passed.");
        }

        private static void VerifyCanonicalAggregate()
        {
            var aggregate = Unity2FoxgloveSchemaManifestBuilder.Build(FixtureManifest());
            var parsed = JObject.Parse(
                Unity2FoxgloveSchemaManifestJsonWriter.WriteCanonical(
                    aggregate));

            Check(
                aggregate.ManifestVersion
                == Unity2FoxgloveSchemaManifestBuilder.ManifestVersion
                && aggregate.ManifestVersion == 2
                && aggregate.Package == "Unity2Foxglove"
                && aggregate.Generator.Name
                   == Unity2FoxgloveSchemaManifestBuilder.GeneratorName,
                "115-A1: aggregate identity is stable at neutral manifest v2");

            var sectionNames = ((JObject)parsed["sections"])
                .Properties()
                .Select(property => property.Name)
                .ToArray();
            Check(
                sectionNames.SequenceEqual(
                    new[]
                    {
                        "foxRun",
                        "protobufRegistry",
                        "sdkTypedPublishers"
                    })
                && parsed["sections"]["ros2MsgRegistry"] == null,
                "115-A2: aggregate contains only SDK-owned neutral sections");

            Check(
                aggregate.Sections.FoxRun.Present
                && aggregate.Sections.FoxRun.ContractCount == 1
                && aggregate.Sections.ProtobufRegistry.EntryCount
                   == aggregate.Sections.ProtobufRegistry.Entries.Count
                && aggregate.SectionHashes.FoxRun.Length == 64
                && aggregate.SectionHashes.ProtobufRegistry.Length == 64
                && aggregate.SectionHashes.SdkTypedPublishers.Length == 64
                && aggregate.SdkSchemaManifestHash.Length == 64,
                "115-A3: section counts and SHA-256 identities are complete");
        }

        private static void VerifyTypedPublisherCatalog()
        {
            var section = Unity2FoxgloveSchemaManifestBuilder
                .Build(FixtureManifest())
                .Sections
                .SdkTypedPublishers;

            Check(
                section.EntryCount == section.Entries.Count
                && section.Entries.Count
                   == FoxgloveSdkPublisherCatalog.Entries.Count,
                "115-B1: typed publisher section comes from the explicit SDK catalog");
            Check(
                section.Entries.All(
                    entry => entry.SupportsJson
                             || entry.SupportsProtobuf
                             || entry.SupportsMsgPack)
                && section.Entries.All(
                    entry => !string.IsNullOrWhiteSpace(
                        entry.PublisherTypeFullName)),
                "115-B2: every SDK publisher declares a neutral wire capability");
            Check(
                section.Entries
                    .Select(entry => entry.PublisherTypeFullName)
                    .SequenceEqual(
                        section.Entries
                            .Select(entry => entry.PublisherTypeFullName)
                            .OrderBy(value => value, StringComparer.Ordinal)),
                "115-B3: typed publisher entries are deterministic");
        }

        private static void VerifyDeterminism()
        {
            var first = Unity2FoxgloveSchemaManifestJsonWriter.WriteCanonical(
                Unity2FoxgloveSchemaManifestBuilder.Build(FixtureManifest()));
            var second = Unity2FoxgloveSchemaManifestJsonWriter.WriteCanonical(
                Unity2FoxgloveSchemaManifestBuilder.Build(FixtureManifest()));
            Check(
                string.Equals(first, second, StringComparison.Ordinal),
                "115-C1: identical neutral inputs produce byte-identical JSON");

            var parsed = JObject.Parse(first);
            Check(
                string.Equals(
                    parsed["sdkSchemaManifestHash"]?.ToString(),
                    Unity2FoxgloveSchemaManifestBuilder
                        .Build(FixtureManifest())
                        .SdkSchemaManifestHash,
                    StringComparison.Ordinal),
                "115-C2: canonical JSON carries the computed aggregate hash");
        }

        private static void VerifyArtifactWriter()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-phase115-" + Guid.NewGuid().ToString("N"));
            try
            {
                var manifest =
                    Unity2FoxgloveSchemaManifestBuilder.Build(FixtureManifest());
                Unity2FoxgloveSchemaManifestWriter.WriteManifestFiles(
                    root,
                    manifest,
                    "2026-07-29T00:00:00.0000000Z",
                    Array.Empty<string>());

                var jsonPath = Path.Combine(
                    root,
                    Unity2FoxgloveSchemaManifestWriter.ManifestJsonFileName);
                var hashPath = Path.Combine(
                    root,
                    Unity2FoxgloveSchemaManifestWriter.ManifestHashFileName);
                var hashBytes =
                    File.Exists(hashPath)
                        ? File.ReadAllBytes(hashPath)
                        : Array.Empty<byte>();
                Check(
                    File.Exists(jsonPath)
                    && Encoding.ASCII.GetString(hashBytes).Trim()
                       == manifest.SdkSchemaManifestHash,
                    "115-D1: writer persists matching canonical JSON and hash");
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        private static void VerifySourceBoundary()
        {
            var builder = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/"
                + "SchemaManifest/Unity2FoxgloveSchemaManifestBuilder.cs");
            var model = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/"
                + "SchemaManifest/Unity2FoxgloveSchemaManifestModel.cs");
            Check(
                !builder.Contains("Ros2", StringComparison.Ordinal)
                && !model.Contains("Ros2", StringComparison.Ordinal)
                && !builder.Contains("cdr", StringComparison.OrdinalIgnoreCase),
                "115-E1: core aggregate model and builder own no ROS/CDR section");
        }

        private static FoxRunCanonicalManifest FixtureManifest()
            => FoxRunManifestBuilder.Build(
                new[]
                {
                    new FoxRunManifestMember(
                        "Fixture",
                        "Telemetry",
                        "Speed",
                        "field",
                        "System.Single",
                        true,
                        false,
                        string.Empty,
                        "/fixture/speed",
                        20f,
                        "fixture.Telemetry",
                        (int)FoxRunPolicy.FixedRate,
                        0f,
                        jsonFieldName: "speed",
                        flow: (int)FoxRunFlow.Publish,
                        encoding: (int)FoxRunEncoding.Protobuf,
                        publishTransportIds:
                            new[]
                            {
                                FoxgloveWebSocketTransport.Id
                            })
                });

        private static string ReadRepoText(string relativePath)
        {
            var root = TestRepoRootLocator.FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath));
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
