// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Video encoder sidecar timestamp behavior.

using System;
using System.Reflection;
using System.Collections;
using System.IO;
using Unity.FoxgloveSDK.Components;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Foxglove.Schemas.Video;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "140-33")]
    [Trait("Domain", "Sensors")]
    public sealed partial class OpenH264EncoderSidecarTests
    {
        [Fact]
        public void OpenH264AllocationContractSeparatesPooledInputFromOwnedOutput()
        {
            var rgbContract = OpenH264AllocationPolicy.Contract(rgbInput: true);
            var i420Contract = OpenH264AllocationPolicy.Contract(rgbInput: false);

            Assert.Equal(1, rgbContract.InputOwnershipCopies);
            Assert.Equal(1, rgbContract.PooledConversionBuffers);
            Assert.Equal(1, i420Contract.InputOwnershipCopies);
            Assert.Equal(0, i420Contract.PooledConversionBuffers);
            Assert.Equal(1, rgbContract.ProtocolHeaderAllocationsPerSession);
            Assert.Equal(1, rgbContract.EncodedOutputOwnershipAllocationsPerAccessUnit);
            Assert.Equal(12, OpenH264AllocationPolicy.ProtocolHeaderBytes);

            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs");
            Assert.Contains("OpenH264AllocationPolicy.ProtocolHeaderBytes", source, StringComparison.Ordinal);
            Assert.Contains("var payload = new byte[length];", source, StringComparison.Ordinal);
            Assert.Contains("ArrayPool<byte>.Shared.Rent", source, StringComparison.Ordinal);
        }

        [Fact]
        public void EmptyAccessUnitIsRejectedWithoutConsumingTimestamp()
        {
            var sidecar = new OpenH264EncoderSidecar();
            sidecar.EnqueueTimestampForTests(100UL);
            sidecar.EnqueueTimestampForTests(200UL);

            Assert.Throws<System.ArgumentException>(() => sidecar.AcceptHelperAccessUnit(System.Array.Empty<byte>()));
            sidecar.AcceptHelperAccessUnit(new byte[] { 1, 2, 3 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(100UL, accessUnit.TimestampNs);
            Assert.Equal(new byte[] { 1, 2, 3 }, accessUnit.Data);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Fact]
        public void SkipSentinelConsumesOnlySkippedTimestamp()
        {
            var sidecar = new OpenH264EncoderSidecar();
            sidecar.EnqueueTimestampForTests(100UL);
            sidecar.EnqueueTimestampForTests(200UL);

            sidecar.AcceptHelperSkippedAccessUnit();
            sidecar.AcceptHelperAccessUnit(new byte[] { 4, 5, 6 });

            Assert.Equal(1, sidecar.SkippedAccessUnits);
            Assert.Contains("skipped", sidecar.LastDiagnosticLine);
            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(200UL, accessUnit.TimestampNs);
            Assert.Equal(new byte[] { 4, 5, 6 }, accessUnit.Data);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Fact]
        public void FullOutputQueueConsumesDroppedTimestamp()
        {
            var sidecar = new OpenH264EncoderSidecar();

            for (var i = 1; i <= 5; i++)
            {
                sidecar.EnqueueTimestampForTests((ulong)i * 100UL);
                sidecar.AcceptHelperAccessUnit(new[] { (byte)i });
            }

            Assert.Equal(1, sidecar.DroppedOutputFrames);
            Assert.Equal(4, sidecar.OutputQueueDepth);
            Assert.Contains("output queue full", sidecar.LastDiagnosticLine);

            for (var i = 1; i <= 4; i++)
            {
                Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var queued));
                Assert.Equal((ulong)i * 100UL, queued.TimestampNs);
            }

            sidecar.EnqueueTimestampForTests(600UL);
            sidecar.AcceptHelperAccessUnit(new byte[] { 6 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(600UL, accessUnit.TimestampNs);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Fact]
        public void FfmpegH264FullOutputQueueConsumesDroppedTimestamp()
        {
            var sidecar = new FfmpegH264EncoderSidecar();

            for (var i = 1; i <= 5; i++)
            {
                sidecar.EnqueueTimestampForTests((ulong)i * 100UL);
                sidecar.AcceptEncodedAccessUnitForTests(new[] { (byte)i });
            }

            Assert.Equal(1, sidecar.AccessUnitsDropped);
            Assert.Equal(4, sidecar.OutputQueueDepth);
            Assert.Contains("output queue full", sidecar.LastDiagnosticLine);

            for (var i = 1; i <= 4; i++)
            {
                Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var queued));
                Assert.Equal((ulong)i * 100UL, queued.TimestampNs);
            }

            sidecar.EnqueueTimestampForTests(600UL);
            sidecar.AcceptEncodedAccessUnitForTests(new byte[] { 6 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(600UL, accessUnit.TimestampNs);
            Assert.Equal(0, sidecar.PendingTimestampCountForTests);
            Assert.Equal(5, sidecar.AccessUnitsProduced);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Fact]
        public void FfmpegH265FullOutputQueueConsumesDroppedTimestamp()
        {
            var sidecar = new FfmpegH265EncoderSidecar();

            for (var i = 1; i <= 5; i++)
            {
                sidecar.EnqueueTimestampForTests((ulong)i * 100UL);
                sidecar.AcceptEncodedAccessUnitForTests(new[] { (byte)i });
            }

            Assert.Equal(1, sidecar.AccessUnitsDropped);
            Assert.Equal(4, sidecar.OutputQueueDepth);
            Assert.Contains("output queue full", sidecar.LastDiagnosticLine);

            for (var i = 1; i <= 4; i++)
            {
                Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var queued));
                Assert.Equal((ulong)i * 100UL, queued.TimestampNs);
            }

            sidecar.EnqueueTimestampForTests(600UL);
            sidecar.AcceptEncodedAccessUnitForTests(new byte[] { 6 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(600UL, accessUnit.TimestampNs);
            Assert.Equal(0, sidecar.PendingTimestampCountForTests);
            Assert.Equal(5, sidecar.AccessUnitsProduced);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Theory]
        [InlineData(@"C:\OpenH264\openh264.dll", "\"C:\\OpenH264\\openh264.dll\"")]
        [InlineData(@"C:\OpenH264 Runtime\", "\"C:\\OpenH264 Runtime\\\\\"")]
        [InlineData("C:\\OpenH264\\quoted\"name.dll", "\"C:\\OpenH264\\quoted\\\"name.dll\"")]
        public void OpenH264ArgumentsUseWindowsCommandLineEscaping(string value, string expected)
        {
            Assert.Equal(expected, QuoteArgument(value));
        }

        [Fact]
        public void CameraVideoSidecarOptionsFactoryClampsGeometryAndRate()
        {
            var h264 = (FfmpegH264EncoderOptions)CreateOptions(
                "CreateH264Options",
                "",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);
            var h265 = (FfmpegH265EncoderOptions)CreateOptions(
                "CreateH265Options",
                "",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);
            var openH264 = (OpenH264EncoderOptions)CreateOptions(
                "CreateOpenH264Options",
                "",
                "",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);
            var mediaFoundation = (MediaFoundationH264EncoderOptions)CreateOptions(
                "CreateMediaFoundationH264Options",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);

            AssertPositiveVideoOptions(h264);
            AssertPositiveVideoOptions(h265);
            AssertPositiveVideoOptions(openH264);
            AssertPositiveVideoOptions(mediaFoundation);
        }

        [Fact]
        public void CameraVideoSidecarOptionsFactoryRoundsHalfFrameRatesAwayFromZero()
        {
            Assert.Equal(31, CameraVideoSidecarConfigFactory.ResolveFrameRate(30.5f));
        }

        [Fact]
        public void OpenH264StartInfoUsesTimestampProtocolV2()
        {
            var options = new OpenH264EncoderOptions
            {
                HelperExecutablePath = "openh264_probe_encoder",
                OpenH264DllPath = "openh264.dll"
            };

            var startInfo = options.CreateStartInfo();
            Assert.DoesNotContain("--protocol", startInfo.Arguments, StringComparison.Ordinal);
            Assert.Equal("2", startInfo.Environment["OPENH264_PROBE_PROTOCOL"]);
        }

        [Fact]
        public void ProtocolHeaderBufferCanBeReusedAcrossFrames()
        {
            var method = typeof(OpenH264EncoderSidecar).GetMethod(
                "PopulateProtocolHeader",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var header = new byte[12];
            using var stream = new MemoryStream();
            method.Invoke(
                null,
                new object[] { header, 0x0102030405060708UL, 0x0A0B0C0D });
            stream.Write(header, 0, header.Length);
            method.Invoke(
                null,
                new object[] { header, 9UL, 3 });
            stream.Write(header, 0, header.Length);

            Assert.Equal(
                new byte[]
                {
                    8, 7, 6, 5, 4, 3, 2, 1, 13, 12, 11, 10,
                    9, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0
                },
                stream.ToArray());

            var source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs"));
            Assert.Equal(1, CountOccurrences(source, "var protocolHeader = new byte[OpenH264AllocationPolicy.ProtocolHeaderBytes];"));
            var writerStart = source.IndexOf("private async Task RunStdinWriter", StringComparison.Ordinal);
            var writerEnd = source.IndexOf("private Task RunStdoutReader", writerStart, StringComparison.Ordinal);
            var writer = source.Substring(writerStart, writerEnd - writerStart);
            Assert.Equal(1, CountOccurrences(writer, "new byte[OpenH264AllocationPolicy.ProtocolHeaderBytes]"));
            Assert.Contains("PopulateProtocolHeader(protocolHeader", writer, StringComparison.Ordinal);
            Assert.Contains("WriteAsync(protocolHeader", writer, StringComparison.Ordinal);
            Assert.DoesNotContain("WriteProtocolHeaderAsync", source, StringComparison.Ordinal);
            var headerWriterStart = source.IndexOf("private static void PopulateProtocolHeader", StringComparison.Ordinal);
            var headerWriterEnd = source.IndexOf("private static ulong ReadUInt64LittleEndian", headerWriterStart, StringComparison.Ordinal);
            var headerWriter = source.Substring(headerWriterStart, headerWriterEnd - headerWriterStart);
            Assert.DoesNotContain("new byte[12]", headerWriter, StringComparison.Ordinal);
        }

        [Fact]
        public void RawSnapshotOwnsPixelsBeforeSubscriberMutation()
        {
            var source = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
            var frame = CameraRawImageFrameBuilder.BuildRgb8(
                1UL,
                "frame",
                2,
                2,
                source,
                flipVertical: false);

            source[0] = 99;
            var data = (byte[])frame.GetType().GetProperty("Data").GetValue(frame);

            Assert.NotSame(source, data);
            Assert.Equal(1, data[0]);
        }
        [Fact]
        public void OpenH264TimestampTestSeamAvoidsPrivateFieldReflection()
        {
            var sidecar = new OpenH264EncoderSidecar();

            sidecar.EnqueueTimestampForTests(123UL);

            Assert.Equal(1, sidecar.PendingTimestampCountForTests);
        }

        [Fact]
        public async Task LegacyHelperFallbackCompletesWithoutProtocolMarker()
        {
            var sidecar = new OpenH264EncoderSidecar();
            SetField(sidecar, "_protocolNegotiationTimeoutMs", 50);
            var wait = typeof(OpenH264EncoderSidecar).GetMethod(
                "WaitForProtocolNegotiation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(wait);

            var task = (Task)wait.Invoke(sidecar, new object[] { CancellationToken.None });
            await task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(1, (int)GetField(sidecar, "_helperProtocolVersion"));
            Assert.Equal(1, (int)GetField(sidecar, "_protocolNegotiationState"));
            Assert.Contains("legacy framing", sidecar.LastDiagnosticLine, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SlowHelperMarkerWithinNegotiationWindowSelectsTimestampProtocol()
        {
            var sidecar = new OpenH264EncoderSidecar();
            var wait = typeof(OpenH264EncoderSidecar).GetMethod(
                "WaitForProtocolNegotiation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var accept = typeof(OpenH264EncoderSidecar).GetMethod(
                "TryAcceptProtocolMarker",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(wait);
            Assert.NotNull(accept);

            // A cold helper can take longer than the previous 250 ms window to
            // advertise; the marker must still win over the legacy fallback.
            var task = (Task)wait.Invoke(sidecar, new object[] { CancellationToken.None });
            await Task.Delay(300);
            Assert.False(task.IsCompleted);
            Assert.True((bool)accept.Invoke(sidecar, null));
            await task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(2, (int)GetField(sidecar, "_helperProtocolVersion"));
            Assert.Equal(2, (int)GetField(sidecar, "_protocolNegotiationState"));
        }

    }
}
