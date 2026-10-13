// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first coverage for the live Bridge v2 publish session.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed partial class Ros2BridgeV2PublishSessionTests
    {
        [Fact]
        public void OversizedResponseLengthsAreTerminalFramingFaults()
        {
            var limits = U2R2ProtocolLimits.Default;
            var fixedHeader = new byte[16];
            fixedHeader[0] = (byte)'U';
            fixedHeader[1] = (byte)'2';
            fixedHeader[2] = (byte)'R';
            fixedHeader[3] = (byte)'2';
            fixedHeader[4] = U2R2ProtocolCodec.EnvelopeVersion;
            var oversizedHeader = checked(
                (uint)(limits.MaxHeaderBytes + 1));
            fixedHeader[8] = (byte)(oversizedHeader & 0xff);
            fixedHeader[9] = (byte)((oversizedHeader >> 8) & 0xff);
            fixedHeader[10] = (byte)((oversizedHeader >> 16) & 0xff);
            fixedHeader[11] = (byte)((oversizedHeader >> 24) & 0xff);

            var read = typeof(Ros2BridgeTcpClient).GetMethod(
                "ReadV2Frame",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(read);
            using var stream = new MemoryStream(fixedHeader);
            var invocation = Assert.Throws<TargetInvocationException>(
                () => read.Invoke(null, new object[] { stream, limits }));
            var error = Assert.IsType<U2R2ProtocolException>(
                invocation.InnerException);

            Assert.Equal("invalid_frame", error.ErrorCode);
            Assert.True(error.Terminal);
            Assert.Equal(fixedHeader.Length, stream.Position);
        }

        [Fact]
        public void PreparationAndPublishCarryCurrentSessionCorrelation()
        {
            var snapshot = Snapshot();
            var qos = FoxRunResolvedQos.Default;
            var preparation =
                Ros2BridgeV2SessionCodec.CreatePublisherPreparation(
                    snapshot,
                    requestId: 2,
                    "/phase186/v2/state",
                    "phase186_msgs/msg/State",
                    qos);
            var parsedPreparation = Parse(preparation.WireBytes);

            Assert.Equal(
                U2R2Operation.PreparePublisher,
                parsedPreparation.Operation);
            Assert.Equal(2UL, parsedPreparation.RequestId);
            Assert.Equal(snapshot.SessionId, parsedPreparation.SessionId);
            Assert.Equal(
                snapshot.ConnectionGeneration,
                parsedPreparation.ConnectionGeneration);
            Assert.Equal("/phase186/v2/state", parsedPreparation.Topic);
            Assert.Equal(
                "phase186_msgs/msg/State",
                parsedPreparation.SchemaName);
            Assert.Equal("cdr", parsedPreparation.Encoding);

            var frame = Ros2BridgeFrame.CreateOwned(
                "/phase186/v2/state",
                "phase186_msgs/msg/State",
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: 123,
                sequence: 4,
                payload: new byte[] { 0, 1, 2, 3 },
                qos);
            var measurement = Ros2BridgeV2SessionCodec.MeasurePublish(
                frame,
                snapshot,
                requestId: 3,
                messageId: 1);
            var publish = Ros2BridgeV2SessionCodec.EncodePublish(
                frame,
                snapshot,
                requestId: 3,
                messageId: 1,
                measurement);
            var parsedPublish = Parse(publish.WireBytes);

            Assert.Equal(U2R2Operation.Publish, parsedPublish.Operation);
            Assert.Equal(3UL, parsedPublish.RequestId);
            Assert.Equal(1UL, parsedPublish.MessageId);
            Assert.Equal(snapshot.SessionId, parsedPublish.SessionId);
            Assert.Equal(
                snapshot.ConnectionGeneration,
                parsedPublish.ConnectionGeneration);
            Assert.Equal(frame.Topic, parsedPublish.Topic);
            Assert.Equal(frame.SchemaName, parsedPublish.SchemaName);
            Assert.Equal(frame.PayloadMemory.ToArray(), publish.Payload);
            Assert.Equal(
                publish.WireBytes.Length,
                measurement.TotalWireBytes);
        }

        [Fact]
        public void WrongCorrelationGenerationAndBusyAreTerminal()
        {
            var limits = U2R2ProtocolLimits.Default;
            var hello = Ros2BridgeV2SessionCodec.CreateHello(
                requestId: 1,
                requiresSubscription: false,
                limits);
            var wrongRequest = U2R2ProtocolCodec.EncodeFrame(
                HelloAck(requestId: 2, generation: 7),
                Array.Empty<byte>(),
                limits);
            var wrongHello = Assert.Throws<U2R2ProtocolException>(
                () => Ros2BridgeV2SessionCodec.AcceptHello(
                    hello,
                    wrongRequest,
                    limits));
            Assert.Equal("response_mismatch", wrongHello.ErrorCode);
            Assert.True(wrongHello.Terminal);

            var busy = U2R2ProtocolCodec.EncodeFrame(
                new JObject
                {
                    ["op"] = "busy",
                    ["protocolVersion"] = 2,
                    ["requestId"] = 1,
                    ["status"] = "error",
                    ["errorCode"] = "busy",
                    ["message"] = "data session already leased",
                    ["terminal"] = true
                },
                Array.Empty<byte>(),
                limits);
            var busyError = Assert.Throws<U2R2ProtocolException>(
                () => Ros2BridgeV2SessionCodec.AcceptHello(
                    hello,
                    busy,
                    limits));
            Assert.Equal("busy", busyError.ErrorCode);
            Assert.True(busyError.Terminal);

            var snapshot = Snapshot();
            var request =
                Ros2BridgeV2SessionCodec.CreatePublisherPreparation(
                    snapshot,
                    requestId: 2,
                    "/phase186/v2/state",
                    "phase186_msgs/msg/State",
                    FoxRunResolvedQos.Default);
            var wrongGeneration = U2R2ProtocolCodec.EncodeFrame(
                new JObject
                {
                    ["op"] = "publisher_ready",
                    ["protocolVersion"] = 2,
                    ["requestId"] = 2,
                    ["status"] = "ok",
                    ["sessionId"] = snapshot.SessionId,
                    ["connectionGeneration"] = 8
                },
                Array.Empty<byte>(),
                limits);
            var generationError = Assert.Throws<U2R2ProtocolException>(
                () => Ros2BridgeV2SessionCodec.ValidateResponse(
                    request,
                    wrongGeneration,
                    snapshot));
            Assert.Equal(
                "response_mismatch",
                generationError.ErrorCode);
            Assert.True(generationError.Terminal);
        }

        [Fact]
        public void WriteLeaseOwnsBoundedJitWireReservation()
        {
            using var scheduler = new Ros2BridgeOutboundScheduler(
                U2R2ProtocolLimits.Default,
                sessionGeneration: 12);
            var frame = Ros2BridgeFrame.CreateOwned(
                "/phase186/v2/reservation",
                "phase186_msgs/msg/State",
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: 1,
                sequence: 1,
                payload: new byte[] { 1 });
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.Accepted,
                scheduler.Enqueue(
                    frame,
                    U2R2QueueOverflowPolicy.Reject));
            Assert.True(scheduler.TryBeginWrite(out var write));
            using (write)
            {
                Assert.True(
                    write.TryReserveTransient(
                        4096,
                        out var transient));
                using (transient)
                    Assert.Equal(4096UL, scheduler.TransientBytes);
                Assert.Equal(0UL, scheduler.TransientBytes);
                write.Complete();
            }
        }

        [Fact]
        public void PublishOnlyWorkerDoesNotOwnInboundQueueOrDispatcher()
        {
            var fields = typeof(Ros2BridgeWorkerLease)
                .GetFields(
                    BindingFlags.Instance
                    | BindingFlags.NonPublic
                    | BindingFlags.Public);
            Assert.DoesNotContain(
                fields,
                field =>
                    field.Name.Contains(
                        "inbound",
                        StringComparison.OrdinalIgnoreCase)
                    || field.FieldType.Name.Contains(
                        "Inbound",
                        StringComparison.OrdinalIgnoreCase)
                    || field.FieldType.Name.Contains(
                        "Dispatcher",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static Ros2BridgeV2SessionSnapshot Snapshot()
        {
            var limits = U2R2ProtocolLimits.Default;
            var hello = Ros2BridgeV2SessionCodec.CreateHello(
                requestId: 1,
                requiresSubscription: false,
                limits);
            return Ros2BridgeV2SessionCodec.AcceptHello(
                hello,
                U2R2ProtocolCodec.EncodeFrame(
                    HelloAck(requestId: 1, generation: 7),
                    Array.Empty<byte>(),
                    limits),
                limits);
        }

        private static JObject HelloAck(
            ulong requestId,
            ulong generation)
            => new JObject
            {
                ["op"] = "hello_ack",
                ["protocolVersion"] = 2,
                ["requestId"] = requestId,
                ["status"] = "ok",
                ["sessionId"] =
                    "5e7c4e90-b5b2-4db4-b27f-5a30e8086e1b",
                ["connectionGeneration"] = generation,
                ["capabilities"] = new JArray("publish")
            };

        private static U2R2Message Parse(byte[] wireBytes)
            => U2R2ProtocolCodec.ParseV2(
                U2R2ProtocolCodec.DecodeFrame(wireBytes));

        private static byte[] ReadWireFrame(Stream stream)
        {
            var fixedHeader = ReadExact(stream, 16);
            var headerLength = ReadUInt32LE(fixedHeader, 8);
            var payloadLength = ReadUInt32LE(fixedHeader, 12);
            var body = ReadExact(
                stream,
                checked((int)(headerLength + payloadLength)));
            var frame = new byte[checked(16 + body.Length)];
            Buffer.BlockCopy(fixedHeader, 0, frame, 0, fixedHeader.Length);
            Buffer.BlockCopy(body, 0, frame, 16, body.Length);
            return frame;
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var bytes = new byte[count];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }
            return bytes;
        }

        private static ulong ReadUInt32LE(byte[] buffer, int offset)
            => (ulong)buffer[offset]
               | ((ulong)buffer[offset + 1] << 8)
               | ((ulong)buffer[offset + 2] << 16)
               | ((ulong)buffer[offset + 3] << 24);
    }
}
