// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-55 review follow-up guard for Phase 139 remote timeline validations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Source-shape validation for Phase 163-55 review fixes.
    /// </summary>
    public static class Phase163_55Validation
    {
        private static int _passed;

        /// <summary>
        /// Validates that Phase 139 remote timeline validation hardening remains in place.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-55: Phase 139 Remote Timeline Validation Robustness ===");
            _passed = 0;

            VerifyCursorEndpointPreflightBeforeAuth();
            VerifyPhase139PythonOutputDrain();
            VerifyTempMcapScopedCleanup();
            VerifyDirectFileRouteOwnsSourceIdentity();
            VerifyPhase139DLivePreflightCoverage();
            VerifyMethodExtractorsIgnoreLiteralBraces();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine($"Phase 163-55: {_passed} checks passed.");
        }

        private static void VerifyCursorEndpointPreflightBeforeAuth()
        {
            var endpoint = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");
            var optionsBranch = endpoint.IndexOf("HttpMethod, \"OPTIONS\"", StringComparison.Ordinal);
            var authGate = endpoint.IndexOf("IsAuthorized(context.Request, options)", StringComparison.Ordinal);

            Check(optionsBranch >= 0 && authGate >= 0 && optionsBranch < authGate,
                "163-55A-1: cursor endpoint handles CORS preflight before bearer-token auth");
        }

        private static void VerifyPhase139PythonOutputDrain()
        {
            var phase139 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase139Validation.cs");
            Check(phase139.Contains("process.WaitForExit();", StringComparison.Ordinal)
                  && phase139.Contains("WaitForOutputTasks(outputTask, errorTask, 2_000)", StringComparison.Ordinal)
                  && phase139.Contains("WaitForOutputTasks(outputTask, errorTask, 1_000)", StringComparison.Ordinal),
                "163-55B-1: Phase139 Python self-test waits boundedly for redirected output streams");
        }

        private static void VerifyTempMcapScopedCleanup()
        {
            var helper = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/TempMcapHelper.cs");
            var phase139b = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase139BValidation.cs");

            Check(helper.Contains("public static void Cleanup(string labelPrefix)", StringComparison.Ordinal)
                  && helper.Contains("Path.GetFileName(path).StartsWith(filePrefix, StringComparison.Ordinal)", StringComparison.Ordinal)
                  && phase139b.Contains("TempMcapHelper.Cleanup(\"phase139b\")", StringComparison.Ordinal)
                  && !phase139b.Contains("Content.Headers.ContentLength > 0", StringComparison.Ordinal),
                "163-55C-1: Phase139B cleanup is scoped and HEAD validation does not depend on Content-Length");
        }

        private static void VerifyPhase139DLivePreflightCoverage()
        {
            var phase139d = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase139DValidation.cs");

            Check(phase139d.Contains("PreflightStatus(", StringComparison.Ordinal)
                  && phase139d.Contains("139D-4I2", StringComparison.Ordinal)
                  && phase139d.Contains("139D-4L2", StringComparison.Ordinal)
                  && phase139d.Contains("optionsBranch < authGate", StringComparison.Ordinal)
                  && phase139d.Contains("status == HttpStatusCode.Accepted", StringComparison.Ordinal),
                "163-55D-1: Phase139D covers unauthenticated and token-protected CORS preflight plus POST status");
        }

        private static void VerifyDirectFileRouteOwnsSourceIdentity()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapDataSourcePrototype.cs");
            var directFile = ExtractMethodBody(source, "public RemoteMcapDataStreamResponse GetDirectFileStream");
            var dataStream = ExtractMethodBody(source, "public RemoteMcapDataStreamResponse GetDataStream");

            Check(directFile.Contains("!string.IsNullOrEmpty(request.SourceId)", StringComparison.Ordinal)
                  && dataStream.Contains("if (!string.Equals(request.SourceId, _sourceId, StringComparison.Ordinal))", StringComparison.Ordinal),
                "163-55D-2: direct file route may omit sourceId while /v1/data keeps explicit source identity");
        }

        private static string ExtractMethodBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            var brace = source.IndexOf('{', start);
            if (brace < 0)
                return string.Empty;

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(brace, i - brace + 1);
                }
            }

            return string.Empty;
        }

        private static void VerifyMethodExtractorsIgnoreLiteralBraces()
        {
            var phase139d = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase139DValidation.cs");
            var unit = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/RemoteTimelineOptimizationTests.cs");

            Check(phase139d.Contains("VerifyMethodExtractorHandlesLiteralBraces", StringComparison.Ordinal)
                  && phase139d.Contains("FindMatchingBrace", StringComparison.Ordinal)
                  && phase139d.Contains("IsEscaped", StringComparison.Ordinal)
                  && phase139d.Contains("ReplayAdvanceToExternalCursor", StringComparison.Ordinal)
                  && phase139d.Contains("!externalAdvance.Contains(\"QueueReplaySceneSnapshot\"", StringComparison.Ordinal),
                "163-55E-1: Phase139D method extraction ignores literal braces and checks advance/seek scopes");
            Check(unit.Contains("ExtractFunctionIgnoresLiteralBraces", StringComparison.Ordinal)
                  && unit.Contains("IsEscaped", StringComparison.Ordinal),
                "163-55E-2: remote timeline unit extractor ignores literal braces");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_55Validation.cs", StringComparison.Ordinal)
                  && registry.Contains("Ci(\"--phase163-55\",", StringComparison.Ordinal)
                  && registry.Contains("Phase163_55Validation.Validate, includeInDefault: false)", StringComparison.Ordinal),
                "163-55F-1: Phase163-55 validation is compiled and registered");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot()
                ?? throw new InvalidOperationException("Could not find repository root.");
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
