// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 139B validation for the official Remote Data Loader HTTP backend contract.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// CI-safe checks for the Phase 139B Remote Data Loader contract surface.
    /// Live Foxglove browser integration remains a manual Phase139C concern.
    /// </summary>
    public static class Phase139BValidation
    {
        private static int _passed;

        /// <summary>Runs all Phase 139B validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 139B: Remote Data Loader HTTP Backend ===");
            _passed = 0;

            try
            {
                VerifyOfficialManifestSerialization();
                VerifyEmbeddedHttpSurface();
                VerifyNanosecondRangeSurface();
                VerifyBearerAuthSurface();
                VerifyValidationWiring();
            }
            finally
            {
                TempMcapHelper.Cleanup();
            }

            Console.WriteLine($"Phase 139B: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void VerifyOfficialManifestSerialization()
        {
            var manifest = BuildFixtureManifest();
            var json = RemoteMcapOfficialManifestSerializer.Serialize(manifest);
            JObject parsed;
            using (var reader = new JsonTextReader(new StringReader(json))
            {
                DateParseHandling = DateParseHandling.None
            })
            {
                parsed = JObject.Load(reader);
            }

            Check((string)parsed["name"] == "Phase139B Fixture",
                "139B-1A: official manifest includes optional display name");
            Check(parsed["sources"] is JArray sources && sources.Count == 1,
                "139B-1B: official manifest contains one source");

            var source = (JObject)parsed["sources"][0];
            Check((string)source["url"] == "/v1/data?recordingId=fixture&startTime=2026-06-05T12%3A00%3A00Z&endTime=2026-06-05T12%3A00%3A01Z",
                "139B-1C: official streamed source uses data URL");
            Check((string)source["id"] == "unity2foxglove-v1-fixture",
                "139B-1D: official streamed source includes stable cache id");
            Check((string)source["startTime"] == "2026-06-05T12:00:00Z"
                  && (string)source["endTime"] == "2026-06-05T12:00:01Z",
                "139B-1E: official source times are ISO 8601 UTC strings");
            Check(source["supportsRangeRequests"] == null,
                "139B-1F: Phase139B serializer emits streamed sources, not static byte-range sources");

            var topics = (JArray)source["topics"];
            Check(topics.Count == 1
                  && (string)topics[0]["name"] == "/tf"
                  && (string)topics[0]["messageEncoding"] == "protobuf"
                  && (int)topics[0]["schemaId"] == 1,
                "139B-1G: official topic fields match Foxglove manifest schema");

            var schemas = (JArray)source["schemas"];
            Check(schemas.Count == 1
                  && (int)schemas[0]["id"] == 1
                  && (string)schemas[0]["name"] == "foxglove.FrameTransform"
                  && (string)schemas[0]["encoding"] == "protobuf"
                  && (string)schemas[0]["data"] == "AQIDBA==",
                "139B-1H: official schema fields include base64 schema data");
        }

        private static RemoteMcapManifest BuildFixtureManifest()
        {
            var manifest = new RemoteMcapManifest { Name = "Phase139B Fixture" };
            var source = new RemoteMcapSource
            {
                Id = "unity2foxglove-v1-fixture",
                DataUrl = "/v1/data?recordingId=fixture&startTime=2026-06-05T12%3A00%3A00Z&endTime=2026-06-05T12%3A00%3A01Z",
                HasTimeRange = true,
                StartTimeNs = 1780660800000000000UL,
                EndTimeNs = 1780660801000000000UL
            };
            source.Topics.Add(new RemoteMcapTopic
            {
                ChannelId = 7,
                Name = "/tf",
                MessageEncoding = "protobuf",
                SchemaId = 1
            });
            source.Schemas.Add(new RemoteMcapSchema
            {
                Id = 1,
                Name = "foxglove.FrameTransform",
                Encoding = "protobuf",
                DataBase64 = "AQIDBA==",
                DataLength = 4
            });
            manifest.Sources.Add(source);
            return manifest;
        }

        private static void VerifyEmbeddedHttpSurface()
        {
            var mcapPath = CreateIndexedFixture("http");
            var options = new RemoteMcapHttpOptions
            {
                Host = "127.0.0.1",
                McapPath = mcapPath,
                SourceId = "phase139b-http",
                ManifestName = "Phase139B HTTP Fixture"
            };

            string baseUrl;
            using (var server = StartRemoteMcapServerWithRetry(options))
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                baseUrl = server.BaseUrl;
                var manifest = client.GetAsync(baseUrl + "/v1/manifest").GetAwaiter().GetResult();
                var body = manifest.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var json = ParseJson(body);

                Check(manifest.StatusCode == HttpStatusCode.OK,
                    "139B-3A: HTTP backend serves GET /v1/manifest");
                Check(manifest.Content.Headers.ContentType != null
                      && manifest.Content.Headers.ContentType.MediaType == "application/json",
                    "139B-3B: manifest response uses application/json");
                Check((string)json["name"] == "Phase139B HTTP Fixture"
                      && ((JArray)json["sources"]).Count == 1,
                    "139B-3C: HTTP manifest is serialized with official source shape");
                Check(((string)json["sources"][0]["url"]).StartsWith("/v1/data?recordingId=phase139b-http", StringComparison.Ordinal),
                    "139B-3D: HTTP manifest points at official data route");

                // Foxglove's stock Remote files dialog requires a URL ending
                // in a filename; /v1/manifest is still the backend contract,
                // while this direct file route is the browser-facing entry.
                var directHead = new HttpRequestMessage(HttpMethod.Head, baseUrl + "/v1/files/phase139b-http.mcap");
                var directHeadResponse = client.SendAsync(directHead).GetAwaiter().GetResult();
                Check(directHeadResponse.StatusCode == HttpStatusCode.OK
                      && directHeadResponse.Content.Headers.ContentLength > 0,
                    "139B-3D2: HTTP backend exposes a direct .mcap file URL for Foxglove Remote files");

                var directRange = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v1/files/phase139b-http.mcap");
                directRange.Headers.Range = new RangeHeaderValue(0, 7);
                var directRangeResponse = client.SendAsync(directRange).GetAwaiter().GetResult();
                var directRangeBytes = directRangeResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                Check(directRangeResponse.StatusCode == HttpStatusCode.PartialContent
                      && directRangeBytes.SequenceEqual(new byte[] { 0x89, (byte)'M', (byte)'C', (byte)'A', (byte)'P', (byte)'0', 0x0D, 0x0A }),
                    "139B-3D3: direct .mcap route supports byte-range reads for Foxglove Remote files");

                var directPreflight = new HttpRequestMessage(HttpMethod.Options, baseUrl + "/v1/files/phase139b-http.mcap");
                directPreflight.Headers.TryAddWithoutValidation("Origin", "https://app.foxglove.dev");
                directPreflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
                directPreflight.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "range, content-type, accept");
                directPreflight.Headers.TryAddWithoutValidation("Access-Control-Request-Private-Network", "true");
                var directPreflightResponse = client.SendAsync(directPreflight).GetAwaiter().GetResult();
                var allowHeaders = string.Join(",", directPreflightResponse.Headers.GetValues("Access-Control-Allow-Headers"));
                var allowPrivateNetwork = directPreflightResponse.Headers.TryGetValues(
                    "Access-Control-Allow-Private-Network",
                    out var privateNetworkValues)
                    ? string.Join(",", privateNetworkValues)
                    : string.Empty;
                Check(directPreflightResponse.StatusCode == HttpStatusCode.NoContent
                      && allowHeaders.IndexOf("Range", StringComparison.OrdinalIgnoreCase) >= 0
                      && allowHeaders.IndexOf("Content-Type", StringComparison.OrdinalIgnoreCase) >= 0
                      && allowHeaders.IndexOf("Accept", StringComparison.OrdinalIgnoreCase) >= 0
                      && string.Equals(allowPrivateNetwork, "true", StringComparison.OrdinalIgnoreCase),
                    "139B-3D4: direct .mcap route accepts Foxglove browser CORS preflight");

                var data = client.GetAsync(baseUrl + "/v1/data?recordingId=phase139b-http&startTime=2026-06-05T12:00:01Z&endTime=2026-06-05T12:00:01Z")
                    .GetAwaiter()
                    .GetResult();
                var dataBytes = data.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                Check(data.StatusCode == HttpStatusCode.OK,
                    "139B-3E: HTTP backend serves GET /v1/data");
                Check(data.Content.Headers.ContentType != null
                      && data.Content.Headers.ContentType.MediaType == "application/octet-stream",
                    "139B-3F: data response uses application/octet-stream");
                VerifyRangeSlice(dataBytes);

                var invalidRange = client.GetAsync(baseUrl + "/v1/data?recordingId=phase139b-http&startTime=2026-06-05T12:00:02Z&endTime=2026-06-05T12:00:01Z")
                    .GetAwaiter()
                    .GetResult();
                Check(invalidRange.StatusCode == HttpStatusCode.BadRequest,
                    "139B-3G: invalid time ranges return 400");

                var missing = client.GetAsync(baseUrl + "/not-found").GetAwaiter().GetResult();
                Check(missing.StatusCode == HttpStatusCode.NotFound,
                    "139B-3H: unsupported HTTP path returns 404");

                var method = client.PostAsync(baseUrl + "/v1/manifest", new StringContent(string.Empty)).GetAwaiter().GetResult();
                Check(method.StatusCode == HttpStatusCode.MethodNotAllowed,
                    "139B-3I: unsupported manifest method returns 405");

                Check(server.IsRunning,
                    "139B-3J: HTTP server reports running before Dispose");
            }

            Check(!RemoteMcapHttpServer.IsListening(baseUrl),
                "139B-3K: HTTP server closes listener resources on Dispose");
        }

        private static void VerifyBearerAuthSurface()
        {
            var mcapPath = CreateIndexedFixture("auth");
            var options = new RemoteMcapHttpOptions
            {
                Host = "127.0.0.1",
                McapPath = mcapPath,
                SourceId = "phase139b-auth",
                ManifestName = "Phase139B Auth Fixture",
                RequiredBearerToken = "phase139b-token"
            };

            using (var server = StartRemoteMcapServerWithRetry(options))
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                var manifestWithoutToken = client.GetAsync(server.BaseUrl + "/v1/manifest").GetAwaiter().GetResult();
                Check(manifestWithoutToken.StatusCode == HttpStatusCode.Unauthorized,
                    "139B-4A: manifest requests without the configured bearer token return 401");

                var badData = new HttpRequestMessage(
                    HttpMethod.Get,
                    server.BaseUrl + "/v1/data?recordingId=phase139b-auth");
                badData.Headers.TryAddWithoutValidation("Authorization", "Bearer wrong-token");
                var dataWithWrongToken = client.SendAsync(badData).GetAwaiter().GetResult();
                Check(dataWithWrongToken.StatusCode == HttpStatusCode.Unauthorized,
                    "139B-4B: data requests with the wrong bearer token return 401");

                var fileWithoutToken = new HttpRequestMessage(
                    HttpMethod.Head,
                    server.BaseUrl + "/v1/files/phase139b-auth.mcap");
                var fileWithoutTokenResponse = client.SendAsync(fileWithoutToken).GetAwaiter().GetResult();
                Check(fileWithoutTokenResponse.StatusCode == HttpStatusCode.Unauthorized,
                    "139B-4B2: direct MCAP file requests without the configured bearer token return 401");

                var badFile = new HttpRequestMessage(
                    HttpMethod.Get,
                    server.BaseUrl + "/v1/files/phase139b-auth.mcap");
                badFile.Headers.TryAddWithoutValidation("Authorization", "Bearer wrong-token");
                var fileWithWrongToken = client.SendAsync(badFile).GetAwaiter().GetResult();
                Check(fileWithWrongToken.StatusCode == HttpStatusCode.Unauthorized,
                    "139B-4B3: direct MCAP file requests with the wrong bearer token return 401");

                var goodManifest = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/v1/manifest");
                goodManifest.Headers.TryAddWithoutValidation("Authorization", "Bearer phase139b-token");
                var manifestWithToken = client.SendAsync(goodManifest).GetAwaiter().GetResult();
                Check(manifestWithToken.StatusCode == HttpStatusCode.OK,
                    "139B-4C: manifest requests with the configured bearer token succeed");

                var goodData = new HttpRequestMessage(
                    HttpMethod.Get,
                    server.BaseUrl + "/v1/data?recordingId=phase139b-auth&startTime=2026-06-05T12:00:01Z&endTime=2026-06-05T12:00:01Z");
                goodData.Headers.TryAddWithoutValidation("Authorization", "Bearer phase139b-token");
                var dataWithToken = client.SendAsync(goodData).GetAwaiter().GetResult();
                Check(dataWithToken.StatusCode == HttpStatusCode.OK
                      && dataWithToken.Content.Headers.ContentType != null
                      && dataWithToken.Content.Headers.ContentType.MediaType == "application/octet-stream",
                    "139B-4D: data requests with the configured bearer token stream MCAP bytes");

                var goodFile = new HttpRequestMessage(
                    HttpMethod.Head,
                    server.BaseUrl + "/v1/files/phase139b-auth.mcap");
                goodFile.Headers.TryAddWithoutValidation("Authorization", "Bearer phase139b-token");
                var fileWithToken = client.SendAsync(goodFile).GetAwaiter().GetResult();
                Check(fileWithToken.StatusCode == HttpStatusCode.OK
                      && fileWithToken.Content.Headers.ContentLength > 0,
                    "139B-4E: direct MCAP file requests with the configured bearer token succeed");
            }
        }

        private static void VerifyRangeSlice(byte[] dataBytes)
        {
            VerifyRangeSlice(dataBytes, 1780660801000000000UL, "139B-3L", "139B-3M", "139B-3N");
        }

        private static void VerifyNanosecondRangeSurface()
        {
            var mcapPath = CreateNanosecondFixture();
            var options = new RemoteMcapHttpOptions
            {
                Host = "127.0.0.1",
                McapPath = mcapPath,
                SourceId = "phase139b-ns",
                ManifestName = "Phase139B Nanosecond Fixture"
            };

            using (var server = StartRemoteMcapServerWithRetry(options))
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                var data = client.GetAsync(server.BaseUrl + "/v1/data?recordingId=phase139b-ns&startTime=1970-01-01T00:00:00.00000002Z&endTime=1970-01-01T00:00:00.00000002Z")
                    .GetAwaiter()
                    .GetResult();
                var dataBytes = data.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

                Check(data.StatusCode == HttpStatusCode.OK,
                    "139B-5A: HTTP backend accepts nanosecond ISO 8601 range bounds");
                VerifyRangeSlice(dataBytes, 20UL, "139B-5B", "139B-5C", "139B-5D");
            }
        }

        private static void VerifyRangeSlice(
            byte[] dataBytes,
            ulong expectedLogTime,
            string schemaLabel,
            string rangeLabel,
            string sortedLabel)
        {
            using (var stream = new MemoryStream(dataBytes))
            using (var loader = new McapDataLoader(stream, leaveOpen: true))
            {
                var initialization = loader.Initialize();
                var messages = loader.CreateIterator(new McapDataLoaderQuery { MaxMessages = 0 }).ToList();
                Check(initialization.Schemas.Count == 1 && initialization.Channels.Count == 1,
                    schemaLabel + ": data response preserves schema and channel records");
                Check(messages.Count == 1 && messages[0].LogTime == expectedLogTime,
                    rangeLabel + ": data response includes only messages inside the requested inclusive range");
                Check(messages.SequenceEqual(messages.OrderBy(m => m.LogTime).ThenBy(m => m.ChannelId)),
                    sortedLabel + ": data response messages are sorted by ascending log time");
            }
        }

        private static void VerifyValidationWiring()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase139b\"", StringComparison.Ordinal),
                "139B-2A: registry wires --phase139b");
            Check(registry.Contains("Phase139BValidation.Validate", StringComparison.Ordinal),
                "139B-2B: registry points Phase139B at the validation entrypoint");
            Check(project.Contains("Phase139BValidation.cs", StringComparison.Ordinal),
                "139B-2C: test project compiles Phase139BValidation");
        }

        private static JObject ParseJson(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json))
            {
                DateParseHandling = DateParseHandling.None
            })
            {
                return JObject.Load(reader);
            }
        }

        private static string CreateIndexedFixture(string label)
        {
            var path = TempMcapHelper.CreatePath("phase139b_" + label);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var recorder = new McapRecorder(fs))
            {
                recorder.AddChannel(1, "/phase139b/http", "json", "phase139b.Http", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 1780660800000000000UL, Encoding.UTF8.GetBytes("{\"index\":0}"));
                recorder.WriteMessage(1, 1780660801000000000UL, Encoding.UTF8.GetBytes("{\"index\":1}"));
                recorder.WriteMessage(1, 1780660802000000000UL, Encoding.UTF8.GetBytes("{\"index\":2}"));
                recorder.Close();
            }

            return path;
        }

        private static string CreateNanosecondFixture()
        {
            var path = TempMcapHelper.CreatePath("phase139b_nanosecond");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var recorder = new McapRecorder(fs))
            {
                recorder.AddChannel(1, "/phase139b/nanosecond", "json", "phase139b.Nanosecond", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10UL, Encoding.UTF8.GetBytes("{\"index\":0}"));
                recorder.WriteMessage(1, 20UL, Encoding.UTF8.GetBytes("{\"index\":1}"));
                recorder.WriteMessage(1, 40UL, Encoding.UTF8.GetBytes("{\"index\":2}"));
                recorder.Close();
            }

            return path;
        }

        private static int FindFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static RemoteMcapHttpServer StartRemoteMcapServerWithRetry(RemoteMcapHttpOptions options)
        {
            Exception lastError = null;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                options.Port = FindFreeLoopbackPort();
                try
                {
                    return RemoteMcapHttpServer.Start(options);
                }
                catch (Exception ex) when (IsAddressAlreadyInUse(ex))
                {
                    lastError = ex;
                }
            }

            throw new InvalidOperationException(
                "Phase139B could not bind a loopback HTTP test server after 5 attempts.",
                lastError);
        }

        private static bool IsAddressAlreadyInUse(Exception error)
        {
            return error is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse
                   || error is HttpListenerException listener
                   && (listener.ErrorCode == 183 || listener.ErrorCode == 10_048);
        }

        private static string Read(string relativePath) => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase139B validation.");
            return root;
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
