// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 164-55 optimization guards for Phase 139 remote timeline paths.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Source-shape validation for Phase 164-55 optimization fixes.</summary>
    public static class Phase164_55Validation
    {
        private static int _passed;

        /// <summary>Runs Phase 164-55 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("--- Phase 164-55 Tests ---");
            _passed = 0;

            VerifyManifestBytesAvoidPublicClonePath();
            VerifyCursorEndpointUsesPooledBodyAndPreencodedResponses();
            VerifyCursorEndpointResolvesCorsOncePerHandledRequest();
            VerifyRemoteRouterParsesIsoTimesWithoutIntermediateStrings();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine($"Phase 164-55: {_passed} checks passed.");
        }

        private static void VerifyManifestBytesAvoidPublicClonePath()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapDataSourcePrototype.cs");
            var manifestBytes = ExtractMethodBody(source, "private byte[] GetCachedManifestBytes()");
            var publicManifest = ExtractMethodBody(source, "private RemoteMcapManifest GetCachedManifest()");

            Check(source.Contains("GetCachedManifestCore(FileStamp loadStamp, out FileStamp storeStamp)", StringComparison.Ordinal),
                "164-55A-1: manifest cache has an internal core path that can return the cached object");
            Check(publicManifest.Contains("CloneManifest(GetCachedManifestCore(ReadFileStamp(), out _))", StringComparison.Ordinal),
                "164-55A-2: public manifest API still returns a defensive clone");
            Check(manifestBytes.Contains("GetCachedManifestCore(stamp, out var storeStamp)", StringComparison.Ordinal)
                  && !manifestBytes.Contains("Serialize(GetCachedManifest())", StringComparison.Ordinal),
                "164-55A-3: manifest bytes path avoids the public clone API on cache miss");
        }

        private static void VerifyCursorEndpointUsesPooledBodyAndPreencodedResponses()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");
            var readBody = ExtractMethodBody(source, "private string ReadBody(HttpListenerRequest request)");

            Check(source.Contains("private static readonly byte[] AcceptedCursorResponseBytes", StringComparison.Ordinal)
                  && source.Contains("private static readonly byte[] DuplicateCursorResponseBytes", StringComparison.Ordinal),
                "164-55B-1: cursor endpoint pre-encodes common fixed responses");
            Check(readBody.Contains("ArrayPool<byte>.Shared.Rent(_options.MaxBodyBytes + 1)", StringComparison.Ordinal)
                  && readBody.Contains("ArrayPool<byte>.Shared.Return(buffer)", StringComparison.Ordinal)
                  && !readBody.Contains("new char[_options.MaxBodyBytes + 1]", StringComparison.Ordinal),
                "164-55B-2: cursor endpoint rents request body buffers");
        }

        private static void VerifyCursorEndpointResolvesCorsOncePerHandledRequest()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");
            var handle = ExtractMethodBody(source, "private void Handle(HttpListenerContext context)");
            var tryWriteBytes = ExtractMethodBody(source, "private void TryWrite(HttpListenerContext context, int statusCode, byte[] bytes, CorsDecision cors)");

            Check(source.Contains("private readonly struct CorsDecision", StringComparison.Ordinal)
                  && source.Contains("private CorsDecision ResolveCors(HttpListenerRequest request)", StringComparison.Ordinal),
                "164-55C-1: cursor endpoint caches CORS decision in a small value type");
            Check(Count(handle, "ResolveCors(context.Request)") == 1
                  && handle.Contains("TryWrite(context, 202, AcceptedCursorResponseBytes, cors)", StringComparison.Ordinal)
                  && handle.Contains("TryWrite(context, 204, string.Empty, cors)", StringComparison.Ordinal),
                "164-55C-2: cursor endpoint resolves CORS once and passes it through hot responses");
            Check(tryWriteBytes.Contains("cors.ResponseOrigin", StringComparison.Ordinal)
                  && !tryWriteBytes.Contains("IsCorsOriginAllowed", StringComparison.Ordinal),
                "164-55C-3: cursor response writer does not rescan allowed origins");
        }

        private static void VerifyRemoteRouterParsesIsoTimesWithoutIntermediateStrings()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapHttpRouter.cs");
            var parse = ExtractMethodBody(source, "private static bool TryParseIsoUtcNs(string value, out ulong nanoseconds)");

            Check(parse.Contains("value.AsSpan(0, value.Length - 1)", StringComparison.Ordinal)
                  && parse.Contains("withoutZone.Slice(0, dot)", StringComparison.Ordinal)
                  && parse.Contains("ReadOnlySpan<char>.Empty", StringComparison.Ordinal),
                "164-55D-1: ISO timestamp parser uses span slices instead of Substring");
            Check(!parse.Contains("Substring", StringComparison.Ordinal)
                  && !parse.Contains("PadRight", StringComparison.Ordinal)
                  && !parse.Contains("ulong.Parse", StringComparison.Ordinal),
                "164-55D-2: ISO timestamp parser avoids intermediate fraction strings");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase164-55\", \"Phase 164-55: optimization guards for Phase 139 remote timeline paths\", Phase164_55Validation.Validate, includeInDefault: false)", StringComparison.Ordinal)
                  && project.Contains("Phase164_55Validation.cs", StringComparison.Ordinal),
                "164-55E-1: validation registry and project compile Phase164-55");
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

        private static int Count(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot()
                ?? throw new InvalidOperationException("Could not find repository root.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
