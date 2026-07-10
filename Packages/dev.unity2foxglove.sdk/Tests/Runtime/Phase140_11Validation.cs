// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-11 MCAP DataLoader and remote-file review fixes.

using System;
using System.IO;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for MCAP DataLoader and remote-file defects found
    /// in Phase 140-11.
    /// </summary>
    public static class Phase140_11Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140-11 MCAP DataLoader and remote-file review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-11: MCAP DataLoader and remote-file review fixes ===");
            _passed = 0;

            RangeWriterReceivesMemoryCapAndAvoidsFullMaterialization();
            RemoteHttpDisposeDoesNotWaitTwoSeconds();
            RemoteFileServerWarnsWhenCorsIsTokenless();
            DataLoaderStreamConstructorRejectsNullStream();
            ManifestOpenRaceReturnsNotFoundInsteadOfServerError();
            RemoteHostDocumentsWindowsAclBoundary();
            DecodeRegistryResetsDiagnosticsForNoDomainReload();
            DecodeRegistryAvoidsDiscardedRawPayload();
            RemoteRangeCopyUsesPooledBuffer();
            RemoteManifestCachesSerializedBytes();
            DataLoaderUsesSharedEmptyArrays();
            NoMatchQueriesUseSharedEmptyResults();
            SchemaReferenceValidationReusesSchemaMap();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-11: {_passed} checks passed.");
        }

        private static void RangeWriterReceivesMemoryCapAndAvoidsFullMaterialization()
        {
            var dataSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapDataSourcePrototype.cs");
            var rangeWriter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapRangeWriter.cs");

            Check(dataSource.Contains("CreateSlice(_mcapPath, request, _maxInMemoryDataBytes)", StringComparison.Ordinal),
                "140-11A-1: remote data source passes the response memory cap into range slicing");
            Check(rangeWriter.Contains("CreateSlice(string mcapPath, RemoteMcapRequest request, long maxInMemoryDataBytes)", StringComparison.Ordinal),
                "140-11A-2: range writer accepts the configured in-memory response cap");
            Check(!rangeWriter.Contains(".ToList()", StringComparison.Ordinal),
                "140-11A-3: range writer no longer materializes the entire requested range before applying the cap");
        }

        private static void RemoteHttpDisposeDoesNotWaitTwoSeconds()
        {
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapHttpServer.cs");

            Check(!server.Contains("TimeSpan.FromSeconds(2)", StringComparison.Ordinal),
                "140-11B-1: remote MCAP HTTP dispose no longer waits two seconds on shutdown");
            Check(server.Contains("TimeSpan.FromMilliseconds(50)", StringComparison.Ordinal),
                "140-11B-2: remote MCAP HTTP dispose uses a short main-thread-safe wait");
        }

        private static void RemoteFileServerWarnsWhenCorsIsTokenless()
        {
            var manager = PhaseValidationSourceHelpers.ReadFoxgloveManagerServerSources();
            var options = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapHttpOptions.cs");

            Check(manager.Contains("Remote MCAP file URL is running without a bearer token", StringComparison.Ordinal),
                "140-11C-1: manager warns when wildcard-CORS remote MCAP file serving has no bearer token");
            Check(options.Contains("wildcard CORS", StringComparison.Ordinal)
                  && options.Contains("loopback MCAP", StringComparison.Ordinal),
                "140-11C-2: RequiredBearerToken XML doc documents the tokenless wildcard-CORS risk");
        }

        private static void DataLoaderStreamConstructorRejectsNullStream()
        {
            CheckThrowsArgumentNull(
                () => new McapDataLoader((Stream)null),
                "stream",
                "140-11D-1: DataLoader stream constructor rejects null with a clear parameter name");
        }

        private static void ManifestOpenRaceReturnsNotFoundInsteadOfServerError()
        {
            var dataSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapDataSourcePrototype.cs");

            Check(dataSource.Contains("catch (IOException", StringComparison.Ordinal)
                  && dataSource.Contains("return CreateMissingManifest()", StringComparison.Ordinal),
                "140-11E-1: manifest file open races are converted to not-found manifest responses");
        }

        private static void RemoteHostDocumentsWindowsAclBoundary()
        {
            var options = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapHttpOptions.cs");

            Check(options.Contains("http.sys URL ACL", StringComparison.Ordinal)
                  && options.Contains("non-loopback", StringComparison.Ordinal),
                "140-11F-1: Host option documents the Windows non-loopback ACL boundary");
        }

        private static void DecodeRegistryResetsDiagnosticsForNoDomainReload()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDecodeRegistry.cs");

            Check(registry.Contains("RuntimeInitializeOnLoadMethod", StringComparison.Ordinal)
                  && registry.Contains("FactoryDiagnostics.Clear()", StringComparison.Ordinal),
                "140-11G-1: decoder factory diagnostics reset across no-domain-reload Play Mode starts");
            Check(registry.Contains("BuiltInFactories = CreateBuiltInFactoriesLazy()", StringComparison.Ordinal),
                "140-11G-2: decoder registry refreshes built-in factory cache on Unity runtime reload");
        }

        private static void DecodeRegistryAvoidsDiscardedRawPayload()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDecodeRegistry.cs");
            var decode = SourceBetween(registry, "public McapDecodedMessage Decode(", "private IMcapMessageDecoder ResolveDecoder");
            Check(!decode.Contains("Payload = McapDecodedPayload.Raw(raw.Data)", StringComparison.Ordinal),
                "140-11I-1: Decode does not allocate a raw payload before resolving the decoder");
        }

        private static void RemoteRangeCopyUsesPooledBuffer()
        {
            var router = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapHttpRouter.cs");
            var copy = SourceBetween(router, "private static async Task CopyAndCloseAsync(", "private static Task WriteTextAsync");
            Check(copy.Contains("ArrayPool<byte>.Shared.Rent", StringComparison.Ordinal)
                  && copy.Contains("ArrayPool<byte>.Shared.Return", StringComparison.Ordinal)
                  && !copy.Contains("new byte[81920]", StringComparison.Ordinal),
                "140-11I-2: ranged HTTP copies reuse an ArrayPool buffer");
        }

        private static void RemoteManifestCachesSerializedBytes()
        {
            var dataSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapDataSourcePrototype.cs");
            var router = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapHttpRouter.cs");
            Check(dataSource.Contains("_cachedManifestBytes", StringComparison.Ordinal)
                  && dataSource.Contains("GetManifestBytes(", StringComparison.Ordinal)
                  && router.Contains("_source.GetManifestBytes(", StringComparison.Ordinal)
                  && !router.Contains("RemoteMcapOfficialManifestSerializer.Serialize(manifest.Manifest)", StringComparison.Ordinal),
                "140-11I-3: HTTP manifest responses reuse serialized bytes from the data source cache");
        }

        private static void DataLoaderUsesSharedEmptyArrays()
        {
            var files = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoaderMessage.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoaderSchema.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDecodeRegistry.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDecodedDataLoaderTypes.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapHttpRouter.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapManifestMapper.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapModels.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/DataLoader/McapFoxgloveProtobufDecoderFactory.cs"
            };

            for (var i = 0; i < files.Length; i++)
                Check(!ReadRepoText(files[i]).Contains("new byte[0]", StringComparison.Ordinal),
                    "140-11I-4: " + Path.GetFileName(files[i]) + " uses shared empty byte arrays");
        }

        private static void NoMatchQueriesUseSharedEmptyResults()
        {
            var loader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            Check(loader.Contains("return Array.Empty<McapDataLoaderMessage>();", StringComparison.Ordinal)
                  && !loader.Contains("return new List<McapDataLoaderMessage>();", StringComparison.Ordinal),
                "140-11I-5: no-match DataLoader queries return shared empty results");
        }

        private static void SchemaReferenceValidationReusesSchemaMap()
        {
            var loader = PhaseValidationSourceHelpers.ReadMcapDataLoaderSources();
            var validation = SourceBetween(loader, "private void AddSchemaReferenceProblems", "private void AddFoxRunSchemaMetadataProblems");
            Check(validation.Contains("_schemaMap.ContainsKey(channel.SchemaId)", StringComparison.Ordinal)
                  && !validation.Contains("new HashSet<ushort>", StringComparison.Ordinal),
                "140-11I-6: schema reference validation reuses the existing schema map");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase140_11Validation.cs", StringComparison.Ordinal),
                "140-11H-1: test project compiles Phase140_11Validation");
            Check(registry.Contains("--phase140-11", StringComparison.Ordinal)
                  && registry.Contains("Phase140_11Validation.Validate", StringComparison.Ordinal),
                "140-11H-2: validation registry exposes --phase140-11");
        }

        private static void CheckThrowsArgumentNull(Action action, string expectedParamName, string label)
        {
            try
            {
                action();
                throw new InvalidOperationException(label + " failed: no exception was thrown.");
            }
            catch (ArgumentNullException ex)
            {
                Check(string.Equals(ex.ParamName, expectedParamName, StringComparison.Ordinal), label);
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath));
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git"))
                    || File.Exists(Path.Combine(dir, ".gitignore")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            return Directory.GetCurrentDirectory();
        }

        private static string SourceBetween(string source, string startMarker, string endMarker)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            if (start < 0 || end < 0)
                throw new InvalidOperationException("Could not locate Phase140-11 source markers.");
            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
