// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Reflection;
using Foxglove.Schemas.Video;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    public sealed class VideoTimestampIntegrityTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void PacketizerDropConsumesItsCaptureTimestampBeforeNextOutput(int codec)
        {
            var sidecar = codec == 0
                ? (object)new FfmpegH264EncoderSidecar()
                : new FfmpegH265EncoderSidecar();
            var packetizer = codec == 0
                ? (object)new H264AnnexBAccessUnitPacketizer(32)
                : new H265AnnexBAccessUnitPacketizer(32);
            Set(sidecar, "_packetizer", packetizer);
            EnqueueTimestamp(sidecar, 100UL);
            EnqueueTimestamp(sidecar, 200UL);

            Append(packetizer, new byte[33]);
            Drain(sidecar);

            Append(packetizer, codec == 0
                ? new byte[] { 0, 0, 0, 1, 9, 0, 0, 0, 1, 1, 1 }
                : new byte[] { 0, 0, 0, 1, 0x46, 0, 0, 0, 1, 0x02, 0 });
            Append(packetizer, codec == 0
                ? new byte[] { 0, 0, 0, 1, 9, 0 }
                : new byte[] { 0, 0, 0, 1, 0x46, 0 });
            Drain(sidecar);

            var output = Dequeue(sidecar);
            Assert.NotNull(output);
            Assert.Equal(200UL, output.Value.TimestampNs);
            Assert.Equal(0, PendingTimestampCount(sidecar));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void TimestampUnderflowRejectsUnpairedAccessUnit(int codec)
        {
            var sidecar = codec == 0
                ? (object)new FfmpegH264EncoderSidecar()
                : codec == 1 ? new FfmpegH265EncoderSidecar() : new OpenH264EncoderSidecar();

            if (codec == 0)
                ((FfmpegH264EncoderSidecar)sidecar).AcceptEncodedAccessUnitForTests(new byte[] { 1 });
            else if (codec == 1)
                ((FfmpegH265EncoderSidecar)sidecar).AcceptEncodedAccessUnitForTests(new byte[] { 1 });
            else
                ((OpenH264EncoderSidecar)sidecar).AcceptHelperAccessUnit(new byte[] { 1 });

            Assert.Null(Dequeue(sidecar));
            Assert.Equal(0, PendingTimestampCount(sidecar));
        }

        [Fact]
        public void TimestampedDrainPathsRejectZeroBeforePublicationFallback()
        {
            var session = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarSession.cs");
            var legacy = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs");
            Assert.Contains("if (accessUnit.TimestampNs == 0UL)", session, StringComparison.Ordinal);
            Assert.Contains("if (accessUnit.TimestampNs == 0UL)", legacy, StringComparison.Ordinal);
        }

        private static void Append(object packetizer, byte[] data)
            => packetizer.GetType().GetMethod("Append", new[] { typeof(byte[]) }).Invoke(packetizer, new object[] { data });

        private static void Drain(object sidecar)
            => sidecar.GetType().GetMethod("DrainPacketizer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sidecar, null);

        private static void EnqueueTimestamp(object sidecar, ulong timestamp)
            => sidecar.GetType().GetMethod("EnqueueTimestampForTests", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sidecar, new object[] { timestamp });

        private static EncodedVideoAccessUnit? Dequeue(object sidecar)
        {
            var method = sidecar.GetType().GetMethod("TryDequeueEncodedAccessUnit");
            var args = new object[] { null };
            if (!(bool)method.Invoke(sidecar, args))
                return null;
            return (EncodedVideoAccessUnit)args[0];
        }

        private static int PendingTimestampCount(object sidecar)
            => (int)sidecar.GetType().GetProperty("PendingTimestampCountForTests", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sidecar);

        private static object Get(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static string Read(string relativePath)
            => System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../../", relativePath));
    }
}
