// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Guards Phase175B direct FoxRun dual-codec generation and client encoding routing.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase175BValidation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 175B Tests ---");
            _passCount = 0;

            VerifyGeneratedProtobufBranches();
            VerifyClientAdvertiseEncodingReachesInboundRouter();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 175B: " + _passCount + " checks passed.\n");
        }

        private static void VerifyGeneratedProtobufBranches()
        {
            var publish = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/ProtobufPublishDispatchEmitter.cs");
            var inbound = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/ProtobufInputDispatchEmitter.cs");

            Check(publish.Contains("FoxRunProtobufWire.", StringComparison.Ordinal)
                  && inbound.Contains("TryRead", StringComparison.Ordinal),
                "175B-1: generated FoxRun Protobuf branches use direct wire helpers");
        }

        private static void VerifyClientAdvertiseEncodingReachesInboundRouter()
        {
            var handler = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionClientPublishHandler.cs");
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var events = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ClientEvents.cs");
            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveInputHub.cs");

            Check(handler.Contains("ch.Encoding, payload", StringComparison.Ordinal)
                  && session.Contains("OnClientMessageWithEncoding", StringComparison.Ordinal)
                  && events.Contains("evt.Encoding", StringComparison.Ordinal)
                  && hub.Contains("string encoding, byte[] payload", StringComparison.Ordinal)
                  && hub.Contains("encoding,", StringComparison.Ordinal),
                "175B-2: client-advertised encoding crosses the session queue into the FoxRun router");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase175b"),
                "175B-3: validation registry exposes the dual-codec flag");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
