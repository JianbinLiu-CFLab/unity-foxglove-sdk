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
