// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-089 review regressions for runtime robustness findings.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "173-089")]
    [Trait("Domain", "Review")]
    public sealed class Phase173089ReviewTests
    {
        [Fact]
        public void RemoteManifestMapperToleratesNullSchemasBeforeSorting()
        {
            var initialization = new McapDataLoaderInitialization();
            initialization.Schemas.Add(new McapDataLoaderSchema
            {
                SchemaId = 2,
                Name = "Second",
                Encoding = "jsonschema",
                Data = new byte[] { 2 }
            });
            initialization.Schemas.Add(null);
            initialization.Schemas.Add(new McapDataLoaderSchema
            {
                SchemaId = 1,
                Name = "First",
                Encoding = "jsonschema",
                Data = new byte[] { 1 }
            });

            var manifest = RemoteMcapManifestMapper.FromInitialization(initialization, "review", "source", "/mcap");
            var schemas = manifest.Sources.Single().Schemas;

            Assert.Equal(new[] { 1, 2 }, schemas.Select(schema => (int)schema.Id).ToArray());
        }

        [Fact]
        public void SessionLogsChannelDescriptorOverwrite()
        {
            var logger = new RecordingLogger();
            using var session = new FoxgloveSession(
                "review-session",
                new NoopTransport(),
                schemaRegistry: new DefaultSchemaRegistry(),
                logger: logger);

            session.RegisterChannel(Channel(7, "/old", "Old.Schema"));
            session.RegisterChannel(Channel(7, "/new", "New.Schema"));

            var warning = Assert.Single(logger.Warnings);
            Assert.Contains("Channel id 7 overwritten", warning, StringComparison.Ordinal);
            Assert.Contains("/old", warning, StringComparison.Ordinal);
            Assert.Contains("/new", warning, StringComparison.Ordinal);
        }

        [Fact]
        public void SkeletonValidationUsesLocalPassCount()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/SkeletonValidation.cs");

            Assert.DoesNotContain("static int _passCount", source, StringComparison.Ordinal);
            Assert.Contains("var passCount = 0;", source, StringComparison.Ordinal);
            Assert.Contains("ref int passCount", source, StringComparison.Ordinal);
        }

        [Fact]
        public void McapRecorderDisposeAfterCloseDoesNotAppendBytes()
        {
            using var stream = new MemoryStream();
            var recorder = new McapRecorder(stream, leaveOpen: true);
            recorder.AddChannel(1, "/review/mcap", "json", "Review.Schema", "jsonschema", "{}");
            recorder.WriteMessage(1, 10UL, new byte[] { 1, 2, 3 });

            recorder.Close();
            var closedLength = stream.Length;
            recorder.Dispose();

            Assert.Equal(closedLength, stream.Length);
            AssertMcapMagic(stream);
        }

        [Fact]
        public void ChunkReaderUsesOneRawRecordParserForMessagesAndPrivateRecords()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapChunkReader.cs");

            Assert.Contains("EnumerateRawRecords", source, StringComparison.Ordinal);
            Assert.Single(FindAll(source, "Chunk inner record is truncated."));
            Assert.Single(FindAll(source, "MCAP opcode 0x00 is invalid inside chunk."));
            Assert.Single(FindAll(source, "Chunk inner record length exceeds int.MaxValue."));
            Assert.Single(FindAll(source, "Chunk inner record content is truncated."));
        }

        [Fact]
        public void UnknownPointCloudOutputModeFailsClosed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PointCloudOutputProfile.ForMode((PointCloudOutputMode)999));
        }

        [Fact]
        public void FoxRunTriggerSmokeLogsAreConditional()
        {
            var source = Text("Unity2Foxglove/Assets/Scripts/FoxRun/FoxRunTriggerTelemetrySmoke.cs");

            Assert.DoesNotContain("Debug.Log($", source, StringComparison.Ordinal);
            Assert.Contains("[System.Diagnostics.Conditional(\"UNITY_EDITOR\")]", source, StringComparison.Ordinal);
            Assert.Contains("[System.Diagnostics.Conditional(\"DEVELOPMENT_BUILD\")]", source, StringComparison.Ordinal);
        }

        private static AdvertiseChannel Channel(uint id, string topic, string schemaName)
            => new AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = "json",
                SchemaName = schemaName,
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            };

        private static void AssertMcapMagic(MemoryStream stream)
        {
            var bytes = stream.ToArray();
            Assert.True(bytes.Length >= 16);
            Assert.Equal(new byte[] { 0x89, (byte)'M', (byte)'C', (byte)'A', (byte)'P', 0x30, 0x0D, 0x0A },
                bytes.Take(8).ToArray());
            Assert.Equal(new byte[] { 0x89, (byte)'M', (byte)'C', (byte)'A', (byte)'P', 0x30, 0x0D, 0x0A },
                bytes.Skip(bytes.Length - 8).Take(8).ToArray());
        }

        private static IEnumerable<int> FindAll(string text, string value)
        {
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                yield return index;
                index += value.Length;
            }
        }

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

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

            throw new DirectoryNotFoundException("Could not find repository root from test base directory.");
        }

        private sealed class RecordingLogger : IFoxgloveLogger
        {
            public readonly List<string> Warnings = new List<string>();
            public void LogWarning(string message) => Warnings.Add(message);
            public void LogError(string message) => throw new InvalidOperationException(message);
        }

        private sealed class NoopTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
        }
    }
}
