// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Conformance gate for MessagePack duplex tooling and direction-locked MCAP inspection.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxRunMessagePackDuplexToolingValidation
    {
        private const string Topic = "/phase185/messagepack/full-duplex";
        private static readonly byte[] RemoteA = { 0x82, 0xa1, 0x61, 0x01, 0xa1, 0x76, 0x29 };
        private static readonly byte[] LocalB = { 0x82, 0xa1, 0x61, 0x02, 0xa1, 0x76, 0x52 };
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun MessagePack duplex/tooling validation ---");
            _passed = 0;

            VerifyCatalogPanelAndProbeSources();
            VerifyInspectorSelectionFailures();
            VerifyMaintainedReaderMixedRecording();
            VerifyCliContract();

            Console.WriteLine("FoxRun MessagePack duplex/tooling: " + _passed + " checks passed.\n");
        }

        private static void VerifyCatalogPanelAndProbeSources()
        {
            var catalog = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSubscriptionCatalog.cs");
            var panel = Read("Tools/foxglove-extensions/foxrun-publish-panel/src/index.ts");
            var protocol = Read("Tools/foxglove-extensions/foxrun-publish-panel/src/protocol.ts");
            var codec = Read("Tools/foxglove-extensions/foxrun-publish-panel/src/msgpack.ts");
            var probe = ReadSplitPythonSource("Scripts/smoke/websocket/phase185_foxrun_messagepack_probe.py");

            Check(
                catalog.Contains("IsWebSocketEncoding", StringComparison.Ordinal)
                && catalog.Contains("\"msgpack\"", StringComparison.Ordinal)
                && catalog.Contains("wireSchemaName", StringComparison.Ordinal)
                && catalog.Contains("logicalSchemaName", StringComparison.Ordinal),
                "185D-1: catalog exposes typed MessagePack logical shape without inventing a wire schema");
            Check(
                panel.Contains("msgpack", StringComparison.OrdinalIgnoreCase)
                && protocol.Contains("DirectFoxRunProtocolClient", StringComparison.Ordinal)
                && codec.Contains("encodeMessagePackMessage", StringComparison.Ordinal)
                && codec.Contains("BigInt", StringComparison.Ordinal),
                "185D-2: maintained custom panel owns typed MessagePack authoring and exact wire lifecycle");
            Check(
                ContainsAll(
                    probe,
                    "CATALOG_SERVICE",
                    "unity2foxglove.direction",
                    "noImmediateMirror",
                    "canonicalOutput",
                    "remoteEcho"),
                "185D-3: independent probe locks catalog, no-mirror, and distinct later-local output evidence");
        }

        private static void VerifyInspectorSelectionFailures()
        {
            var input = Channel(1, 0, "msgpack", "input");
            var output = Channel(2, 0, "msgpack", "output");
            var selected = FoxRunMessagePackMcapInspector.SelectOutputChannel(
                new[] { input, output },
                Topic);
            Check(selected.Id == output.Id, "185D-4: exact output metadata wins over a same-topic input channel");

            ExpectInvalid(
                () => FoxRunMessagePackMcapInspector.SelectOutputChannel(
                    new[] { Channel(1, 0, "msgpack", null) },
                    Topic),
                "direction metadata");
            Check(true, "185D-5: missing direction metadata fails closed");

            ExpectInvalid(
                () => FoxRunMessagePackMcapInspector.SelectOutputChannel(
                    new[] { Channel(1, 0, "msgpack", "output"), Channel(2, 0, "msgpack", "output") },
                    Topic),
                "exactly one");
            Check(true, "185D-6: duplicate output channels fail closed");

            ExpectInvalid(
                () => FoxRunMessagePackMcapInspector.SelectOutputChannel(
                    new[] { Channel(1, 0, "msgpack", "output"), Channel(2, 0, "msgpack", "unknown") },
                    Topic),
                "ambiguous");
            Check(true, "185D-7: ambiguous same-topic direction candidates fail closed");

            ExpectInvalid(
                () => FoxRunMessagePackMcapInspector.SelectOutputChannel(
                    new[] { Channel(1, 7, "msgpack", "output") },
                    Topic),
                "schema id zero");
            Check(true, "185D-8: selected output with a non-zero schema id fails closed");

            ExpectInvalid(
                () => FoxRunMessagePackMcapInspector.SelectOutputChannel(
                    new[] { Channel(1, 0, "json", "output") },
                    Topic),
                "msgpack");
            Check(true, "185D-9: selected output with the wrong encoding fails closed");

            ExpectInvalid(
                () => FoxRunMessagePackMcapInspector.RequireCanonicalMessage(
                    new[] { new McapMessage { ChannelId = 2, Data = RemoteA } },
                    output.Id,
                    LocalB),
                "canonical");
            Check(true, "185D-10: inbound A bytes cannot satisfy distinct later-local B output");
        }

        private static void VerifyMaintainedReaderMixedRecording()
        {
            var root = FoxRunMessagePackPublicContractValidation.Root();
            var fixtureRoot = Path.Combine(root, "build", "phase185", "runtime-validation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureRoot);
            var mcapPath = Path.Combine(fixtureRoot, "mixed-direction.mcap");
            var expectedPath = Path.Combine(fixtureRoot, "probe-report.json");
            var outputPath = Path.Combine(fixtureRoot, "inspection.json");
            try
            {
                using (var stream = new FileStream(mcapPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream))
                {
                    recorder.AddChannel(1, Topic, "msgpack", "", "", "");
                    recorder.WriteMessage(1, 1_000, LocalB);
                    recorder.WriteClientMessage(185, 186, 900, RemoteA, Topic, "msgpack", "", "", "");
                    recorder.AddChannel(
                        2,
                        "/phase185/unrelated/json",
                        "json",
                        "phase185.Unrelated",
                        "jsonschema",
                        "{\"type\":\"object\"}");
                    recorder.WriteMessage(2, 1_100, new byte[] { (byte)'{', (byte)'}' });
                    recorder.Close();
                }

                File.WriteAllText(expectedPath, BuildExpectedReport());
                FoxRunMessagePackMcapInspector.InspectOrThrow(mcapPath, expectedPath, outputPath);
                var report = JObject.Parse(File.ReadAllText(outputPath));
                Check(
                    string.Equals((string)report["verdict"], "PASS", StringComparison.Ordinal)
                    && (int)report["selectedOutput"]["schemaId"] == 0
                    && (int)report["selectedOutput"]["matchingPayloadCount"] == 1
                    && (int)report["unrelatedSchemaCount"] == 1,
                    "185D-11: maintained MCAP reader selects output B and tolerates an unrelated JSON schema");
            }
            finally
            {
                if (Directory.Exists(fixtureRoot))
                    Directory.Delete(fixtureRoot, recursive: true);
            }
        }

        private static void VerifyCliContract()
        {
            var program = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            Check(
                ContainsAll(
                    program,
                    "--phase185-inspect-mcap",
                    "--expected-probe-report",
                    "--output",
                    "FoxRunMessagePackMcapInspector.RunCommand"),
                "185D-12: runtime tool exposes the fail-closed three-path MCAP inspector contract");
        }

        private static McapChannel Channel(ushort id, ushort schemaId, string encoding, string direction)
        {
            var channel = new McapChannel
            {
                Id = id,
                SchemaId = schemaId,
                Topic = Topic,
                MessageEncoding = encoding
            };
            if (direction != null)
                channel.Metadata[McapRecorder.DataDirectionMetadataKey] = direction;
            return channel;
        }

        private static string BuildExpectedReport()
        {
            var report = new JObject
            {
                ["version"] = 1,
                ["verdict"] = "PASS",
                ["selectedContract"] = new JObject
                {
                    ["topic"] = Topic,
                    ["messageEncoding"] = "msgpack",
                    ["schemaName"] = "",
                    ["wireSchemaName"] = ""
                },
                ["remoteInput"] = new JObject
                {
                    ["identity"] = "A",
                    ["payloadHex"] = ToHex(RemoteA)
                },
                ["canonicalOutput"] = new JObject
                {
                    ["identity"] = "B",
                    ["topic"] = Topic,
                    ["directionMetadataKey"] = McapRecorder.DataDirectionMetadataKey,
                    ["direction"] = "output",
                    ["messageEncoding"] = "msgpack",
                    ["schemaName"] = "",
                    ["expectedSchemaId"] = 0,
                    ["payloadHex"] = ToHex(LocalB),
                    ["count"] = 1,
                    ["remoteEcho"] = false
                }
            };
            return report.ToString(Formatting.None);
        }

        private static void ExpectInvalid(Action action, string expectedText)
        {
            try
            {
                action();
            }
            catch (InvalidDataException exception)
            {
                if (exception.Message.Contains(expectedText, StringComparison.OrdinalIgnoreCase))
                    return;
                throw new InvalidOperationException(
                    "Expected failure containing '" + expectedText + "', got: " + exception.Message,
                    exception);
            }
            throw new InvalidOperationException("Expected an InvalidDataException containing '" + expectedText + "'.");
        }

        private static string ToHex(byte[] bytes)
            => string.Concat(bytes.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));

        private static string Read(string path) => FoxRunMessagePackPublicContractValidation.Read(path);

        private static string ReadSplitPythonSource(string relativePath)
            => PhaseValidationSourceHelpers.ReadSplitPythonSource(relativePath);

        private static bool ContainsAll(string source, params string[] values)
            => FoxRunMessagePackPublicContractValidation.ContainsAll(source, values);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }

}
