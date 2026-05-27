// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-29 regression coverage for core smoke script hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_29Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-29: Core Smoke Scripts ===");
            _passed = 0;

            VerifyTfWebSocketSmokeDiscoversChannels();
            VerifyPointCloudProbesFailMalformedPayloads();
            VerifyFetchAssetFrameBounds();
            VerifyAttachmentMcapFixtureConstants();
            VerifySlowCameraAdvertiseParsing();
            VerifyTopicProbeExceptionOrdering();
            VerifyDotnetSmokeScriptsSurfaceFailures();

            Console.WriteLine($"Phase 134-29: {_passed} checks passed.");
        }

        private static void VerifyTfWebSocketSmokeDiscoversChannels()
        {
            var source = ReadRepoText("Scripts/smoke/tf_websocket_smoke.py");
            Check(source.Contains("DEFAULT_TOPIC = \"/tf\"", StringComparison.Ordinal)
                  && source.Contains("DEFAULT_ADVERTISE_TIMEOUT_SECONDS", StringComparison.Ordinal)
                  && source.Contains("wait_for_channel", StringComparison.Ordinal),
                "134-29A-1: TF websocket smoke discovers the /tf channel from advertise frames");
            Check(!source.Contains("TF_CHANNEL_ID", StringComparison.Ordinal)
                  && source.Contains("build_subscribe_payload(channel.channel_id", StringComparison.Ordinal),
                "134-29A-2: TF websocket smoke no longer hardcodes a channel id");
            Check(source.Contains("drain_for_seconds", StringComparison.Ordinal)
                  && !source.Contains("DEFAULT_STARTUP_FRAMES_TO_DRAIN", StringComparison.Ordinal),
                "134-29A-3: TF websocket smoke drains startup frames for a bounded interval");
            Check(source.Contains("def describe_message_payload(payload: bytes, encoding: str)", StringComparison.Ordinal)
                  && source.Contains("if encoding.lower() == \"json\"", StringComparison.Ordinal)
                  && source.Contains("payloadBytes={len(payload)}", StringComparison.Ordinal),
                "134-29A-4: TF websocket smoke describes non-JSON payloads without decoding them as JSON");
        }

        private static void VerifyPointCloudProbesFailMalformedPayloads()
        {
            var draco = ReadRepoText("Scripts/smoke/compressed_pointcloud_draco_probe.py");
            Check(draco.Contains("EXIT_DECODE_FAILURE = 6", StringComparison.Ordinal)
                  && draco.Contains("--allow-malformed-payloads", StringComparison.Ordinal),
                "134-29B-1: Draco probe exposes an explicit decode-failure exit path");
            Check(draco.Contains("elif not measurement.decoded_samples:", StringComparison.Ordinal)
                  && draco.Contains("measurement.malformed_payload_count > 0 and not args.allow_malformed_payloads", StringComparison.Ordinal)
                  && draco.Contains("exit_code = EXIT_DECODE_FAILURE", StringComparison.Ordinal),
                "134-29B-2: Draco probe fails when received payloads cannot be decoded");

            var qos = ReadRepoText("Scripts/smoke/pointcloud_qos_probe.py");
            Check(qos.Contains("EXIT_DECODE_FAILURE = 6", StringComparison.Ordinal)
                  && qos.Contains("result = \"DECODE_FAILURE\"", StringComparison.Ordinal)
                  && qos.Contains("exit_code = EXIT_DECODE_FAILURE", StringComparison.Ordinal),
                "134-29B-3: point cloud QoS probe fails instead of passing all-decode-failed runs");
        }

        private static void VerifyFetchAssetFrameBounds()
        {
            var source = ReadRepoText("Scripts/smoke/fetch_asset_smoke.py");
            Check(source.Contains("MIN_FETCH_ASSET_RESPONSE_BYTES = PAYLOAD_START", StringComparison.Ordinal)
                  && source.Contains("if len(data) < MIN_FETCH_ASSET_RESPONSE_BYTES:", StringComparison.Ordinal)
                  && source.Contains("FetchAsset response too short", StringComparison.Ordinal),
                "134-29C-1: fetch asset smoke bounds-checks binary response headers before parsing");
            Check(source.Contains("except ValueError as exc:", StringComparison.Ordinal)
                  && source.Contains("Invalid fetchAsset response", StringComparison.Ordinal),
                "134-29C-2: fetch asset smoke reports malformed responses without IndexError");
        }

        private static void VerifyAttachmentMcapFixtureConstants()
        {
            var source = ReadRepoText("Scripts/smoke/phase34_attachment_mcap.py");
            Check(source.Contains("MESSAGE_INDEX_RECORDS_BYTE_LENGTH", StringComparison.Ordinal)
                  && source.Contains("ZERO_CRC_SENTINEL", StringComparison.Ordinal)
                  && !source.Contains("MESSAGE_RECORD_OFFSET_IN_CHUNK", StringComparison.Ordinal)
                  && !source.Contains("NONZERO_CRC_SENTINEL", StringComparison.Ordinal)
                  && !source.Contains("SUMMARY_OFFSET_ENTRY_COUNT", StringComparison.Ordinal),
                "134-29D-1: attachment MCAP smoke uses semantic fixture constants");
            Check(Occurrences(source, "+ u16(CHANNEL_ID)") >= 2,
                "134-29D-2: attachment MCAP smoke keys chunk/statistics maps by channel id");
        }

        private static void VerifySlowCameraAdvertiseParsing()
        {
            var source = ReadRepoText("Scripts/smoke/phase40_slow_camera_client.py");
            Check(source.Contains("MAX_HANDSHAKE_RESPONSE_BYTES = 8192", StringComparison.Ordinal)
                  && source.Contains("Handshake response exceeded", StringComparison.Ordinal),
                "134-29E-1: slow camera client caps websocket handshake response bytes");
            Check(source.Contains("def find_channel_id_in_advertise_message(message: dict, topic: str)", StringComparison.Ordinal)
                  && source.Contains("message = json.loads(text)", StringComparison.Ordinal)
                  && source.Contains("find_channel_id_in_advertise_message(message, CAMERA_TOPIC)", StringComparison.Ordinal),
                "134-29E-2: slow camera client parses advertise JSON before using regex fallback");
        }

        private static void VerifyTopicProbeExceptionOrdering()
        {
            VerifyTopicNotFoundBeforeWait("Scripts/smoke/pointcloud_qos_probe.py");
            VerifyTopicNotFoundBeforeWait("Scripts/smoke/topic_rate_probe.py");
        }

        private static void VerifyTopicNotFoundBeforeWait(string relativePath)
        {
            var source = ReadRepoText(relativePath);
            var classIndex = source.IndexOf("class TopicNotFoundError", StringComparison.Ordinal);
            var waitIndex = source.IndexOf("async def wait_for_channel", StringComparison.Ordinal);
            Check(classIndex >= 0 && waitIndex >= 0 && classIndex < waitIndex,
                "134-29F: " + relativePath + " declares TopicNotFoundError before channel wait helpers");
        }

        private static void VerifyDotnetSmokeScriptsSurfaceFailures()
        {
            VerifyCapturedDotnetFailure("Scripts/smoke/phase44_all_schemas_mcap.py", "phase44");
            VerifyCapturedDotnetFailure("Scripts/smoke/phase68_indexed_reader_smoke.py", "phase68");
            VerifyCapturedDotnetFailure("Scripts/smoke/ros2_cdr_mcap_inspect.py", "ros2_cdr");
        }

        private static void VerifyCapturedDotnetFailure(string relativePath, string label)
        {
            var source = ReadRepoText(relativePath);
            Check(source.Contains("capture_output=True", StringComparison.Ordinal)
                  && source.Contains("text=True", StringComparison.Ordinal)
                  && source.Contains("result.stdout", StringComparison.Ordinal)
                  && source.Contains("result.stderr", StringComparison.Ordinal),
                "134-29G: " + label + " dotnet smoke prints captured stdout/stderr on failure");
        }

        private static int Occurrences(string source, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static string ReadRepoText(string relativePath)
        {
            var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Missing repository file: " + relativePath, fullPath);

            return File.ReadAllText(fullPath);
        }

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
