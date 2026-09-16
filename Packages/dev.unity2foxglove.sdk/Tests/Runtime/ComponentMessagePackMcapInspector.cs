// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Strict Phase189 MCAP identity and MessagePack payload inspector.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas.MsgPack;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Extends the Phase185 inspector with exact Phase189 run identity checks.</summary>
    internal static class ComponentMessagePackMcapInspector
    {
        private const long MaxProbeReportBytes = 1024 * 1024;

        internal static int RunCommand(
            string mcapPath,
            string expectedProbeReportPath,
            string expectedRun,
            string expectedHead,
            string expectedGeneration,
            string outputPath)
        {
            try
            {
                InspectOrThrow(mcapPath, expectedProbeReportPath, expectedRun, expectedHead, expectedGeneration, outputPath);
                Console.WriteLine("PHASE189_COMPONENT_MESSAGEPACK_MCAP_INSPECTOR_PASS " + outputPath);
                return 0;
            }
            catch (Exception exception)
            {
                TryWriteFailure(outputPath, exception.Message);
                Console.Error.WriteLine("PHASE189_COMPONENT_MESSAGEPACK_MCAP_INSPECTOR_FAIL " + exception.Message);
                return 1;
            }
        }

        internal static void InspectOrThrow(
            string mcapPath,
            string expectedProbeReportPath,
            string expectedRun,
            string expectedHead,
            string expectedGeneration,
            string outputPath)
        {
            RequireInput(mcapPath, "MCAP");
            RequireInput(expectedProbeReportPath, "probe report");
            if (new FileInfo(expectedProbeReportPath).Length > MaxProbeReportBytes)
                throw new InvalidDataException("Probe report exceeds the bounded one MiB limit.");
            if (string.IsNullOrWhiteSpace(expectedRun) || string.IsNullOrWhiteSpace(expectedHead) || string.IsNullOrWhiteSpace(expectedGeneration))
                throw new InvalidDataException("expectedRun, expectedHead, and expectedGeneration are required.");

            var report = JObject.Parse(File.ReadAllText(expectedProbeReportPath));
            RequireIdentity(report, expectedRun, expectedHead, expectedGeneration);
            RequireLifecycleEvidence(report, expectedRun, expectedHead, expectedGeneration);
            var fullMcapPath = Path.GetFullPath(mcapPath);
            if (!fullMcapPath.Contains(expectedRun, StringComparison.Ordinal))
                throw new InvalidDataException("MCAP path is not bound to the expected run; stale artifacts are rejected.");
            var closePath = (report["recordingClose"] as JObject)?.Value<string>("path");
            if (string.IsNullOrWhiteSpace(closePath)
                || !string.Equals(Path.GetFullPath(closePath), fullMcapPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Recording-close path does not identify the inspected MCAP.");

            var final = report["finalMessagePack"] as JObject
                        ?? throw new InvalidDataException("Probe report has no finalMessagePack segment.");
            var topics = final["topics"] as JObject
                         ?? throw new InvalidDataException("Probe report finalMessagePack topics are missing.");

            McapFileSummary summary;
            System.Collections.Generic.List<McapMessage> messages;
            using (var stream = new FileStream(mcapPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new McapReader(stream))
            {
                summary = reader.ReadSummary();
                messages = reader.ReadSequentialMessages(
                    summary.DataSectionEndOffset,
                    sequentialLimits: new McapSequentialReadLimits
                    {
                        MaxMessages = 10_000,
                        MaxPayloadBytes = 16L * 1024L * 1024L
                    });
            }

            var selected = new JArray();
            foreach (var topic in RequiredTopics)
            {
                var contract = topics[topic] as JObject
                                ?? throw new InvalidDataException("Required Phase189 topic is missing: " + topic);
                var expectedPayload = FromHex(contract.Value<string>("payloadHex"));
                ValidateMessagePackPayload(expectedPayload, topic, contract.Value<string>("binaryMember"));

                var channels = summary.Channels.Where(channel => string.Equals(channel.Topic, topic, StringComparison.Ordinal)).ToArray();
                if (channels.Length != 1)
                    throw new InvalidDataException("Expected exactly one MCAP channel for " + topic + ".");
                var channel = channels[0];
                if (!string.Equals(channel.MessageEncoding, "msgpack", StringComparison.Ordinal))
                    throw new InvalidDataException(topic + " MCAP channel encoding is not msgpack.");
                if (channel.SchemaId != 0)
                    throw new InvalidDataException(topic + " MCAP channel schema id must be zero.");
                if (summary.Schemas.Any(schema => schema.Id == channel.SchemaId && schema.Id != 0))
                    throw new InvalidDataException(topic + " has an associated MessagePack schema record.");

                var topicMessages = messages.Where(message => message.ChannelId == channel.Id).ToArray();
                if (topicMessages.Length != 1 || topicMessages[0].Data == null || !topicMessages[0].Data.SequenceEqual(expectedPayload))
                    throw new InvalidDataException(topic + " payload bytes do not exactly match the independent probe report.");
                selected.Add(new JObject
                {
                    ["topic"] = topic,
                    ["channelId"] = channel.Id,
                    ["messageEncoding"] = channel.MessageEncoding,
                    ["schemaId"] = channel.SchemaId,
                    ["payloadHex"] = ToHex(expectedPayload),
                    ["payloadSha256"] = Convert.ToHexString(SHA256.HashData(expectedPayload)).ToLowerInvariant()
                });
            }

            var artifact = new JObject
            {
                ["version"] = 1,
                ["verdict"] = "PASS",
                ["runId"] = expectedRun,
                ["head"] = expectedHead,
                ["generation"] = expectedGeneration,
                ["topicCount"] = selected.Count,
                ["selectedOutputs"] = selected
            };
            WriteArtifact(outputPath, artifact);
        }

        private static readonly string[] RequiredTopics =
        {
            "/phase189/component/scalar",
            "/phase189/component/nested",
            "/phase189/component/jpeg",
            "/phase189/component/pointcloud"
        };

        private static void ValidateMessagePackPayload(byte[] payload, string topic, string binaryMember)
        {
            if (payload == null || payload.Length == 0)
                throw new InvalidDataException(topic + " payload identity is empty.");
            var reader = new FoxgloveMsgPackReader(
                payload,
                FoxgloveMsgPackReadLimits.ForPayloadBytes(Math.Max(payload.Length, 64)));
            if (!reader.TryReadMapHeader(out var count))
                throw new InvalidDataException(topic + " payload is not a MessagePack map: " + reader.Error);
            var foundBinary = false;
            for (var index = 0; index < count; index++)
            {
                if (!reader.TryReadString(out var key))
                    throw new InvalidDataException(topic + " payload map key is invalid: " + reader.Error);
                if (string.Equals(key, binaryMember, StringComparison.Ordinal) && !string.IsNullOrEmpty(binaryMember))
                {
                    if (!reader.TryReadBinary(out _))
                        throw new InvalidDataException(topic + " binary member is not MessagePack bin: " + reader.Error);
                    foundBinary = true;
                }
                else if (!reader.TrySkipValue())
                {
                    throw new InvalidDataException(topic + " payload value is invalid: " + reader.Error);
                }
            }
            if (reader.HasError || reader.RemainingBytes != 0)
                throw new InvalidDataException(topic + " payload has trailing or malformed MessagePack bytes.");
            if (!string.IsNullOrEmpty(binaryMember) && !foundBinary)
                throw new InvalidDataException(topic + " binary member is missing.");
        }

        private static byte[] FromHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || (value.Length & 1) != 0 || value.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("Probe payloadHex is missing or malformed.");
            var bytes = new byte[value.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
                bytes[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            return bytes;
        }

        private static string ToHex(byte[] bytes)
            => Convert.ToHexString(bytes ?? Array.Empty<byte>()).ToLowerInvariant();

        private static void RequireIdentity(JObject report, string expectedRun, string expectedHead, string expectedGeneration)
        {
            if (!string.Equals(report.Value<string>("verdict"), "PASS", StringComparison.Ordinal))
                throw new InvalidDataException("Probe report is not terminal PASS.");
            var segments = report["finalMessagePack"] as JObject ?? report["finalGeneration"] as JObject;
            if (segments == null)
                throw new InvalidDataException("Probe report has no finalMessagePack identity segment.");
            RequireEqual(segments.Value<string>("runId"), expectedRun, "run ID");
            RequireEqual(segments.Value<string>("head"), expectedHead, "HEAD");
            RequireEqual(segments.Value<string>("generation"), expectedGeneration, "Manager generation");
            var topics = segments["topics"] as JObject;
            if (topics == null || topics.Count != 4)
                throw new InvalidDataException("Final MessagePack segment must contain exactly four Phase189 topics.");
            var required = new[] { "/phase189/component/scalar", "/phase189/component/nested", "/phase189/component/jpeg", "/phase189/component/pointcloud" };
            foreach (var name in required)
                if (topics[name] == null) throw new InvalidDataException("Required Phase189 topic is missing: " + name);
            foreach (var topic in topics.Properties())
            {
                var contract = topic.Value as JObject;
                RequireEqual(contract?.Value<string>("effectiveEncoding"), "msgpack", topic.Name + " encoding");
                RequireEqual(contract?.Value<string>("wireSchema"), "schemaless", topic.Name + " wire schema");
                if (string.IsNullOrWhiteSpace(contract?.Value<string>("shapeIdentity")))
                    throw new InvalidDataException(topic.Name + " shape identity is missing.");
                if ((topic.Name.EndsWith("/jpeg", StringComparison.Ordinal) || topic.Name.EndsWith("/pointcloud", StringComparison.Ordinal))
                    && !string.Equals(contract?.Value<string>("binaryMember"), "data", StringComparison.Ordinal))
                    throw new InvalidDataException(topic.Name + " must identify its binary data member.");
                if (string.IsNullOrWhiteSpace(contract?.Value<string>("payloadHex")))
                    throw new InvalidDataException(topic.Name + " payload identity is missing.");
            }
        }

        private static void RequireLifecycleEvidence(JObject report, string expectedRun, string expectedHead, string expectedGeneration)
        {
            var close = report["recordingClose"] as JObject;
            if (close == null || close.Value<bool?>("closed") != true)
                throw new InvalidDataException("Recording-close evidence is missing or not closed.");
            RequireEqual(close.Value<string>("runId"), expectedRun, "recording run ID");
            RequireEqual(close.Value<string>("head"), expectedHead, "recording HEAD");
            RequireEqual(close.Value<string>("generation"), expectedGeneration, "recording generation");
            var exit = report["playExit"] as JObject;
            if (exit == null || !string.Equals(exit.Value<string>("marker"), "EDIT_MODE", StringComparison.Ordinal))
                throw new InvalidDataException("Play-exit evidence is missing or not EDIT_MODE.");
            RequireEqual(exit.Value<string>("runId"), expectedRun, "Play-exit run ID");
            RequireEqual(exit.Value<string>("head"), expectedHead, "Play-exit HEAD");
            if (report["observations"]?.Value<bool?>("noJsonFallback") != true)
                throw new InvalidDataException("Probe did not prove that JSON fallback was absent.");
        }

        private static void RequireEqual(string actual, string expected, string label)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidDataException("Probe report " + label + " does not match the expected final run.");
        }

        private static void RequireInput(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Required " + label + " file was not found.", path);
        }

        private static void WriteArtifact(string path, JObject artifact)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Inspector output path is required.", nameof(path));
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(Directory.GetCurrentDirectory());
            if (!full.StartsWith(Path.Combine(root, "build") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Phase189 inspector output must remain below the repository build directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? root);
            File.WriteAllText(full, artifact.ToString(Formatting.Indented) + Environment.NewLine);
        }

        private static void TryWriteFailure(string path, string reason)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                WriteArtifact(path, new JObject { ["version"] = 1, ["verdict"] = "FAIL", ["reason"] = reason ?? "unknown" });
            }
            catch { }
        }
    }
}
