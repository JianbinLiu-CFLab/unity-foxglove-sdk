// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Compatibility taxonomy, metadata version and policy vectors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    public sealed partial class FoxRunCompatibilityAnalyzerTests
    {
        [Fact]
        public void VersionTwoRecordsUseLegacyFieldProjectionWhenMetadataGainsFields()
        {
            var recordedManifest = ManifestWithContracts(
                "recorded",
                ContractWithFields(
                    "contract", "binding", "policy", "json",
                    new FoxRunSchemaFieldInfo("field0", "field0", "field", "int32", true, true, aggregate: true)));
            var currentManifest = ManifestWithContracts(
                    "current",
                ContractWithFields(
                    "contract", "binding", "policy", "json",
                    new FoxRunSchemaFieldInfo("field0", "field0", "field", "int32", true, true, aggregate: true),
                    new FoxRunSchemaFieldInfo("field1", "field1", "field", "string", false, false)));
            Assert.True(FoxRunSchemaMcapMetadata.TryCreateJson(recordedManifest, out var json));
            var legacyJson = json.Replace("\"schemaMetadataVersion\":3", "\"schemaMetadataVersion\":2", StringComparison.Ordinal)
                .Replace(",\"nullable\":true,\"array\":true,\"aggregate\":true,\"protobufFieldNumber\":0,\"typeShapeDigest\":\"\"", string.Empty, StringComparison.Ordinal);
            Assert.True(FoxRunSchemaMcapMetadata.TryParseJson(legacyJson, out var record, out var error), error);

            var result = FoxRunCompatibilityAnalyzer.Analyze(record, currentManifest);

            Assert.Equal(FoxRunCompatibilityClass.BackwardCompatible, result.Classification);
        }

        [Theory]
        [InlineData("json")]
        [InlineData("msgpack")]
        public void VersionTwoNameBasedFieldsIgnoreInsertedOrdinal(string encoding)
        {
            var recordedManifest = ManifestWithContracts(
                "recorded",
                ContractWithFields(
                    "old-contract", "old-binding", "old-policy", encoding,
                    new FoxRunSchemaFieldInfo("b", "b", "field", "int32", false, false),
                    new FoxRunSchemaFieldInfo("c", "c", "field", "string", false, false)));
            var currentManifest = ManifestWithContracts(
                "current",
                ContractWithFields(
                    "new-contract", "new-binding", "new-policy", encoding,
                    new FoxRunSchemaFieldInfo("a", "a", "field", "bool", false, false),
                    new FoxRunSchemaFieldInfo("b", "b", "field", "int32", false, false),
                    new FoxRunSchemaFieldInfo("c", "c", "field", "string", false, false)));

            Assert.True(
                FoxRunSchemaMcapMetadata.TryCreateJson(
                    recordedManifest,
                    out var json));
            var legacyJson = json.Replace(
                "\"schemaMetadataVersion\":3",
                "\"schemaMetadataVersion\":2",
                StringComparison.Ordinal);
            Assert.True(
                FoxRunSchemaMcapMetadata.TryParseJson(
                    legacyJson,
                    out var record,
                    out var error),
                error);

            var result = FoxRunCompatibilityAnalyzer.Analyze(
                record,
                currentManifest);

            Assert.Equal(
                FoxRunCompatibilityClass.BackwardCompatible,
                result.Classification);
        }

        [Fact]
        public void CurrentVersionRequiresFieldArraysDuringParsing()
        {
            var current = Manifest("g", "c", "b", "p", "int32");
            Assert.True(FoxRunSchemaMcapMetadata.TryCreateJson(current, out var json));
            var malformed = json.Replace(",\"fields\":[{\"name\":\"field0\",\"canonicalType\":\"int32\",\"ordinal\":0,\"encoding\":\"json\",\"nullable\":false,\"array\":false,\"aggregate\":false,\"protobufFieldNumber\":0,\"typeShapeDigest\":\"\"}]", string.Empty, StringComparison.Ordinal);

            Assert.False(FoxRunSchemaMcapMetadata.TryParseJson(malformed, out _, out var error));
            Assert.Contains("contract fields are missing", error, StringComparison.Ordinal);
        }

        [Fact]
        public void MalformedMetadataUsesOneConsistentPolicyDiagnostic()
        {
            var current = Manifest("g", "c", "b", "p", "int32");
            var baseResult = FoxRunSchemaMcapMetadata.CreateMalformedRecordedResult("detail");
            var warning = FoxRunSchemaMcapMetadata.EvaluateRecordedJson(
                "{\"schemaMetadataVersion\":1}",
                current,
                SchemaIdentityMode.Warn);
            var strict = FoxRunSchemaMcapMetadata.EvaluateRecordedJson(
                "{\"schemaMetadataVersion\":1}",
                current,
                SchemaIdentityMode.Strict);

            Assert.DoesNotContain("replay will continue", baseResult.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Replay blocked", baseResult.Message, StringComparison.Ordinal);
            Assert.Contains("Replay will continue", warning.Message, StringComparison.Ordinal);
            Assert.False(warning.IsBlocking);
            Assert.Contains("Replay blocked", strict.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Replay will continue", strict.Message, StringComparison.Ordinal);
            Assert.True(strict.IsBlocking);
        }

        [Fact]
        public void MemberDecompositionKeepsCanonicalValues()
        {
            var member = new FoxRunGenerationMember(
                "Demo", "State", "_value", "field", "System.Int32", true, false, "",
                "/state", 10f, "Demo.State", 1, 0f, "UnitTest", 0, "",
                encoding: "json");
            var decomposition = FoxRunGenerationMemberDecomposition.From(member);
            Assert.Equal(member.EmissionTypeName, decomposition.Type.EmissionTypeName);
            Assert.Equal(member.Hz, decomposition.Schedule.Hz);
            Assert.Equal(member.Encoding, decomposition.Encoding.Encoding);
            Assert.Equal(member.ConditionMemberKind, decomposition.Condition.MemberKind);
        }

        [Fact]
        public void CompatibilityVectorFixtureIsDeclarativeAndComplete()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null
                   && !Directory.Exists(Path.Combine(root.FullName, ".git"))
                   && !File.Exists(Path.Combine(root.FullName, ".git")))
                root = root.Parent;
            Assert.NotNull(root);
            var path = Path.Combine(root.FullName, "Packages", "dev.unity2foxglove.sdk", "Tests", "Unit", "FoxRun", "Fixtures", "Phase191CompatibilityVectors.json");
            var vectors = JObject.Parse(File.ReadAllText(path))["vectors"] as JArray;
            Assert.NotNull(vectors);
            Assert.Equal(13, vectors.Count);
            foreach (var vector in vectors)
            {
                Assert.False(string.IsNullOrWhiteSpace((string)vector["id"]));
                Assert.True(Enum.TryParse((string)vector["expected"], out FoxRunCompatibilityClass _));
            }
        }

        [Fact]
        public void ConflictedGeneratedAuthorityBlocksReplayIdentityEvaluation()
        {
            var first = Manifest("first", "c", "b", "p");
            var second = Manifest("second", "c", "b", "p");
            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(first);
                FoxRunSchemaInfoRegistry.RegisterGenerated(second);

                var result = FoxRunSchemaMcapMetadata.Evaluate(
                    FoxRunSchemaMcapMetadata.CreateRecord(first),
                    first,
                    SchemaIdentityMode.Strict);

                Assert.True(FoxRunSchemaInfoRegistry.HasConflict);
                Assert.Equal(FoxRunReplaySchemaGuardState.Mismatch, result.State);
                Assert.True(result.IsBlocking);
                Assert.Contains("authority is conflicted", result.Message, StringComparison.Ordinal);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        public void RecordingControllerSkipsSchemaEvidenceWhenAuthorityIsConflicted()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-conflicted-schema-"
                + Guid.NewGuid().ToString("N")
                + ".mcap");
            var first = Manifest("first", "c", "b", "p");
            var second = Manifest("second", "c", "b", "p");
            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(first);
                FoxRunSchemaInfoRegistry.RegisterGenerated(second);

                using var session = new FoxgloveSession(
                    "conflicted-schema",
                    new NoopTransport());
                using var controller = new RecordingController(new ConsoleLogger());
                controller.Enable(path);
                controller.AttachToSession(new FoxgloveParameterStore(), session);
                controller.DetachFromSession();

                using var indexed = McapIndexedReader.OpenRead(path);
                Assert.DoesNotContain(
                    indexed.MetadataIndexes,
                    index => index.Name == FoxRunSchemaMcapMetadata.MetadataName);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void ReplayGuardBlocksSdkWideWireSchemaMismatchWithoutFoxRunMetadata()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-sdk-wire-schema-mismatch-"
                + Guid.NewGuid().ToString("N")
                + ".mcap");
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "module8.ReplayIdentity",
                Encoding = "jsonschema",
                Content = "{\"type\":\"integer\"}"
            });

            try
            {
                Assert.True(SdkWireSchemaIdentity.TryCompute(registry, out var currentHash));
                Assert.True(SdkWireSchemaMcapMetadata.TryCreateJson(
                    new string('0', currentHash.Length),
                    out var recordedJson));
                using (var stream = new MemoryStream())
                {
                    using (var recorder = new McapRecorder(stream, leaveOpen: true))
                    {
                        recorder.WriteMetadata(SdkWireSchemaMcapMetadata.MetadataName, recordedJson);
                        recorder.Close();
                    }
                    File.WriteAllBytes(path, stream.ToArray());
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = ReplaySchemaGuard.EvaluateWithMode(
                    engine,
                    SchemaIdentityMode.Strict,
                    registry);

                Assert.Equal(FoxRunReplaySchemaGuardState.Mismatch, result.State);
                Assert.True(result.IsBlocking);
                Assert.Contains("SDK-wide wire-schema identity mismatch", result.Message, StringComparison.Ordinal);

                var compatible = ReplaySchemaGuard.EvaluateWithMode(
                    engine,
                    SchemaIdentityMode.Compatible,
                    registry);
                Assert.Equal(FoxRunReplaySchemaGuardState.Mismatch, compatible.State);
                Assert.False(compatible.IsBlocking);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void RecordingIdentityUsesSchemasActuallyWrittenAfterLateRegistration()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-sdk-wire-schema-late-registration-"
                + Guid.NewGuid().ToString("N")
                + ".mcap");
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "module8.Recorded",
                Encoding = "jsonschema",
                Content = "{\"type\":\"integer\"}"
            });

            try
            {
                using var session = new FoxgloveSession(
                    "module8-late-registration",
                    new NoopTransport(),
                    schemaRegistry: registry);
                session.RegisterSchemaChannel(1, "/module8/recorded", "module8.Recorded", "json");
                using var controller = new RecordingController(new ConsoleLogger(), new SystemClock(), registry);
                controller.Enable(path);
                controller.AttachToSession(new FoxgloveParameterStore(), session);

                registry.Register(new SchemaEntry
                {
                    Name = "module8.RegisteredAfterAttach",
                    Encoding = "jsonschema",
                    Content = "{\"type\":\"string\"}"
                });
                session.Publish(1, new byte[] { 1 }, 8UL);
                controller.DetachFromSession();

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var metadata = engine.FindMetadata(SdkWireSchemaMcapMetadata.MetadataName);
                Assert.NotNull(metadata);
                Assert.True(SdkWireSchemaMcapMetadata.TryParseJson(
                    metadata.Metadata["value"],
                    out _,
                    out var version,
                    out var error), error);
                Assert.Equal(2, version);

                var result = ReplaySchemaGuard.EvaluateWithMode(
                    engine,
                    SchemaIdentityMode.Strict,
                    registry);
                Assert.Equal(FoxRunReplaySchemaGuardState.Match, result.State);
                Assert.False(result.IsBlocking);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static FoxRunCompatibilityResult Analyze(
            string recordedGlobal,
            string recordedContract,
            string recordedBinding,
            string recordedPolicy,
            string currentGlobal,
            string currentContract,
            string currentBinding,
            string currentPolicy,
            string currentType = "int32")
        {
            var recorded = FoxRunSchemaMcapMetadata.CreateRecord(Manifest(
                recordedGlobal, recordedContract, recordedBinding, recordedPolicy, "int32"));
            var current = Manifest(currentGlobal, currentContract, currentBinding, currentPolicy, currentType);
            return FoxRunCompatibilityAnalyzer.Analyze(recorded, current);
        }

        private static FoxRunSchemaManifestInfo Manifest(
            string global,
            string contractHash,
            string bindingHash,
            string policyHash,
            params string[] fieldTypes)
        {
            return ManifestWithContracts(global, Contract(
                contractHash,
                bindingHash,
                policyHash,
                fieldTypes: fieldTypes));
        }

        private static FoxRunSchemaManifestInfo ManifestWithContracts(
            string global,
            params FoxRunSchemaContractInfo[] contracts)
        {
            return new FoxRunSchemaManifestInfo(
                4, "unit", "unit", 1, global, "manifest", new[] { new FoxRunSchemaTypeInfo("Demo.State", contracts) });
        }

        private static FoxRunSchemaContractInfo Contract(
            string contractHash,
            string bindingHash,
            string policyHash,
            string topic = "/state",
            string schemaName = "Demo.State",
            string encoding = "json",
            string flow = "Publish",
            params string[] fieldTypes)
        {
            var fields = new List<FoxRunSchemaFieldInfo>();
            var types = fieldTypes == null || fieldTypes.Length == 0 ? new[] { "int32" } : fieldTypes;
            for (var index = 0; index < types.Length; index++)
                fields.Add(new FoxRunSchemaFieldInfo("field" + index, "field" + index, "field", types[index], false, false));
            return new FoxRunSchemaContractInfo(
                "Demo.State", topic, schemaName, encoding, contractHash, bindingHash, policyHash,
                "Publish", 1f, 0f, fields, flow: flow);
        }

        private static FoxRunSchemaContractInfo ContractWithFields(
            string contractHash,
            string bindingHash,
            string policyHash,
            string encoding,
            params FoxRunSchemaFieldInfo[] fields)
        {
            return new FoxRunSchemaContractInfo(
                "Demo.State", "/state", "Demo.State", encoding,
                contractHash, bindingHash, policyHash,
                "Publish", 1f, 0f, fields, flow: "Publish");
        }

        private sealed class NoopTransport : IFoxgloveTransport
        {
            public bool IsRunning => false;
            public event Action<uint> OnClientConnected { add { } remove { } }
            public event Action<uint> OnClientDisconnected { add { } remove { } }
            public event Action<uint, string> OnTextReceived { add { } remove { } }
            public event Action<uint, byte[]> OnBinaryReceived { add { } remove { } }
            public void Start(string host, int port) { }
            public void Stop() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() { }
        }
    }
}
