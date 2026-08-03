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
            var probe = Read("Scripts/smoke/websocket/phase185_foxrun_messagepack_probe.py");

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

    internal static class FoxRunMessagePackMcapInspector
    {
        private const long MaxExpectedReportBytes = 1024 * 1024;
        private const int MaxMessages = 10_000;
        private const long MaxPayloadBytes = 16L * 1024L * 1024L;

        internal static int RunCommand(string mcapPath, string expectedProbeReportPath, string outputPath)
        {
            try
            {
                InspectOrThrow(mcapPath, expectedProbeReportPath, outputPath);
                Console.WriteLine("PHASE185_MESSAGEPACK_MCAP_INSPECTOR_PASS " + outputPath);
                return 0;
            }
            catch (Exception exception)
            {
                TryWriteFailure(outputPath, exception.Message);
                Console.Error.WriteLine("PHASE185_MESSAGEPACK_MCAP_INSPECTOR_FAIL " + exception.Message);
                return 1;
            }
        }

        internal static void InspectOrThrow(
            string mcapPath,
            string expectedProbeReportPath,
            string outputPath)
        {
            RequireInputFile(mcapPath, "MCAP");
            RequireInputFile(expectedProbeReportPath, "probe report");
            if (new FileInfo(expectedProbeReportPath).Length > MaxExpectedReportBytes)
                throw new InvalidDataException("Probe report exceeds the bounded one MiB limit.");
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Inspector output path is required.", nameof(outputPath));

            var expected = JObject.Parse(File.ReadAllText(expectedProbeReportPath));
            var expectation = ReadExpectation(expected);

            McapFileSummary summary;
            List<McapMessage> messages;
            using (var stream = new FileStream(mcapPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new McapReader(stream))
            {
                summary = reader.ReadSummary();
                var limits = new McapSequentialReadLimits
                {
                    MaxMessages = MaxMessages,
                    MaxPayloadBytes = MaxPayloadBytes
                };
                limits.Validate();
                messages = reader.ReadSequentialMessages(
                    summary.DataSectionEndOffset,
                    sequentialLimits: limits);
            }

            var outputChannel = SelectOutputChannel(summary.Channels, expectation.Topic);
            var matchingCount = RequireCanonicalMessage(messages, outputChannel.Id, expectation.Payload);
            var artifact = new JObject
            {
                ["version"] = 1,
                ["verdict"] = "PASS",
                ["selectedOutput"] = new JObject
                {
                    ["topic"] = expectation.Topic,
                    ["direction"] = "output",
                    ["messageEncoding"] = outputChannel.MessageEncoding,
                    ["schemaId"] = outputChannel.SchemaId,
                    ["channelId"] = outputChannel.Id,
                    ["payloadHex"] = ToHex(expectation.Payload),
                    ["matchingPayloadCount"] = matchingCount
                },
                ["sameTopicInputChannelCount"] = summary.Channels.Count(
                    channel => string.Equals(channel.Topic, expectation.Topic, StringComparison.Ordinal)
                               && Direction(channel) == "input"),
                ["unrelatedSchemaCount"] = summary.Schemas.Count
            };
            WriteArtifact(outputPath, artifact);
        }

        internal static McapChannel SelectOutputChannel(
            IEnumerable<McapChannel> channels,
            string topic)
        {
            if (channels == null)
                throw new ArgumentNullException(nameof(channels));
            if (string.IsNullOrEmpty(topic))
                throw new InvalidDataException("Probe-selected topic is missing.");

            var topicChannels = channels
                .Where(channel => string.Equals(channel.Topic, topic, StringComparison.Ordinal))
                .ToArray();
            if (topicChannels.Length == 0)
                throw new InvalidDataException("No MCAP channel matches the probe-selected topic.");

            foreach (var channel in topicChannels)
            {
                if (!channel.Metadata.TryGetValue(McapRecorder.DataDirectionMetadataKey, out var direction)
                    || string.IsNullOrWhiteSpace(direction))
                {
                    throw new InvalidDataException("Same-topic MCAP channel is missing direction metadata.");
                }
                if (!string.Equals(direction, "input", StringComparison.Ordinal)
                    && !string.Equals(direction, "output", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Same-topic MCAP channel has ambiguous direction metadata.");
                }
            }

            var outputs = topicChannels
                .Where(channel => Direction(channel) == "output")
                .ToArray();
            if (outputs.Length != 1)
                throw new InvalidDataException("Expected exactly one direction-locked output MCAP channel.");

            var selected = outputs[0];
            if (!string.Equals(selected.MessageEncoding, "msgpack", StringComparison.Ordinal))
                throw new InvalidDataException("Selected output channel message encoding is not msgpack.");
            if (selected.SchemaId != 0)
                throw new InvalidDataException("Selected schemaless MessagePack output must have schema id zero.");
            return selected;
        }

        internal static int RequireCanonicalMessage(
            IEnumerable<McapMessage> messages,
            ushort outputChannelId,
            byte[] expectedPayload)
        {
            if (messages == null)
                throw new ArgumentNullException(nameof(messages));
            if (expectedPayload == null || expectedPayload.Length == 0)
                throw new InvalidDataException("Canonical output payload is missing.");

            var outputMessages = messages
                .Where(message => message.ChannelId == outputChannelId)
                .ToArray();
            var matches = outputMessages.Count(
                message => message.Data != null && message.Data.SequenceEqual(expectedPayload));
            if (matches != 1)
                throw new InvalidDataException(
                    "Expected exactly one canonical later-local B payload on the selected output channel.");
            return matches;
        }

        private static ExpectedOutput ReadExpectation(JObject report)
        {
            if (!string.Equals((string)report["verdict"], "PASS", StringComparison.Ordinal))
                throw new InvalidDataException("Expected probe report does not have terminal PASS.");

            var selected = report["selectedContract"] as JObject
                           ?? throw new InvalidDataException("Probe report selectedContract is missing.");
            var canonical = report["canonicalOutput"] as JObject
                            ?? throw new InvalidDataException("Probe report canonicalOutput is missing.");
            var remote = report["remoteInput"] as JObject
                         ?? throw new InvalidDataException("Probe report remoteInput is missing.");
            var topic = (string)selected["topic"];
            var canonicalTopic = (string)canonical["topic"];
            var payloadHex = (string)canonical["payloadHex"];
            var remoteHex = (string)remote["payloadHex"];

            if (string.IsNullOrEmpty(topic)
                || !string.Equals(topic, canonicalTopic, StringComparison.Ordinal)
                || !string.Equals((string)selected["messageEncoding"], "msgpack", StringComparison.Ordinal)
                || !string.Equals((string)canonical["messageEncoding"], "msgpack", StringComparison.Ordinal)
                || !string.IsNullOrEmpty((string)selected["schemaName"])
                || !string.IsNullOrEmpty((string)selected["wireSchemaName"])
                || !string.IsNullOrEmpty((string)canonical["schemaName"])
                || !string.Equals(
                    (string)canonical["directionMetadataKey"],
                    McapRecorder.DataDirectionMetadataKey,
                    StringComparison.Ordinal)
                || !string.Equals((string)canonical["direction"], "output", StringComparison.Ordinal)
                || (int?)canonical["expectedSchemaId"] != 0
                || (int?)canonical["count"] != 1
                || (bool?)canonical["remoteEcho"] != false)
            {
                throw new InvalidDataException("Probe report output identity is incomplete or not direction locked.");
            }
            if (string.IsNullOrEmpty(payloadHex)
                || !IsEvenHex(payloadHex)
                || string.Equals(payloadHex, remoteHex, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Canonical later-local B payload is missing or mirrors inbound A.");
            }

            return new ExpectedOutput(topic, FromHex(payloadHex));
        }

        private static string Direction(McapChannel channel)
            => channel.Metadata.TryGetValue(McapRecorder.DataDirectionMetadataKey, out var value)
                ? value
                : string.Empty;

        private static bool IsEvenHex(string value)
        {
            if ((value.Length & 1) != 0)
                return false;
            foreach (var character in value)
                if (!Uri.IsHexDigit(character))
                    return false;
            return true;
        }

        private static byte[] FromHex(string value)
        {
            var bytes = new byte[value.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(
                    value.Substring(i * 2, 2),
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture);
            return bytes;
        }

        private static string ToHex(byte[] bytes)
            => string.Concat(bytes.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));

        private static void RequireInputFile(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Required " + label + " file was not found.", path);
        }

        private static void TryWriteFailure(string outputPath, string reason)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(outputPath))
                    return;
                WriteArtifact(
                    outputPath,
                    new JObject
                    {
                        ["version"] = 1,
                        ["verdict"] = "FAIL",
                        ["reason"] = reason ?? "Unknown inspector failure."
                    });
            }
            catch
            {
                // The original failure remains authoritative when failure-artifact writing also fails.
            }
        }

        private static void WriteArtifact(string path, JObject artifact)
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetFullPath(FoxRunMessagePackPublicContractValidation.Root());
            var buildRoot = Path.Combine(root, "build") + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(buildRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Phase185 inspector output must remain below the repository build directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
                                      ?? throw new InvalidOperationException("Inspector output directory is missing."));
            File.WriteAllText(fullPath, artifact.ToString(Formatting.Indented) + Environment.NewLine);
        }

        private sealed class ExpectedOutput
        {
            public ExpectedOutput(string topic, byte[] payload)
            {
                Topic = topic;
                Payload = payload;
            }

            public string Topic { get; }
            public byte[] Payload { get; }
        }
    }
}
