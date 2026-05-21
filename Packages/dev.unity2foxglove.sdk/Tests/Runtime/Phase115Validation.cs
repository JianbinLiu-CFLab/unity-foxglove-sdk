// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 115 SDK schema manifest aggregate validation.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Foxglove.Schemas;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase115Validation
    {
        private const string ExpectedFoxRunFixtureHash = "54a93011d18c1ba9d53c955eb047c285096fb1a0a58376beb31c935ed3eff0e4";
        private const string SharedDir = "Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest";
        private const string GeneratorPath = "Packages/dev.unity2foxglove.sdk/Editor/SchemaManifest/Unity2FoxgloveSchemaManifestGenerator.cs";
        private const string PlayModeHookPath = "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestPlayModeHook.cs";
        private const string BuildPreprocessPath = "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs";
        private const string ReplayControllerPath = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 115: SDK Schema Manifest Aggregate ===");
            _passed = 0;

            VerifyCanonicalAggregateFixture();
            VerifyProtobufRegistrySection();
            VerifyRos2RegistrySection();
            VerifyPublisherCatalogSection();
            VerifyArtifactWriter();
            VerifySourceBoundariesAndWiring();
            VerifyDocs();

            Console.WriteLine($"Phase 115: {_passed} checks passed.");
        }

        private static void VerifyCanonicalAggregateFixture()
        {
            var aggregate = Unity2FoxgloveSchemaManifestBuilder.Build(FixtureManifest());
            var json = Unity2FoxgloveSchemaManifestJsonWriter.WriteCanonical(aggregate);
            var parsed = JObject.Parse(json);

            Check((int)parsed["manifestVersion"] == 1
                  && (string)parsed["package"] == "Unity2Foxglove"
                  && (string)parsed["generator"]["name"] == "Unity2FoxgloveSchemaManifest"
                  && (int)parsed["generator"]["majorVersion"] == 1,
                "115-A1: aggregate manifest identity is stable");

            var sections = (JObject)parsed["sections"];
            Check(sections.Properties().Select(p => p.Name).SequenceEqual(new[]
                  {
                      "foxRun",
                      "protobufRegistry",
                      "ros2MsgRegistry",
                      "sdkTypedPublishers"
                  }),
                "115-A2: aggregate sections are present in fixed order");

            Check((bool)sections["foxRun"]["present"]
                  && (string)sections["foxRun"]["globalManifestHash"] == ExpectedFoxRunFixtureHash
                  && (string)sections["foxRun"]["source"] == "generatedRegistry",
                "115-A3: FoxRun section imports runtime schema summary without replacing the FoxRun manifest");

            var sectionHashes = (JObject)parsed["sectionHashes"];
            Check(IsLowercaseSha256Hex((string)sectionHashes["foxRun"])
                  && IsLowercaseSha256Hex((string)sectionHashes["protobufRegistry"])
                  && IsLowercaseSha256Hex((string)sectionHashes["ros2MsgRegistry"])
                  && IsLowercaseSha256Hex((string)sectionHashes["sdkTypedPublishers"]),
                "115-A4: each aggregate section has a lowercase SHA-256 hash");

            Check(IsLowercaseSha256Hex(aggregate.SdkSchemaManifestHash)
                  && (string)parsed["sdkSchemaManifestHash"] == aggregate.SdkSchemaManifestHash,
                "115-A5: aggregate manifest has a stable SDK schema manifest hash");

            var aggregateAgain = Unity2FoxgloveSchemaManifestBuilder.Build(FixtureManifest());
            var jsonAgain = Unity2FoxgloveSchemaManifestJsonWriter.WriteCanonical(aggregateAgain);
            Check(json == jsonAgain && aggregate.SdkSchemaManifestHash == aggregateAgain.SdkSchemaManifestHash,
                "115-A6: two consecutive aggregate builds produce identical canonical JSON and hash");

            var aggregateHashInput = Unity2FoxgloveSchemaManifestJsonWriter.WriteAggregateHashInput(aggregate);
            Check(!aggregateHashInput.Contains("sdkSchemaManifestHash", StringComparison.Ordinal)
                  && aggregateHashInput.Contains("sectionHashes", StringComparison.Ordinal)
                  && Sha256Hex(aggregateHashInput) == aggregate.SdkSchemaManifestHash,
                "115-A7: aggregate hash input excludes sdkSchemaManifestHash and includes computed section hashes");

            var foxRunSectionInput = Unity2FoxgloveSchemaManifestJsonWriter.WriteFoxRunSectionHashInput(aggregate.Sections.FoxRun);
            Check(!foxRunSectionInput.Contains(aggregate.SectionHashes.FoxRun, StringComparison.Ordinal)
                  && Sha256Hex(foxRunSectionInput) == aggregate.SectionHashes.FoxRun,
                "115-A8: section hash input excludes its own output field");

            Check(!json.Contains("generatedAtUtc", StringComparison.Ordinal)
                  && !json.Contains("warnings", StringComparison.Ordinal),
                "115-A9: report-only fields stay out of canonical aggregate JSON");
        }

        private static void VerifyProtobufRegistrySection()
        {
            var aggregate = Unity2FoxgloveSchemaManifestBuilder.Build(FixtureManifest());
            var section = aggregate.Sections.ProtobufRegistry;
            var descriptorBytes = FoxgloveSchemas.FileDescriptorSetData;

            Check(section.SchemaEncoding == "protobuf"
                  && section.EntryCount == FoxgloveProtoSchemaCatalog.Entries.Count
                  && section.Entries.Count == FoxgloveProtoSchemaCatalog.Entries.Count,
                "115-B1: protobuf section covers the bundled protobuf catalog");

            Check(descriptorBytes.Length > 0
                  && section.DescriptorDataSha256 == Sha256Hex(descriptorBytes),
                "115-B2: descriptorDataSha256 is computed from decoded FileDescriptorSetData bytes");

            Check(IsLowercaseSha256Hex(section.CatalogEntryHash)
                  && IsLowercaseSha256Hex(section.DescriptorDataSha256)
                  && section.CatalogEntryHash != section.DescriptorDataSha256,
                "115-B3: protobuf catalogEntryHash and descriptorDataSha256 are separate domains");

            Check(section.Entries.Select(e => e.SchemaName)
                    .SequenceEqual(section.Entries.Select(e => e.SchemaName).OrderBy(v => v, StringComparer.Ordinal)),
                "115-B4: protobuf entries are sorted by stable schema name");
        }

        private static void VerifyRos2RegistrySection()
        {
            var aggregate = Unity2FoxgloveSchemaManifestBuilder.Build(FixtureManifest());
            var section = aggregate.Sections.Ros2MsgRegistry;

            Check(section.SchemaEncoding == FoxgloveRos2MsgSchemaCatalog.SchemaEncoding
                  && section.SourceSnapshot == FoxgloveRos2MsgSchemaCatalog.SourceSnapshot
                  && section.SourceCommit == FoxgloveRos2MsgSchemaCatalog.SourceCommit
                  && section.SourceTreeSha256 == FoxgloveRos2MsgSchemaCatalog.SourceTreeSha256,
                "115-C1: ROS2 .msg section records source snapshot identity");

            Check(section.SourceFileCount == FoxgloveRos2MsgSchemaCatalog.SourceFileCount
                  && section.EntryCount == FoxgloveRos2MsgSchemaCatalog.Entries.Count
                  && section.EntryCount == section.SourceFileCount,
                "115-C2: ROS2 .msg section entry count matches SourceFileCount");

            Check(section.Entries.All(e => IsLowercaseSha256Hex(e.SourceSha256))
                  && section.Entries.Select(e => e.SchemaName)
                      .SequenceEqual(section.Entries.Select(e => e.SchemaName).OrderBy(v => v, StringComparer.Ordinal)),
                "115-C3: ROS2 .msg entries are sorted and carry source hashes");
        }

        private static void VerifyPublisherCatalogSection()
        {
            var aggregate = Unity2FoxgloveSchemaManifestBuilder.Build(FixtureManifest());
            var section = aggregate.Sections.SdkTypedPublishers;

            Check(section.Entries.Count == FoxgloveSdkPublisherCatalog.Entries.Count
                  && section.Entries.Count >= 10,
                "115-D1: SDK typed publisher section comes from an explicit SDK-owned catalog");

            Check(section.Entries.Any(e => e.PublisherTypeFullName == "Unity.FoxgloveSDK.Components.FoxgloveTransformPublisher"
                                           && e.EntryKind == "concretePublisher"
                                           && e.FoxgloveSchemaName == "foxglove.FrameTransform"
                                           && e.Ros2SchemaName == Ros2PublisherSchemaNames.FrameTransform),
                "115-D2: concrete transform publisher coverage includes protobuf and ROS2 schemas");

            Check(section.Entries.Any(e => e.PublisherTypeFullName == "Unity.FoxgloveSDK.Components.FoxgloveCameraPublisher"
                                           && e.ProductNote.Contains("JPEG", StringComparison.Ordinal)
                                           && e.FoxgloveSchemaName == "foxglove.CompressedImage")
                  && section.Entries.Any(e => e.PublisherTypeFullName == "Unity.FoxgloveSDK.Components.FoxgloveCameraPublisher"
                                              && e.ProductNote.Contains("video", StringComparison.OrdinalIgnoreCase)
                                              && e.FoxgloveSchemaName == "foxglove.CompressedVideo"),
                "115-D3: multi-profile camera publisher coverage is explicit and not scene-derived");

            var templates = section.Entries.Where(e => e.IsTemplate).ToList();
            Check(templates.Count == 2
                  && templates.All(e => e.EntryKind == "genericTemplate"
                                        && string.IsNullOrEmpty(e.FoxgloveSchemaName)
                                        && string.IsNullOrEmpty(e.Ros2SchemaName)),
                "115-D4: generic publisher templates are capability evidence without fixed schema names");

            var concrete = section.Entries.Where(e => !e.IsTemplate).ToList();
            Check(concrete.All(e => string.IsNullOrEmpty(e.FoxgloveSchemaName) || FoxgloveProtoSchemaCatalog.TryGet(e.FoxgloveSchemaName, out _))
                  && concrete.All(e => string.IsNullOrEmpty(e.Ros2SchemaName) || FoxgloveRos2MsgSchemaCatalog.TryGet(e.Ros2SchemaName, out _)),
                "115-D5: concrete publisher schema names resolve through protobuf or ROS2 catalogs");

            Check(!string.Join("\n", section.Entries.Select(e => e.PublisherTypeFullName))
                    .Contains("TestLog", StringComparison.OrdinalIgnoreCase),
                "115-D6: SDK publisher catalog does not scan user-authored subclasses");
        }

        private static void VerifyArtifactWriter()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "unity2foxglove-phase115-" + Guid.NewGuid().ToString("N"));
            try
            {
                var aggregate = Unity2FoxgloveSchemaManifestBuilder.Build(FixtureManifest());
                Unity2FoxgloveSchemaManifestWriter.WriteManifestFiles(
                    tempRoot,
                    aggregate,
                    "2026-05-21T00:00:00.0000000Z",
                    new[] { "report-only warning" });

                var manifestPath = Path.Combine(tempRoot, Unity2FoxgloveSchemaManifestWriter.ManifestJsonFileName);
                var hashPath = Path.Combine(tempRoot, Unity2FoxgloveSchemaManifestWriter.ManifestHashFileName);
                var reportPath = Path.Combine(tempRoot, Unity2FoxgloveSchemaManifestWriter.ManifestReportFileName);

                Check(File.Exists(manifestPath) && File.Exists(hashPath) && File.Exists(reportPath),
                    "115-E1: writer emits aggregate manifest, hash sidecar, and report artifacts");

                var manifestJson = File.ReadAllText(manifestPath);
                var hashText = File.ReadAllText(hashPath);
                var hashBytes = File.ReadAllBytes(hashPath);
                Check(manifestJson == Unity2FoxgloveSchemaManifestJsonWriter.WriteCanonical(aggregate)
                      && hashText == aggregate.SdkSchemaManifestHash + "\n"
                      && !hashText.Contains("\r", StringComparison.Ordinal)
                      && hashBytes.SequenceEqual(Encoding.ASCII.GetBytes(aggregate.SdkSchemaManifestHash + "\n")),
                    "115-E2: hash sidecar uses stable LF newline and no BOM");

                var report = File.ReadAllText(reportPath);
                Check(report.Contains("generatedAtUtc", StringComparison.Ordinal)
                      && report.Contains("report-only warning", StringComparison.Ordinal)
                      && !manifestJson.Contains("report-only warning", StringComparison.Ordinal),
                    "115-E3: generated time and warnings are report-only");

                Unity2FoxgloveSchemaManifestWriter.WriteManifestFiles(
                    tempRoot,
                    aggregate,
                    "2026-05-21T00:00:00.0000000Z",
                    new[] { "report-only warning" });
                Check(File.ReadAllText(manifestPath) == manifestJson
                      && File.ReadAllText(hashPath) == hashText,
                    "115-E4: repeated writes are deterministic for canonical artifacts");
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
        }

        private static void VerifySourceBoundariesAndWiring()
        {
            foreach (var path in new[]
            {
                SharedDir + "/Unity2FoxgloveSchemaManifestModel.cs",
                SharedDir + "/Unity2FoxgloveSchemaManifestBuilder.cs",
                SharedDir + "/Unity2FoxgloveSchemaManifestJsonWriter.cs",
                SharedDir + "/Unity2FoxgloveSchemaManifestWriter.cs",
                SharedDir + "/FoxgloveSdkPublisherCatalog.cs",
                GeneratorPath
            })
            {
                Check(RepoFileExists(path), "115-F1: required schema manifest source file exists: " + path);
            }

            var model = ReadRepoText(SharedDir + "/Unity2FoxgloveSchemaManifestModel.cs");
            Check(!model.Contains("class Unity2FoxgloveSchemaManifestGenerator\r", StringComparison.Ordinal)
                  && !model.Contains("class Unity2FoxgloveSchemaManifestGenerator\n", StringComparison.Ordinal)
                  && model.Contains("Unity2FoxgloveSchemaManifestGeneratorInfo", StringComparison.Ordinal),
                "115-F1b: manifest DTO names do not collide with the editor generator entry point");

            var generator = ReadRepoText(GeneratorPath);
            Check(generator.Contains("GenerateArtifacts", StringComparison.Ordinal)
                  && generator.Contains("FoxrunCodeGenerator.GenerateManifestAndSchemaInfoFilesOnly", StringComparison.Ordinal)
                  && generator.Contains("Unity2FoxgloveSchemaManifestWriter.WriteManifestFiles", StringComparison.Ordinal),
                "115-F2: deterministic editor/batch generator refreshes FoxRun first and writes aggregate artifacts");

            var hook = ReadRepoText(PlayModeHookPath);
            Check(hook.Contains("GenerateManifestFilesOnly", StringComparison.Ordinal)
                  && hook.Contains("Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts", StringComparison.Ordinal)
                  && hook.IndexOf("GenerateManifestFilesOnly", StringComparison.Ordinal) <
                  hook.IndexOf("Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts", StringComparison.Ordinal)
                  && !hook.Contains("GenerateSourceFiles()", StringComparison.Ordinal),
                "115-F3: Play Mode refresh writes aggregate after FoxRun artifacts without physical fallback files");

            var build = ReadRepoText(BuildPreprocessPath);
            Check(build.Contains("GenerateSourceFiles", StringComparison.Ordinal)
                  && build.Contains("VerifyGeneratedSchemaInfoFiles", StringComparison.Ordinal)
                  && build.Contains("Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts", StringComparison.Ordinal)
                  && build.IndexOf("VerifyGeneratedSchemaInfoFiles", StringComparison.Ordinal) <
                  build.IndexOf("Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts", StringComparison.Ordinal),
                "115-F4: Player build preprocess writes aggregate after FoxRun regeneration and schema-info verification");

            var gitignore = ReadRepoText(".gitignore");
            Check(gitignore.Contains("Unity2Foxglove/Assets/Generated/Unity2Foxglove/", StringComparison.Ordinal)
                  && gitignore.Contains("Unity2Foxglove/Assets/Generated/Unity2Foxglove.meta", StringComparison.Ordinal)
                  && gitignore.Contains("!Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs", StringComparison.Ordinal),
                "115-F5: generated aggregate artifacts are ignored without breaking FoxRun whitelist behavior");

            var replay = ReadRepoText(ReplayControllerPath);
            foreach (var token in new[] { "Unity2FoxgloveSchemaManifest", "schema-manifest", "sdkSchemaManifestHash" })
            {
                Check(!replay.Contains(token, StringComparison.Ordinal),
                    "115-F6: replay guard remains isolated from aggregate manifest token: " + token);
            }

            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            Check(project.Contains("Phase115Validation.cs", StringComparison.Ordinal)
                  && project.Contains("Editor/Shared/SchemaManifest", StringComparison.Ordinal)
                  && program.Contains("--phase115", StringComparison.Ordinal)
                  && program.Contains("Phase115Validation.Validate()", StringComparison.Ordinal),
                "115-F7: runtime validation project and runner wire --phase115");
        }

        private static void VerifyDocs()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/07_FoxRun_Zero_Code_Publishing.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/08_MCAP_Recording_and_Replay.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/13_Schema_Coverage.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/zh/07_FoxRun自动发布.md",
                "docs/research-shared-emitter-architecture.md"
            })
            {
                var doc = ReadRepoText(path);
                Check(doc.Contains("SDK schema manifest", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("protobuf", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("ROS2", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("replay", StringComparison.OrdinalIgnoreCase),
                    "115-G1: docs describe SDK aggregate manifest as separate from replay governance: " + path);
            }
        }

        private static FoxRunCanonicalManifest FixtureManifest()
        {
            return FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo",
                    "RobotState",
                    "_batteryLevel",
                    "field",
                    "System.Single",
                    true,
                    false,
                    "",
                    "/phase112/battery",
                    10f,
                    "",
                    1,
                    0.001f,
                    0f)
            });
        }

        private static string Sha256Hex(string value)
        {
            return Sha256Hex(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes ?? Array.Empty<byte>());
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static bool IsLowercaseSha256Hex(string value)
        {
            return value != null
                   && value.Length == 64
                   && value.All(ch => (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'));
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath));
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
