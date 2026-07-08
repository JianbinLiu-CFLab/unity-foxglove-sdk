// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-086 Unity review regression checks.

using System;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-086")]
    [Trait("Domain", "UnityReview")]
    public sealed class Phase173086ReviewTests
    {
        [Fact]
        public void Crc32ByteArrayOverloadRejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => Crc32Helper.Compute((byte[])null));
        }

        [Fact]
        public void McapDecodedMessageProblemsAreLazyUntilNeeded()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDecodedDataLoaderTypes.cs");
            var decoded = new McapDecodedMessage
            {
                Payload = McapDecodedPayload.Raw(Array.Empty<byte>())
            };

            Assert.Empty(decoded.Problems);
            decoded.Problems.Add(new McapDecodeProblem { Code = "phase173-086" });
            Assert.Single(decoded.Problems);
            Assert.Contains("private List<McapDecodeProblem> _problems;", source, StringComparison.Ordinal);
            Assert.Contains("get => _problems ?? (_problems = new List<McapDecodeProblem>())", source, StringComparison.Ordinal);
            Assert.Contains("set => _problems = value;", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RemoteMcapHttpServerDisposesStartupTokenOnFailedStartAndExposesAsyncProbe()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapHttpServer.cs");
            var constructor = TestSources.Slice(source, "private RemoteMcapHttpServer(RemoteMcapHttpOptions options)", "/// <summary>Options used");

            Assert.Contains("var stop = new CancellationTokenSource();", constructor, StringComparison.Ordinal);
            Assert.Contains("stop.Dispose();", constructor, StringComparison.Ordinal);
            Assert.Contains("listener?.Close();", constructor, StringComparison.Ordinal);
            Assert.Contains("can block for up to 500 ms", source, StringComparison.Ordinal);
            Assert.Contains("public static async Task<bool> IsListeningAsync", source, StringComparison.Ordinal);
            Assert.Contains("Task.WhenAny(connect, timeout)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LidarScanDiagnosticsUsesSingleScanCounterForIntervalAndAverages()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/LidarScanDiagnostics.cs");

            Assert.DoesNotContain("private int _ticks;", source, StringComparison.Ordinal);
            Assert.Contains("private int _scans;", source, StringComparison.Ordinal);
            Assert.Contains("if (_scans < LogIntervalTicks)", source, StringComparison.Ordinal);
            Assert.Contains("var divisor = Math.Max(1, _scans);", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ProtoCatalogClrTypeLookupUsesConsistentLastWriteWinsMap()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Registry/FoxgloveProtoSchemaCatalog.cs");
            var method = TestSources.ExtractMethod(source, "private static Dictionary<Type, FoxgloveProtoSchemaCatalogEntry> BuildEntriesByClrType");

            Assert.Contains("result[entry.ClrType] = entry;", method, StringComparison.Ordinal);
            Assert.DoesNotContain("result.Add(entry.ClrType", method, StringComparison.Ordinal);
        }

        [Fact]
        public void FfmpegH265CreateStartInfoStillEnforcesValidation()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderOptions.cs");
            var method = TestSources.ExtractMethod(source, "public ProcessStartInfo CreateStartInfo()");

            Assert.Contains("if (!Validate(out var error))", method, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => new Foxglove.Schemas.Video.FfmpegH265EncoderOptions
            {
                Preset = "invalid-preset"
            }.CreateStartInfo());
        }
    }
}
