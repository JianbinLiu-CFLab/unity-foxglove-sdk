// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Guard Phase 134-34 mid-baseline validation fixes.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_34Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 134-34 Tests ---");
            _passCount = 0;

            var root = Phase16Validation.FindRepoRoot();
            var phase28 = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase28Validation.cs");
            var phase33 = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase33Validation.cs");
            var phase34 = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase34Validation.cs");
            var phase24d = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase24DValidation.cs");
            var phase41 = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase41Validation.cs");

            Check(phase28.Contains("GetFreeTcpPort()", StringComparison.Ordinal)
                  && !phase28.Contains("backend.Start(\"127.0.0.1\", 1879", StringComparison.Ordinal),
                "134-34A: Origin guard tests use dynamic loopback ports");
            Check(phase28.Contains("SendRawHandshake", StringComparison.Ordinal)
                  && phase28.Contains("HTTP/1.1 403", StringComparison.Ordinal)
                  && phase28.Contains("403 Forbidden", StringComparison.Ordinal),
                "134-34B: disallowed Origin test asserts the HTTP 403 response");
            Check(phase33.Contains("TestByteLimitOverflowDropsAndRejectsOversizedData", StringComparison.Ordinal)
                  && phase33.Contains("maxQueuedBytes: 4", StringComparison.Ordinal)
                  && phase33.Contains("DroppedDataFrames == 2", StringComparison.Ordinal),
                "134-34C: send queue validation covers byte-limit overflow");
            Check(phase34.Contains("McapWriter.MagicLength", StringComparison.Ordinal)
                  && phase34.Contains("McapWriter.RecordHeaderLength", StringComparison.Ordinal)
                  && phase34.Contains("McapWriter.FooterContentLength", StringComparison.Ordinal)
                  && !phase34.Contains("allBytes.Length - 8 - 9 - 20", StringComparison.Ordinal),
                "134-34D: MCAP footer offset tests use writer constants");
            Check(phase24d.Contains("MessageCount == 3", StringComparison.Ordinal)
                  && phase24d.Contains("ChannelMessageCounts.Values.Contains(1UL)", StringComparison.Ordinal)
                  && phase24d.Contains("ChannelMessageCounts.Values.Contains(2UL)", StringComparison.Ordinal),
                "134-34E: matching-schema MCAP test asserts message counts");
            Check(phase41.Contains("TestEmitterUsesConfiguredEpsilonInGeneratedSource", StringComparison.Ordinal)
                  && phase41.Contains("FoxRunChangeHelper.FloatChanged", StringComparison.Ordinal)
                  && phase41.Contains("this.Value", StringComparison.Ordinal)
                  && phase41.Contains("__last_0_0", StringComparison.Ordinal)
                  && phase41.Contains("0.100000001f", StringComparison.Ordinal),
                "134-34F: generated source test covers configured epsilon");

            Console.WriteLine($"Phase 134-34: {_passCount} checks passed.");
        }

        private static string Read(string root, string relativePath)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException($"Required validation source missing: {path}", path);
            return File.ReadAllText(path);
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                _passCount++;
                Console.WriteLine($"[PASS] {label}");
                return;
            }

            throw new Exception($"[FAIL] {label}");
        }
    }
}
