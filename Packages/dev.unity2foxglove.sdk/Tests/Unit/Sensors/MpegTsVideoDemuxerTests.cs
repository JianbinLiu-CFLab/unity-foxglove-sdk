// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Foxglove.Schemas.Video;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    public sealed class MpegTsVideoDemuxerTests
    {
        [Fact]
        public void ParsesSplitPesWithPtsAndPreservesPayload()
        {
            var demuxer = new MpegTsVideoDemuxer();
            var stream = BuildStream(0x1B, new[] {
                new PesSample(90000, new byte[] { 0, 0, 0, 1, 0x65, 1 }),
                new PesSample(93000, new byte[] { 0, 0, 0, 1, 0x41, 2 }) });

            demuxer.Append(stream, 0, 137);
            demuxer.Append(stream, 137, stream.Length - 137);
            demuxer.Flush();

            Assert.True(demuxer.TryDequeue(out var first));
            Assert.Equal(90000L, first.Pts90k);
            Assert.Equal(new byte[] { 0, 0, 0, 1, 0x65, 1 }, first.Data);
            Assert.True(demuxer.TryDequeue(out var second));
            Assert.Equal(93000L, second.Pts90k);
            Assert.Equal(new byte[] { 0, 0, 0, 1, 0x41, 2 }, second.Data);
            Assert.False(demuxer.TryDequeue(out _));
        }

        [Fact]
        public void IgnoresNonVideoPesAndAcceptsAdaptationFields()
        {
            var demuxer = new MpegTsVideoDemuxer();
            var stream = BuildStream(0x24, new[] {
                new PesSample(180000, new byte[] { 1, 2, 3 }) }, includeAdaptationField: true);

            demuxer.Append(stream, 0, stream.Length);
            demuxer.Flush();

            Assert.True(demuxer.TryDequeue(out var output));
            Assert.Equal(180000L, output.Pts90k);
            Assert.Equal(new byte[] { 1, 2, 3 }, output.Data);
        }

        [Fact]
        public void DropsTruncatedPesAndHandlesPtsWrapWithoutThrowing()
        {
            var demuxer = new MpegTsVideoDemuxer();
            var stream = BuildStream(0x1B, new[] {
                new PesSample((1L << 33) - 10, new byte[] { 9 }),
                new PesSample(5, new byte[300]) });

            // Keep the first packet of the second PES, but omit its continuation
            // packet. Flush must discard that incomplete declared PES length.
            var firstPesPacketCount = 3;
            demuxer.Append(stream, 0, (firstPesPacketCount + 1) * 188);
            demuxer.Flush();

            Assert.True(demuxer.TryDequeue(out var output));
            Assert.Equal((1L << 33) - 10, output.Pts90k);
            Assert.Equal(new byte[] { 9 }, output.Data);
            Assert.False(demuxer.TryDequeue(out _));
        }

        [Fact]
        public void DropsUnboundedPesAfterTheMaximumPayloadSize()
        {
            var demuxer = new MpegTsVideoDemuxer(maxPesBytes: 1024);
            var payload = new byte[2048];
            var packets = new List<byte[]> { BuildPat(0x0100), BuildPmt(0x0100, 0x1B, 0x0101) };
            packets.AddRange(BuildPes(0x0101, 90000, payload, adaptation: false, unbounded: true));
            var stream = FlattenPackets(packets);

            demuxer.Append(stream, 0, stream.Length);
            demuxer.Flush();

            Assert.False(demuxer.TryDequeue(out _));
            Assert.Equal(1, demuxer.DroppedOversizedPes);
        }

        [Fact]
        public void IgnoresVideoPesUntilLatePatAndPmtAreAvailable()
        {
            var demuxer = new MpegTsVideoDemuxer();
            var earlyPes = new List<byte[]>(BuildPes(0x0101, 90000, new byte[] { 1 }, false))[0];
            var latePes = new List<byte[]>(BuildPes(0x0101, 93000, new byte[] { 2 }, false))[0];
            var packets = new List<byte[]>
            {
                earlyPes,
                BuildPat(0x0100),
                BuildPmt(0x0100, 0x1B, 0x0101),
                latePes
            };

            var stream = FlattenPackets(packets);
            demuxer.Append(stream, 0, stream.Length);
            demuxer.Flush();

            Assert.True(demuxer.TryDequeue(out var output));
            Assert.Equal(93000L, output.Pts90k);
            Assert.Equal(new byte[] { 2 }, output.Data);
            Assert.False(demuxer.TryDequeue(out _));
        }
        internal readonly struct PesSample
        {
            public PesSample(long pts, byte[] data)
            {
                Pts = pts;
                Data = data;
            }

            public long Pts { get; }
            public byte[] Data { get; }
        }

        internal static byte[] BuildStream(
            byte streamType,
            IReadOnlyList<PesSample> samples,
            bool includeAdaptationField = false,
            bool unbounded = false)
        {
            var packets = new List<byte[]>();
            packets.Add(BuildPat(0x0100));
            packets.Add(BuildPmt(0x0100, streamType, 0x0101));
            foreach (var sample in samples)
                packets.AddRange(BuildPes(0x0101, sample.Pts, sample.Data, includeAdaptationField, unbounded));

            return FlattenPackets(packets);
        }

        private static byte[] FlattenPackets(IReadOnlyList<byte[]> packets)
        {
            var length = packets.Count * 188;
            var result = new byte[length];
            var offset = 0;
            foreach (var packet in packets)
            {
                Buffer.BlockCopy(packet, 0, result, offset, packet.Length);
                offset += packet.Length;
            }

            return result;
        }
        private static byte[] BuildPat(int pmtPid)
        {
            var section = new byte[]
            { 0x00, 0xB0, 0x0D, 0x00, 0x01, 0xC1, 0x00, 0x00, 0x00, 0x01,
              (byte)(0xE0 | (pmtPid >> 8)), (byte)pmtPid, 0, 0, 0, 0 };
            return BuildPsiPacket(0, section);
        }

        private static byte[] BuildPmt(int pmtPid, byte streamType, int videoPid)
        {
            var section = new byte[]
            { 0x02, 0xB0, 0x12, 0x00, 0x01, 0xC1, 0x00, 0x00,
              (byte)(0xE0 | (videoPid >> 8)), (byte)videoPid, 0xF0, 0x00,
              streamType, (byte)(0xE0 | (videoPid >> 8)), (byte)videoPid, 0xF0, 0x00,
              0, 0, 0, 0 };
            return BuildPsiPacket(pmtPid, section);
        }

        private static byte[] BuildPsiPacket(int pid, byte[] section)
        {
            var packet = NewPacket(pid, true);
            packet[4] = 0;
            Buffer.BlockCopy(section, 0, packet, 5, section.Length);
            return packet;
        }

        private static IEnumerable<byte[]> BuildPes(int pid, long pts, byte[] payload, bool adaptation, bool unbounded = false)
        {
            var header = new byte[14];
            header[0] = 0; header[1] = 0; header[2] = 1; header[3] = 0xE0;
            var pesLength = 8 + payload.Length;
            if (!unbounded)
            {
                header[4] = (byte)(pesLength >> 8); header[5] = (byte)pesLength;
            }
            header[6] = 0x80; header[7] = 0x80; header[8] = 5;
            WritePts(header, 9, pts);
            var bytes = new byte[header.Length + payload.Length];
            Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
            Buffer.BlockCopy(payload, 0, bytes, header.Length, payload.Length);

            var first = NewPacket(pid, true, adaptation);
            var firstOffset = SetPayload(first, 0, bytes, 0, Math.Min(bytes.Length, PayloadCapacity(first, adaptation)));
            yield return first;
            var offset = firstOffset;
            while (offset < bytes.Length)
            {
                var packet = NewPacket(pid, false);
                var copied = SetPayload(packet, 0, bytes, offset, Math.Min(bytes.Length - offset, 184));
                offset += copied;
                yield return packet;
            }
        }

        private static int SetPayload(byte[] packet, int unused, byte[] source, int sourceOffset, int count)
        {
            var payloadOffset = packet[3] == 0x30 ? 5 + packet[4] : 4;
            Buffer.BlockCopy(source, sourceOffset, packet, payloadOffset, count);
            return count;
        }

        private static int PayloadCapacity(byte[] packet, bool adaptation)
            => adaptation ? 188 - (6 + packet[4]) : 184;

        private static byte[] NewPacket(int pid, bool payloadStart, bool adaptation = false)
        {
            var packet = new byte[188];
            Array.Fill(packet, (byte)0xFF);
            packet[0] = 0x47;
            packet[1] = (byte)((payloadStart ? 0x40 : 0) | ((pid >> 8) & 0x1F));
            packet[2] = (byte)pid;
            packet[3] = (byte)(adaptation ? 0x30 : 0x10);
            if (adaptation)
            {
                packet[4] = 1;
                packet[5] = 0;
            }
            return packet;
        }

        private static void WritePts(byte[] target, int offset, long pts)
        {
            var value = pts & ((1L << 33) - 1);
            target[offset] = (byte)(0x21 | ((value >> 29) & 0x0E));
            target[offset + 1] = (byte)(value >> 22);
            target[offset + 2] = (byte)(0x01 | ((value >> 14) & 0xFE));
            target[offset + 3] = (byte)(value >> 7);
            target[offset + 4] = (byte)(0x01 | (value << 1));
        }
    }
}
