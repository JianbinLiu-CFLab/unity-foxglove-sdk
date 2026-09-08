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
        public void CompletedOutputBeforePacketizerDropKeepsItsEarlierTimestamp(int codec)
        {
            var sidecar = codec == 0
                ? (object)new FfmpegH264EncoderSidecar()
                : new FfmpegH265EncoderSidecar();
            var packetizer = codec == 0
                ? (object)new H264AnnexBAccessUnitPacketizer(24)
                : new H265AnnexBAccessUnitPacketizer(24);
            Set(sidecar, "_packetizer", packetizer);
            EnqueueTimestamp(sidecar, 100UL);
            EnqueueTimestamp(sidecar, 200UL);
            EnqueueTimestamp(sidecar, 300UL);

            Append(packetizer, codec == 0
                ? Concat(H264Nal(9, 1), H264Nal(1, 0xA), H264Nal(9, 1))
                : Concat(H265Nal(35, 1), H265Nal(0, 0xA), H265Nal(35, 1)));
            Append(packetizer, new byte[25]);
            Append(packetizer, codec == 0
                ? Concat(H264Nal(9, 1), H264Nal(1, 0xC), H264Nal(9, 1))
                : Concat(H265Nal(35, 1), H265Nal(0, 0xC), H265Nal(35, 1)));

            Drain(sidecar);

            var first = Dequeue(sidecar);
            var second = Dequeue(sidecar);
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(100UL, first.Value.TimestampNs);
            Assert.Equal(300UL, second.Value.TimestampNs);
            Assert.Null(Dequeue(sidecar));
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

        private static byte[] H264Nal(byte type, byte payload)
            => new[] { (byte)0, (byte)0, (byte)0, (byte)1, type, payload };

        private static byte[] H265Nal(byte type, byte payload)
            => new[] { (byte)0, (byte)0, (byte)0, (byte)1, (byte)(type << 1), (byte)1, payload };

        private static byte[] Concat(params byte[][] parts)
        {
            var length = 0;
            foreach (var part in parts)
                length += part.Length;

            var result = new byte[length];
            var offset = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }

            return result;
        }

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
        {
            for (var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                var path = System.IO.Path.Combine(directory.FullName, relativePath);
                if (System.IO.File.Exists(path))
                    return System.IO.File.ReadAllText(path);
            }

            throw new System.IO.DirectoryNotFoundException("Could not locate repository file " + relativePath);
        }
    }
}
