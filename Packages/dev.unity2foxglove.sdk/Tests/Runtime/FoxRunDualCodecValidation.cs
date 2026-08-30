// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Guards Phase175B direct FoxRun dual-codec generation and client encoding routing.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase175BValidation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 175B Tests ---");
            _passCount = 0;

            VerifyGeneratedJsonContractDomain();
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

        private static void VerifyGeneratedJsonContractDomain()
        {
            var overflowAccepted = FoxRunInboundJson.TryRead(
                Encoding.UTF8.GetBytes("{\"value\":1e400}"),
                "value",
                out double overflowValue,
                out var overflowError);
            Check(
                !overflowAccepted && overflowValue == 0d,
                "187-E03-001-1: generated JSON rejects non-finite floating input (" + overflowError + ")");

            var scalarExtraAccepted = FoxRunInboundJson.TryRead(
                Encoding.UTF8.GetBytes("{\"value\":1,\"unexpected\":2}"),
                "value",
                out double scalarValue,
                out var scalarExtraError);
            Check(
                !scalarExtraAccepted && scalarValue == 0d,
                "187-E03-001-2: generated JSON rejects undeclared scalar envelope properties ("
                    + scalarExtraError + ")");

            var rootExtraAccepted = FoxRunInboundJson.TryReadObject(
                Encoding.UTF8.GetBytes(
                    "{\"state\":{\"Reading\":1.25,\"Ratio\":2.5},\"unexpected\":2}"),
                "state",
                out GeneratedFloatingProbe _,
                out var rootExtraError);
            Check(
                !rootExtraAccepted
                    && rootExtraError.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0,
                "187-E03-001-3: generated JSON rejects undeclared object envelope properties (" + rootExtraError + ")");

            var nestedExtraAccepted = FoxRunInboundJson.TryReadObject(
                Encoding.UTF8.GetBytes(
                    "{\"state\":{\"Reading\":1.25,\"Ratio\":2.5,\"unexpected\":2}}"),
                "state",
                out GeneratedFloatingProbe _,
                out var nestedExtraError);
            Check(
                !nestedExtraAccepted,
                "187-E03-001-4: generated JSON rejects undeclared DTO properties (" + nestedExtraError + ")");

            var nonFiniteStringAccepted = FoxRunInboundJson.TryReadObject(
                Encoding.UTF8.GetBytes("{\"state\":{\"Reading\":\"NaN\",\"Ratio\":\"Infinity\"}}"),
                "state",
                out GeneratedFloatingProbe _,
                out var nonFiniteStringError);
            Check(
                !nonFiniteStringAccepted,
                "187-E03-001-5: generated JSON rejects non-finite floating strings ("
                    + nonFiniteStringError + ")");

            var vectorExtraAccepted = FoxRunInboundJson.TryRead(
                Encoding.UTF8.GetBytes(
                    "{\"position\":{\"x\":1,\"y\":2,\"z\":3,\"unexpected\":4}}"),
                "position",
                out UnityEngine.Vector3 _,
                out var vectorExtraError);
            Check(
                !vectorExtraAccepted,
                "187-E03-001-6: generated JSON rejects undeclared vector properties ("
                    + vectorExtraError + ")");

            var output = new StringBuilder();
            FoxRunInboundJson.AppendObject(
                output,
                new GeneratedFloatingProbe
                {
                    Reading = float.NaN,
                    Ratio = double.PositiveInfinity
                });
            var encoded = output.ToString();
            Check(
                encoded.Contains("\"Reading\":null", StringComparison.Ordinal)
                    && encoded.Contains("\"Ratio\":null", StringComparison.Ordinal)
                    && !encoded.Contains("NaN", StringComparison.Ordinal)
                    && !encoded.Contains("Infinity", StringComparison.Ordinal),
                "187-E03-001-7: generated JSON emits null for non-finite floating output (" + encoded + ")");
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

        private sealed class GeneratedFloatingProbe
        {
            public float Reading { get; set; }
            public double Ratio { get; set; }
        }
    }
}
