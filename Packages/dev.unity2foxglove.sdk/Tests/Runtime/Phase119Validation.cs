// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 119 validation for the local prototype remote MCAP data-source boundary.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase119Validation
    {
        private const string Token = "phase119-token";
        private const string SourceId = "phase119-source";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 119: Remote MCAP Data Source Exploration ===");
            _passed = 0;

            VerifyBoundaryNote();
            VerifyDtoSurface();
            VerifyIndexedManifest();
            VerifyDirectManifest();
            VerifyAuthorizationAndUnsupportedRequests();
            VerifyDataBytesReadableByLocalReaders();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 119: {_passed} checks passed.");
        }

        private static void VerifyBoundaryNote()
        {
            var note = ReadRepoText("Developer/MCAP Remote Data Source Boundary.md");
            foreach (var required in new[]
            {
                "Remote Data Loader",
                "manifest endpoint",
                "data endpoint",
                "authorization source of truth",
                "Static/direct MCAP URL",
                "Remote Access Gateway",
                "https://docs.foxglove.dev/docs/visualization/connecting/cloud-data/remote-data-loader",
                "https://docs.foxglove.dev/docs/visualization/connecting/local-data",
                "https://docs.foxglove.dev/docs/visualization/connecting/live/remote-access",
                "No production Foxglove Remote Data Loader deployment"
            })
            {
                Check(note.Contains(required, StringComparison.Ordinal),
                    "119-A1: boundary note records " + required);
            }
        }

        private static void VerifyDtoSurface()
        {
            foreach (var typeName in new[]
            {
                "RemoteMcapManifest",
                "RemoteMcapSource",
                "RemoteMcapTopic",
                "RemoteMcapSchema",
                "RemoteMcapProblem",
                "RemoteMcapRequest",
                "RemoteMcapAuthorizationResult",
                "RemoteMcapManifestResponse",
                "RemoteMcapDataResponse",
                "RemoteMcapManifestMapper",
                "RemoteMcapDataSourcePrototype"
            })
            {
                Check(Type.GetType("Unity.FoxgloveSDK.IO." + typeName) != null,
                    "119-B1: required remote MCAP type exists: " + typeName);
            }
        }

        private static void VerifyIndexedManifest()
        {
            var path = CreateIndexedFixture("indexed");
            var service = new RemoteMcapDataSourcePrototype(path, SourceId, "phase119-indexed", Token);
            var response = service.GetManifest(AuthorizedRequest());
            Check(response.Status == RemoteMcapResponseStatus.Ok && response.Authorization.Allowed,
                "119-C1: authorized indexed manifest request succeeds");
            Check(response.Manifest.Name == "phase119-indexed", "119-C2: manifest name is preserved");

            var source = SingleSource(response);
            Check(source.Id == SourceId && source.DataUrl.Contains(SourceId, StringComparison.Ordinal),
                "119-C3: indexed source exposes stable source id and data route");
            Check(source.HasTimeRange && source.StartTimeNs == 10 && source.EndTimeNs == 40,
                "119-C4: indexed source maps inclusive time range");
            Check(source.Topics.Select(t => t.Name).SequenceEqual(new[] { "/phase119/a", "/phase119/b" }),
                "119-C5: indexed topics are deterministic by topic name");
            Check(source.Schemas.Select(s => s.Id).SequenceEqual(new ushort[] { 1, 2 }),
                "119-C6: indexed schemas are deterministic by schema id");
            Check(source.Problems.Any(p => p.Code == "FoxRunSchemaMetadataMissing"),
                "119-C7: DataLoader warnings are preserved as manifest problems");
        }

        private static void VerifyDirectManifest()
        {
            var path = CreateDirectFixture();
            var service = new RemoteMcapDataSourcePrototype(path, SourceId, "phase119-direct", Token);
            var response = service.GetManifest(AuthorizedRequest());
            Check(response.Status == RemoteMcapResponseStatus.Ok, "119-D1: direct manifest request succeeds");

            var source = SingleSource(response);
            Check(source.Topics.Select(t => t.Name).SequenceEqual(new[] { "/phase119/direct/a", "/phase119/direct/b" }),
                "119-D2: summary-less direct topics map into manifest");
            Check(source.Schemas.Count == 1 && source.Schemas[0].Name == "phase119.Direct",
                "119-D3: summary-less direct schemas map into manifest");
            Check(source.HasTimeRange && source.StartTimeNs == 10 && source.EndTimeNs == 40,
                "119-D4: summary-less direct time range maps into manifest");
        }

        private static void VerifyAuthorizationAndUnsupportedRequests()
        {
            var path = CreateIndexedFixture("auth");
            var service = new RemoteMcapDataSourcePrototype(path, SourceId, "phase119-auth", Token);

            var denied = service.GetManifest(new RemoteMcapRequest { BearerToken = "wrong" });
            Check(denied.Status == RemoteMcapResponseStatus.Unauthorized && !denied.Authorization.Allowed,
                "119-E1: unauthorized manifest request is denied");
            Check(denied.Manifest.Sources.Count == 0 && !ManifestContainsDataUrl(denied.Manifest),
                "119-E2: unauthorized manifest response does not reveal data URLs");

            var missing = service.GetData(new RemoteMcapRequest
            {
                BearerToken = "Bearer " + Token,
                SourceId = "missing"
            });
            Check(missing.Status == RemoteMcapResponseStatus.NotFound
                  && missing.Problems.Any(p => p.Code == "SourceNotFound"),
                "119-E3: invalid source id returns clear not-found");

            var unsupported = service.GetManifest(new RemoteMcapRequest
            {
                BearerToken = "Bearer " + Token,
                RequestMultipleSources = true
            });
            Check(unsupported.Status == RemoteMcapResponseStatus.Unsupported
                  && unsupported.Problems.Any(p => p.Code == "UnsupportedMultiSource"),
                "119-E4: unsupported multi-source request is explicit");
        }

        private static void VerifyDataBytesReadableByLocalReaders()
        {
            var path = CreateIndexedFixture("data");
            var service = new RemoteMcapDataSourcePrototype(path, SourceId, "phase119-data", Token);
            var response = service.GetData(AuthorizedRequest());
            Check(response.Status == RemoteMcapResponseStatus.Ok && response.Data.Length == new FileInfo(path).Length,
                "119-F1: authorized data request returns exact MCAP bytes");

            using (var ms = new MemoryStream(response.Data))
            {
                var summary = new McapReader(ms).ReadSummary();
                Check(summary.Channels.Count == 2, "119-F2: returned bytes are readable by McapReader");
            }

            using (var ms = new MemoryStream(response.Data))
            using (var reader = new McapIndexedReader(ms, leaveOpen: true))
            {
                Check(reader.ReadMessages().Count == 4, "119-F3: returned bytes are readable by McapIndexedReader");
            }

            using (var ms = new MemoryStream(response.Data))
            using (var loader = new McapDataLoader(ms, leaveOpen: true))
            {
                Check(loader.Initialize().Channels.Count == 2
                      && loader.CreateIterator(new McapDataLoaderQuery()).Count() == 4,
                    "119-F4: returned bytes are readable by McapDataLoader");
            }
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(program.Contains("--phase119", StringComparison.Ordinal)
                  && program.Contains("RunPhase119Only", StringComparison.Ordinal)
                  && program.Contains("Phase119Validation.Validate()", StringComparison.Ordinal),
                "119-G1: Program.cs wires --phase119");
            Check(project.Contains("Phase119Validation.cs", StringComparison.Ordinal),
                "119-G2: runtime test project compiles Phase119Validation");
        }

        private static RemoteMcapRequest AuthorizedRequest()
        {
            return new RemoteMcapRequest
            {
                BearerToken = "Bearer " + Token,
                SourceId = SourceId
            };
        }

        private static RemoteMcapSource SingleSource(RemoteMcapManifestResponse response)
        {
            if (response.Manifest.Sources.Count != 1)
                throw new Exception("Expected one manifest source, got " + response.Manifest.Sources.Count);
            return response.Manifest.Sources[0];
        }

        private static bool ManifestContainsDataUrl(RemoteMcapManifest manifest)
        {
            for (var i = 0; i < manifest.Sources.Count; i++)
            {
                if (!string.IsNullOrEmpty(manifest.Sources[i].DataUrl))
                    return true;
            }

            return false;
        }

        private static string CreateIndexedFixture(string label)
        {
            var path = Path.Combine(Path.GetTempPath(), "phase119_" + label + "_" + Guid.NewGuid().ToString("N") + ".mcap");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var recorder = new McapRecorder(fs))
            {
                recorder.AddChannel(2, "/phase119/b", "json", "phase119.B", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(1, "/phase119/a", "json", "phase119.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                recorder.WriteMessage(2, 20, Encoding.UTF8.GetBytes("{\"b\":20}"));
                recorder.WriteMessage(1, 30, Encoding.UTF8.GetBytes("{\"a\":30}"));
                recorder.WriteMessage(2, 40, Encoding.UTF8.GetBytes("{\"b\":40}"));
                recorder.Close();
            }

            return path;
        }

        private static string CreateDirectFixture()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase119_direct_" + Guid.NewGuid().ToString("N") + ".mcap");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new McapWriter(fs))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase119-direct");
                writer.WriteSchema(1, "phase119.Direct", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(2, 1, "/phase119/direct/b", "json", new Dictionary<string, string>());
                writer.WriteChannel(1, 1, "/phase119/direct/a", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                writer.WriteMessage(2, 1, 20, 20, Encoding.UTF8.GetBytes("{\"b\":20}"));
                writer.WriteMessage(1, 2, 30, 30, Encoding.UTF8.GetBytes("{\"a\":30}"));
                writer.WriteMessage(2, 2, 40, 40, Encoding.UTF8.GetBytes("{\"b\":40}"));
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            return path;
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase119 file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
