// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-79 source-shape regression coverage for core smoke script optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_79Validation.
    /// </summary>
    public static class Phase140_79Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-79: Core Smoke Scripts Optimization ===");
            _passed = 0;

            VerifyHandshakeReadsBoundedChunks();
            VerifyCollectMessagesAvoidsBytesFrameCopy();
            VerifyCollectAdvertisementsMaintainsRunningTopicSet();
            VerifyDeferredSmokeMicroOptimizationsRemainDeferred();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-79: {_passed} checks passed.");
        }

        private static void VerifyHandshakeReadsBoundedChunks()
        {
            var source = Read("Scripts/smoke/phase40_slow_camera_client.py");
            var method = Slice(source, "def read_handshake_response", "def build_websocket_upgrade_request");
            Check(source.Contains("HANDSHAKE_READ_CHUNK_BYTES = 256", StringComparison.Ordinal)
                  && method.Contains("to_read = min(HANDSHAKE_READ_CHUNK_BYTES, MAX_HANDSHAKE_RESPONSE_BYTES - len(response))", StringComparison.Ordinal)
                  && method.Contains("chunk = sock.recv(to_read)", StringComparison.Ordinal)
                  && method.Contains("response.extend(chunk)", StringComparison.Ordinal)
                  && !method.Contains("sock.recv(HANDSHAKE_READ_BYTES)", StringComparison.Ordinal),
                "140-79A-1: WebSocket handshake response is read in bounded chunks");
        }

        private static void VerifyCollectMessagesAvoidsBytesFrameCopy()
        {
            var source = Read("Scripts/smoke/phase139_e2e_integration_smoke.py");
            var method = Slice(source, "async def collect_messages", "def summarize_observed");
            Check(method.Contains("data = frame if isinstance(frame, bytes) else bytes(frame)", StringComparison.Ordinal)
                  && !method.Contains("data = bytes(frame)", StringComparison.Ordinal),
                "140-79B-1: E2E smoke avoids copying binary frames that are already bytes");
        }

        private static void VerifyCollectAdvertisementsMaintainsRunningTopicSet()
        {
            var source = Read("Scripts/smoke/phase139_e2e_integration_smoke.py");
            var method = Slice(source, "async def collect_advertisements", "async def collect_messages");
            Check(method.Contains("advertised_topics: set[str] = set()", StringComparison.Ordinal)
                  && method.Contains("advertised_topics.add(channel.get(\"topic\"))", StringComparison.Ordinal)
                  && !method.Contains("advertised_topics = {channel.get(\"topic\") for channel in channels.values()}", StringComparison.Ordinal),
                "140-79C-1: E2E smoke maintains advertised topics incrementally");
        }

        private static void VerifyDeferredSmokeMicroOptimizationsRemainDeferred()
        {
            var phase68 = Read("Scripts/smoke/phase68_indexed_reader_smoke.py");
            var topicRate = Read("Scripts/smoke/topic_rate_probe.py");
            Check(phase68.Contains("sorted(matches, key=lambda path: path.stat().st_mtime", StringComparison.Ordinal)
                  && topicRate.Contains("ordered = sorted(values)", StringComparison.Ordinal)
                  && topicRate.Contains("p50={percentile(values, 50):.2f}", StringComparison.Ordinal),
                "140-79D-1: low-value stat and percentile smoke micro-optimizations remain deferred");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_79Validation.cs", StringComparison.Ordinal),
                "140-79E-1: test project compiles Phase140_79Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-79\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_79Validation.Validate", StringComparison.Ordinal),
                "140-79E-2: validation registry exposes --phase140-79");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static string Slice(string source, string startText, string endText)
        {
            var start = source.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Could not locate source slice start: " + startText);
            var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;
            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
