// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: OpenH264 helper protocol timestamp behavior.

using System.Collections.Concurrent;
using System.Reflection;
using Foxglove.Schemas.Video;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "140-33")]
    [Trait("Domain", "Sensors")]
    public sealed class OpenH264EncoderSidecarTests
    {
        [Fact]
        public void EmptyAccessUnitIsRejectedWithoutConsumingTimestamp()
        {
            var sidecar = new OpenH264EncoderSidecar();
            PendingTimestamps(sidecar).Enqueue(100UL);
            PendingTimestamps(sidecar).Enqueue(200UL);

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
            PendingTimestamps(sidecar).Enqueue(100UL);
            PendingTimestamps(sidecar).Enqueue(200UL);

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
            var pending = PendingTimestamps(sidecar);

            for (var i = 1; i <= 5; i++)
            {
                pending.Enqueue((ulong)i * 100UL);
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

            pending.Enqueue(600UL);
            sidecar.AcceptHelperAccessUnit(new byte[] { 6 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(600UL, accessUnit.TimestampNs);
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

        private static ConcurrentQueue<ulong> PendingTimestamps(OpenH264EncoderSidecar sidecar)
        {
            var field = typeof(OpenH264EncoderSidecar).GetField(
                "_encodedFrameTimestamps",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (ConcurrentQueue<ulong>)field.GetValue(sidecar);
        }

        private static string QuoteArgument(string value)
        {
            var method = typeof(OpenH264EncoderOptions).GetMethod(
                "QuoteArgument",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (string)method.Invoke(null, new object[] { value });
        }
    }
}
