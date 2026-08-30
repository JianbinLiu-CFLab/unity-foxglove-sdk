// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Protocol;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Protocol")]
    public sealed class BinaryEncodingStrictnessTests
    {
        [Fact]
        public void ServiceEncodingRejectsMalformedUtf8()
        {
            AssertServiceEncodingRejected(new byte[] { 0xff });
            AssertServiceEncodingRejected(new byte[] { 0x80 });
            AssertServiceEncodingRejected(new byte[] { 0xc0, 0xaf });

            var valid = Encoding.UTF8.GetBytes("编码");
            var frame = BuildServiceRequest(valid);
            Assert.True(BinaryEncoding.TryDecodeClientServiceCallRequest(
                frame, out _, out _, out var encoding, out var payload));
            Assert.Equal("编码", encoding);
            Assert.Empty(payload);
        }

        [Fact]
        public void PlaybackRequestIdRejectsMalformedUtf8AndTrailingBytes()
        {
            AssertPlaybackRequestIdRejected(new byte[] { 0xff });
            AssertPlaybackRequestIdRejected(new byte[] { 0x80 });
            AssertPlaybackRequestIdRejected(new byte[] { 0xc0, 0xaf });

            var valid = Encoding.UTF8.GetBytes("播放");
            Assert.False(TryDecodePlayback(BuildPlaybackRequest(valid, trailingByteCount: 1), out _));

            var boundary = Encoding.UTF8.GetBytes(new string('é', 128));
            Assert.Equal(BinaryEncoding.MaxPlaybackRequestIdBytes, boundary.Length);
            Assert.True(TryDecodePlayback(BuildPlaybackRequest(boundary), out var requestId));
            Assert.Equal(new string('é', 128), requestId);
        }

        [Fact]
        public void AcceptedSubprotocolsCannotBeMutatedThroughThePublicSurface()
        {
            var accepted = Assert.IsAssignableFrom<IList<string>>(Subprotocol.Accepted);
            var original = accepted[0];
            try
            {
                Assert.Throws<NotSupportedException>(() => accepted[0] = "phase187-mutated");
            }
            finally
            {
                if (!string.Equals(accepted[0], original, StringComparison.Ordinal))
                    accepted[0] = original;
            }

            Assert.Equal(Subprotocol.SdkV1, accepted[0]);
            Assert.Equal(Subprotocol.WebSocketV1, accepted[1]);
        }

        [Fact]
        public void CustomServiceEncodingsDoNotCreateAnUnboundedStaticCache()
        {
            Assert.DoesNotContain(
                typeof(BinaryEncoding).GetFields(BindingFlags.Static | BindingFlags.NonPublic),
                field => field.FieldType.IsGenericType
                         && field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>));

            for (var i = 0; i < 32; i++)
            {
                var encoding = "phase187-custom-" + i;
                var frame = BinaryEncoding.EncodeServerServiceCallResponse(1, 2, encoding, Array.Empty<byte>());
                var length = checked((int)BinaryEncoding.ReadU32LE(frame, 9));
                Assert.Equal(encoding, Encoding.UTF8.GetString(frame, 13, length));
            }
        }

        [Fact]
        public void DataTimestampNanosecondOverflowLeavesThePreviousStateIntact()
        {
            var timestamp = new DataTimestamp { Sec = ulong.MaxValue, Nsec = 0 };

            Assert.Throws<ArgumentOutOfRangeException>(() => timestamp.Nsec = 1_000_000_000U);

            Assert.Equal(ulong.MaxValue, timestamp.Sec);
            Assert.Equal(0U, timestamp.Nsec);
        }

        [Fact]
        public void ServerMessageFrameLengthRejectsIntegerOverflow()
        {
            var maximumPayload = int.MaxValue - BinaryEncoding.ServerMessageDataHeaderLength;

            Assert.Equal(int.MaxValue, BinaryEncoding.GetServerMessageDataFrameLength(maximumPayload));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BinaryEncoding.GetServerMessageDataFrameLength(maximumPayload + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BinaryEncoding.GetServerMessageDataFrameLength(int.MaxValue));
        }

        private static void AssertServiceEncodingRejected(byte[] encoding)
        {
            Assert.False(BinaryEncoding.TryDecodeClientServiceCallRequest(
                BuildServiceRequest(encoding), out _, out _, out _, out _));
        }

        private static void AssertPlaybackRequestIdRejected(byte[] requestId)
        {
            Assert.False(TryDecodePlayback(BuildPlaybackRequest(requestId), out _));
        }

        private static byte[] BuildServiceRequest(byte[] encoding)
        {
            var frame = new byte[13 + encoding.Length];
            frame[0] = ClientOpcode.ServiceCallRequest;
            BinaryEncoding.WriteU32LE(frame, 1, 1);
            BinaryEncoding.WriteU32LE(frame, 5, 2);
            BinaryEncoding.WriteU32LE(frame, 9, (uint)encoding.Length);
            Buffer.BlockCopy(encoding, 0, frame, 13, encoding.Length);
            return frame;
        }

        private static byte[] BuildPlaybackRequest(byte[] requestId, int trailingByteCount = 0)
        {
            var frame = new byte[19 + requestId.Length + trailingByteCount];
            frame[0] = ClientOpcode.PlaybackControlRequest;
            frame[1] = 1;
            BinaryEncoding.WriteF32LE(frame, 2, 1f);
            frame[6] = 0;
            BinaryEncoding.WriteU64LE(frame, 7, 0);
            BinaryEncoding.WriteU32LE(frame, 15, (uint)requestId.Length);
            Buffer.BlockCopy(requestId, 0, frame, 19, requestId.Length);
            return frame;
        }

        private static bool TryDecodePlayback(byte[] frame, out string requestId)
        {
            return BinaryEncoding.TryDecodePlaybackControlRequest(
                frame, out _, out _, out _, out _, out requestId);
        }
    }
}
