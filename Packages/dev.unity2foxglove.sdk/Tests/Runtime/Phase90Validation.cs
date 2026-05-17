// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 90 validation for ROS 2 .msg schema registry parity.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Phase 90 schema-only ROS 2 .msg / CDR advertisement boundary.
    /// </summary>
    public static class Phase90Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 90: ROS2 Msg Schema Registry Parity ===");
            _passed = 0;

            VerifyPlannedSourceFilesExist();
            VerifyCatalog();
            VerifyRegistry();
            VerifyWebSocketAdvertisement();
            VerifyRuntimeWrapper();
            VerifyMcapSchemaAndChannel();
            VerifyBoundary();

            Console.WriteLine($"Phase 90: {_passed} checks passed.");
        }

        private static void VerifyPlannedSourceFilesExist()
        {
            Check(!string.IsNullOrEmpty(ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/FoxgloveRos2MsgSchemaCatalog.cs")),
                "90A-1: ROS2 msg schema catalog source exists");
            Check(!string.IsNullOrEmpty(ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Ros2MsgSchemasSetup.cs")),
                "90A-2: ROS2 msg schemas setup source exists");
        }

        private static void VerifyCatalog()
        {
            Check(FoxgloveRos2MsgSchemaCatalog.SourceFileCount == 41,
                "90B-1: catalog records 41 ROS2 msg source files");
            Check(!string.IsNullOrEmpty(FoxgloveRos2MsgSchemaCatalog.SourceTreeSha256)
                  && FoxgloveRos2MsgSchemaCatalog.SourceTreeSha256.Length == 64,
                "90B-2: catalog records source tree SHA-256");
            Check(FoxgloveRos2MsgSchemaCatalog.Entries.Count == 41,
                "90B-3: catalog exposes 41 entries");
            Check(FoxgloveRos2MsgSchemaCatalog.TryGet("foxglove_msgs/msg/PointCloud", out var pointCloud),
                "90B-4: catalog contains foxglove_msgs/msg/PointCloud");
            Check(FoxgloveRos2MsgSchemaCatalog.TryGet("foxglove_msgs/msg/CompressedPointCloud", out _),
                "90B-5: catalog contains foxglove_msgs/msg/CompressedPointCloud");
            Check(FoxgloveRos2MsgSchemaCatalog.TryGet("foxglove_msgs/msg/SceneUpdate", out _),
                "90B-6: catalog contains foxglove_msgs/msg/SceneUpdate");
            Check(FoxgloveRos2MsgSchemaCatalog.Entries.All(entry => entry.SchemaEncoding == "ros2msg"),
                "90B-7: every catalog entry uses ros2msg schemaEncoding");
            Check(pointCloud.Content.StartsWith("# foxglove_msgs/msg/PointCloud", StringComparison.Ordinal)
                  && pointCloud.Content.Contains("MSG: geometry_msgs/Pose")
                  && pointCloud.Content.Contains("MSG: geometry_msgs/Point")
                  && pointCloud.Content.Contains("MSG: geometry_msgs/Quaternion")
                  && pointCloud.Content.Contains("MSG: foxglove_msgs/PackedElementField"),
                "90B-8: merged PointCloud schema includes transitive dependencies");

            var sourceRoot = Path.Combine(Phase16Validation.FindRepoRoot(), "third-party", "foxglove-sdk", "schemas", "ros2");
            if (Directory.Exists(sourceRoot))
            {
                Check(ComputeSourceTreeSha256(sourceRoot) == FoxgloveRos2MsgSchemaCatalog.SourceTreeSha256,
                    "90B-9: catalog source tree hash matches local third-party snapshot");
            }
            else
            {
                Check(!string.IsNullOrEmpty(FoxgloveRos2MsgSchemaCatalog.SourceTreeSha256),
                    "90B-9: catalog source tree hash is present when third-party snapshot is unavailable");
            }
        }

        private static void VerifyRegistry()
        {
            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);

            foreach (var entry in FoxgloveRos2MsgSchemaCatalog.Entries)
            {
                Check(registry.TryGetSchema(entry.SchemaName, "ros2msg", out var registered)
                      && registered.Encoding == "ros2msg"
                      && !string.IsNullOrEmpty(registered.Content),
                    "90C-1: registry resolves " + entry.SchemaName + " by ros2msg");
            }

            Check(registry.TryGetSchema("foxglove_msgs/msg/PointCloud", out var nameOnly)
                  && nameOnly.Encoding == "ros2msg",
                "90C-2: name-only lookup works for PointCloud when no jsonschema entry exists");
            Check(nameOnly.Content.StartsWith("# foxglove_msgs/msg/PointCloud", StringComparison.Ordinal),
                "90C-3: registered PointCloud content is merged .msg text");
        }

        private static void VerifyWebSocketAdvertisement()
        {
            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            var transport = new Phase90FakeTransport();
            var session = new FoxgloveSession("phase90-session", transport, schemaRegistry: registry);
            session.EnableCdr();
            transport.SimulateConnect(1);

            var serverInfo = JObject.Parse(transport.LastSentText);
            var encodings = serverInfo["supportedEncodings"]?.ToObject<string[]>();
            Check(encodings != null && encodings.Contains("json") && encodings.Contains("cdr"),
                "90D-1: session serverInfo includes cdr when enabled");

            session.RegisterRos2MsgSchemaChannel(1, "/phase90/pointcloud", "foxglove_msgs/msg/PointCloud");
            var channel = FirstAdvertisedChannel(transport.LastBroadcastText);
            Check(channel?["encoding"]?.ToString() == "cdr",
                "90D-2: ROS2 msg channel advertises cdr message encoding");
            Check(channel?["schemaName"]?.ToString() == "foxglove_msgs/msg/PointCloud",
                "90D-3: ROS2 msg channel advertises ROS2 schema name");
            Check(channel?["schemaEncoding"]?.ToString() == "ros2msg",
                "90D-4: ROS2 msg channel advertises ros2msg schemaEncoding");
            Check(channel?["schema"]?.ToString()?.Contains("MSG: geometry_msgs/Pose") == true,
                "90D-5: ROS2 msg channel advertises merged dependency schema text");

            var cdrRequiresExplicitSchemaEncoding = false;
            try
            {
                session.RegisterSchemaChannel(2, "/phase90/implicit_cdr", "foxglove_msgs/msg/PointCloud", "cdr");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("explicit schemaEncoding"))
            {
                cdrRequiresExplicitSchemaEncoding = true;
            }

            Check(cdrRequiresExplicitSchemaEncoding,
                "90D-6: generic CDR registration requires explicit schemaEncoding");
        }

        private static void VerifyRuntimeWrapper()
        {
            var transport = new Phase90FakeTransport();
            using var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase90-runtime", "127.0.0.1", 9090);
            transport.SimulateConnect(1);

            var serverInfo = JObject.Parse(transport.LastSentText);
            var encodings = serverInfo["supportedEncodings"]?.ToObject<string[]>();
            Check(encodings != null && encodings.Contains("json") && encodings.Contains("cdr"),
                "90E-1: runtime serverInfo includes json and cdr after optional ROS2 schema setup");

            runtime.RegisterRos2MsgSchemaChannel(1, "/phase90/runtime_pointcloud", "foxglove_msgs/msg/PointCloud");
            var channel = FirstAdvertisedChannel(transport.LastBroadcastText);
            Check(channel?["encoding"]?.ToString() == "cdr"
                  && channel?["schemaEncoding"]?.ToString() == "ros2msg"
                  && channel?["schemaName"]?.ToString() == "foxglove_msgs/msg/PointCloud",
                "90E-2: runtime RegisterRos2MsgSchemaChannel forwards to session helper");
        }

        private static void VerifyMcapSchemaAndChannel()
        {
            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            var transport = new Phase90FakeTransport();
            var session = new FoxgloveSession("phase90-mcap", transport, schemaRegistry: registry);

            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            session.SetRecorder(recorder);
            session.RegisterRos2MsgSchemaChannel(1, "/phase90/pointcloud", "foxglove_msgs/msg/PointCloud");
            recorder.Close();

            stream.Position = 0;
            var summary = new McapReader(stream).ReadSummary();
            Check(summary.Schemas.Count == 1,
                "90F-1: MCAP contains one ROS2 msg schema record");
            Check(summary.Schemas[0].Name == "foxglove_msgs/msg/PointCloud",
                "90F-2: MCAP schema name is ROS2 interface name");
            Check(summary.Schemas[0].Encoding == "ros2msg",
                "90F-3: MCAP schema encoding is ros2msg");
            Check(Encoding.UTF8.GetString(summary.Schemas[0].Data).Contains("MSG: geometry_msgs/Pose"),
                "90F-4: MCAP schema data contains merged dependencies");
            Check(summary.Channels.Count == 1
                  && summary.Channels[0].Topic == "/phase90/pointcloud"
                  && summary.Channels[0].MessageEncoding == "cdr",
                "90F-5: MCAP channel preserves cdr message encoding");
        }

        private static void VerifyBoundary()
        {
            var runtimeRoot = Path.Combine(Phase16Validation.FindRepoRoot(), "Packages", "dev.unity2foxglove.sdk", "Runtime");
            var runtimeText = string.Join("\n", Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
            var componentsRoot = Path.Combine(runtimeRoot, "Components");
            var componentText = string.Join("\n", Directory.EnumerateFiles(componentsRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
            var catalog = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/FoxgloveRos2MsgSchemaCatalog.cs");

            Check(!runtimeText.Contains("class CdrWriter") && !runtimeText.Contains("Ros2CdrPublisher"),
                "90G-1: Phase90 does not introduce CDR writer or CDR publisher");
            Check(!componentText.Contains("PublisherEffectiveEncoding.Cdr"),
                "90G-2: Phase90 does not add publisher CDR output mode");
            Check(!catalog.Contains("HasDedicatedUnityPublisher"),
                "90G-3: ROS2 catalog does not imply dedicated ROS2 CDR publisher support");

            var pointCloudPublisher = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var cameraPublisher = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Check(!pointCloudPublisher.Contains("Ros2") && !pointCloudPublisher.Contains("Cdr")
                  && !cameraPublisher.Contains("Ros2") && !cameraPublisher.Contains("Cdr"),
                "90G-4: Phase90 does not change camera or point-cloud publisher output modes");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private static JToken FirstAdvertisedChannel(string json)
        {
            var adv = JObject.Parse(json);
            return (adv["channels"] as JArray)?[0];
        }

        private static string ComputeSourceTreeSha256(string sourceRoot)
        {
            using var sha = SHA256.Create();
            foreach (var path in Directory.GetFiles(sourceRoot, "*.msg").OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var nameBytes = Encoding.UTF8.GetBytes(Path.GetFileName(path));
                sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
                sha.TransformBlock(new byte[] { 0 }, 0, 1, null, 0);
                var fileBytes = File.ReadAllBytes(path);
                sha.TransformBlock(fileBytes, 0, fileBytes.Length, null, 0);
                sha.TransformBlock(new byte[] { 0 }, 0, 1, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }

        private sealed class Phase90FakeTransport : IFoxgloveTransport
        {
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public bool IsRunning { get; private set; }
            public string LastSentText;
            public string LastBroadcastText;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) => LastBroadcastText = json;
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) => LastSentText = json;
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() { }
            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
        }
    }
}
