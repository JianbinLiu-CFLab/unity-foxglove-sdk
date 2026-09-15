using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas.MsgPack;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class ComponentMessagePackMcapInspectorValidation
    {
        private static readonly string[] Topics =
        {
            "/phase189/component/scalar",
            "/phase189/component/nested",
            "/phase189/component/jpeg",
            "/phase189/component/pointcloud"
        };

        public static void ValidateIndependentFourTopicInspection()
        {
            var root = FoxRunMessagePackPublicContractValidation.Root();
            const string run = "phase189-inspector-fixture";
            var dir = Path.Combine(root, "build", "phase189", "inspector-red-green-" + run, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var mcapPath = Path.Combine(dir, "final.mcap");
            var reportPath = Path.Combine(dir, "probe.json");
            var outputPath = Path.Combine(dir, "inspection.json");
            const string head = "0123456789abcdef0123456789abcdef01234567";
            const string generation = "3";
            try
            {
                var payloads = new[]
                {
                    MapPayload("value", 7, "label", "scalar"),
                    MapPayload("sequence", 11, "nested", MapPayload("enabled", true, "label", "nested")),
                    BinaryPayload(new byte[] { 0xff, 0xd8, 0xff, 0xd9 }),
                    BinaryPayload(new byte[] { 0x01, 0x02, 0x03, 0x04 })
                };
                using (var stream = new FileStream(mcapPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream))
                {
                    for (var i = 0; i < Topics.Length; i++)
                    {
                        recorder.AddChannel((uint)(i + 1), Topics[i], "msgpack", string.Empty, string.Empty, string.Empty);
                        recorder.WriteMessage((uint)(i + 1), (ulong)(1000 + i), payloads[i]);
                    }
                    recorder.Close();
                }

                var topics = new JObject();
                for (var i = 0; i < Topics.Length; i++)
                {
                    topics[Topics[i]] = new JObject
                    {
                        ["effectiveEncoding"] = "msgpack",
                        ["wireSchema"] = "schemaless",
                        ["shapeIdentity"] = "shape-" + i,
                        ["binaryMember"] = i >= 2 ? "data" : null,
                        ["payloadHex"] = ToHex(payloads[i])
                    };
                }
                var report = new JObject
                {
                    ["version"] = 1,
                    ["verdict"] = "PASS",
                    ["finalMessagePack"] = new JObject
                    {
                        ["runId"] = run,
                        ["head"] = head,
                        ["generation"] = generation,
                        ["topics"] = topics
                    },
                    ["recordingClose"] = new JObject
                    {
                        ["closed"] = true,
                        ["runId"] = run,
                        ["head"] = head,
                        ["generation"] = generation,
                        ["path"] = mcapPath
                    },
                    ["playExit"] = new JObject
                    {
                        ["marker"] = "EDIT_MODE",
                        ["runId"] = run,
                        ["head"] = head,
                        ["generation"] = generation
                    },
                    ["observations"] = new JObject { ["noJsonFallback"] = true }
                };
                File.WriteAllText(reportPath, report.ToString(Formatting.Indented));
                ComponentMessagePackMcapInspector.InspectOrThrow(
                    mcapPath, reportPath, run, head, generation, outputPath);
                var artifact = JObject.Parse(File.ReadAllText(outputPath));
                if (!string.Equals((string)artifact["verdict"], "PASS", StringComparison.Ordinal))
                    throw new InvalidOperationException("Component inspector did not produce PASS.");
                if ((int?)artifact["topicCount"] != 4)
                    throw new InvalidOperationException("Component inspector did not inspect all four topics.");

                var stale = (JObject)report.DeepClone();
                stale["recordingClose"]["path"] = Path.Combine(dir, "another-run.mcap");
                File.WriteAllText(reportPath, stale.ToString(Formatting.Indented));
                ExpectInspectorFailure(mcapPath, reportPath, run, head, generation, outputPath, "recording-close path");

                var malformed = (JObject)report.DeepClone();
                malformed["finalMessagePack"]["topics"][Topics[0]]["payloadHex"] = "00";
                File.WriteAllText(reportPath, malformed.ToString(Formatting.Indented));
                ExpectInspectorFailure(mcapPath, reportPath, run, head, generation, outputPath, "payload");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        private static byte[] MapPayload(params object[] values)
        {
            using (var writer = new FoxgloveMsgPackWriter())
            {
                writer.WriteMapHeader(values.Length / 2);
                for (var i = 0; i < values.Length; i += 2)
                {
                    writer.WriteString((string)values[i]);
                    switch (values[i + 1])
                    {
                        case int integer: writer.WriteInt32(integer); break;
                        case bool boolean: writer.WriteBool(boolean); break;
                        case string text: writer.WriteString(text); break;
                        case byte[] bytes: writer.WriteBinary(bytes); break;
                        default:
                            throw new InvalidOperationException("Unsupported fixture value: " + values[i + 1]?.GetType());
                    }
                }
                return writer.ToArray();
            }
        }

        private static byte[] BinaryPayload(byte[] data) => MapPayload("data", data);

        private static string ToHex(byte[] bytes)
            => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

        private static void ExpectInspectorFailure(
            string mcapPath,
            string reportPath,
            string run,
            string head,
            string generation,
            string outputPath,
            string expected)
        {
            try
            {
                ComponentMessagePackMcapInspector.InspectOrThrow(
                    mcapPath, reportPath, run, head, generation, outputPath);
            }
            catch (InvalidDataException exception)
            {
                if (exception.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
                throw new InvalidOperationException(
                    "Inspector failure did not identify the expected control: " + exception.Message,
                    exception);
            }
            throw new InvalidOperationException("Inspector accepted a negative control for " + expected + ".");
        }
    }
}
