// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2Bridge.Tests/Protocol
// Purpose: Cross-language authority for the strict U2R2 v2 envelope and dialect model.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests.Unit.Protocol
{
    public sealed partial class U2R2ProtocolV2Tests
    {
        [Fact]
        public void StrictDecoderRejectsEnvelopeUtf8DuplicateAndTrailingRootFailures()
        {
            var hello = Vector("hello_request");
            var frame = HexToBytes(hello.Value<string>("frameHex"));

            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.DecodeFrame(
                    Mutate(frame, bytes => bytes[0] = (byte)'X')));
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.DecodeFrame(
                    Mutate(frame, bytes => bytes[4] = 2)));
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.DecodeFrame(
                    Mutate(frame, bytes => bytes[6] = 1)));
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.DecodeFrame(
                    BuildFrame(new byte[] { 0xff }, Array.Empty<byte>())));
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.DecodeFrame(
                    BuildFrame(
                        Encoding.UTF8.GetBytes(
                            "{\"op\":\"hello\",\"op\":\"hello\",\"protocolVersion\":2,\"requestId\":1}"),
                        Array.Empty<byte>())));
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.DecodeFrame(
                    BuildFrame(
                        Encoding.UTF8.GetBytes(
                            "{\"op\":\"hello\",/*comment*/\"protocolVersion\":2,\"requestId\":1}"),
                        Array.Empty<byte>())));
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.DecodeFrame(
                    BuildFrame(
                        Encoding.UTF8.GetBytes("{}{}"),
                        Array.Empty<byte>())));
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.DecodeFrame(
                    frame.Concat(new byte[] { 0 }).ToArray()));
        }

        [Fact]
        public void V2ModelRejectsZeroIdsUnsupportedVersionAndCounterWrap()
        {
            var hello = Assert.IsType<JObject>(Vector("hello_request")["header"])
                .DeepClone() as JObject;
            hello["requestId"] = 0;
            AssertProtocolError(
                "invalid_request_id",
                terminal: true,
                () => U2R2ProtocolCodec.ParseV2(
                    U2R2ProtocolCodec.DecodeFrame(
                        U2R2ProtocolCodec.EncodeFrame(
                            hello,
                            Array.Empty<byte>()))));

            var unsupported = Vector("hello_unsupported_version");
            AssertProtocolError(
                "unsupported_protocol",
                terminal: true,
                () => U2R2ProtocolCodec.ParseV2(
                    U2R2ProtocolCodec.DecodeFrame(
                        HexToBytes(unsupported.Value<string>("frameHex")))));

            var whitespaceOperation =
                (JObject)Assert.IsType<JObject>(Vector("hello_request")["header"]).DeepClone();
            whitespaceOperation["op"] = " \t ";
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.ParseV2(
                    new U2R2Frame(whitespaceOperation, Array.Empty<byte>())));

            var whitespaceSession =
                (JObject)Assert.IsType<JObject>(Vector("terminal_fault_request")["header"])
                    .DeepClone();
            whitespaceSession["sessionId"] = " \t ";
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.ParseV2(
                    new U2R2Frame(whitespaceSession, Array.Empty<byte>())));

            var counter = new U2R2MonotonicCounter(ulong.MaxValue - 1);
            Assert.Equal(ulong.MaxValue, counter.Next());
            AssertProtocolError("counter_exhausted", terminal: true, () => counter.Next());
            Assert.True(counter.IsFaulted);

            var allocator = new U2R2SidecarSessionIdentityAllocator();
            var first = allocator.Allocate();
            var second = allocator.Allocate();
            Assert.False(string.IsNullOrWhiteSpace(first.SessionId));
            Assert.False(string.IsNullOrWhiteSpace(second.SessionId));
            Assert.NotEqual(first.SessionId, second.SessionId);
            Assert.Equal(1UL, first.ConnectionGeneration);
            Assert.Equal(2UL, second.ConnectionGeneration);
        }

        [Fact]
        public void DialectStateIsSingleSocketAndSubscriptionsRequireCapability()
        {
            var state = new U2R2SessionStateMachine();
            state.AcceptV2(
                ParseVector("hello_request"),
                new[] { U2R2Capability.Publish, U2R2Capability.Subscribe });
            Assert.Equal(U2R2Dialect.V2, state.Dialect);
            Assert.Equal(U2R2ConnectionState.V2Active, state.State);
            Assert.True(state.AcquiresDataLease);

            var legacyPublish = U2R2LegacyV1Codec.ParseFirstFrame(
                HexToBytes(
                    Assert.IsType<JObject>(
                        LoadFixture()["publish"])["frame"]
                    .Value<string>("frameHex")));
            AssertProtocolError(
                "dialect_downgrade",
                terminal: true,
                () => state.AcceptLegacy(legacyPublish));
            Assert.Equal(U2R2ConnectionState.Terminal, state.State);

            var insufficient = new U2R2SessionStateMachine();
            AssertProtocolError(
                "missing_capability",
                terminal: true,
                () => insufficient.AcceptV2(
                    ParseVector("hello_missing_capability"),
                    new[] { U2R2Capability.Publish, U2R2Capability.Subscribe }));
            Assert.Equal(U2R2ConnectionState.Terminal, insufficient.State);
        }

        [Fact]
        public void ExplicitLegacyCodecKeepsV1StringIdsAndLeaseClassification()
        {
            var fixture = LoadFixture();
            var health = Assert.IsType<JObject>(fixture["health"]);
            var healthMessage = U2R2LegacyV1Codec.ParseFirstFrame(
                HexToBytes(
                    Assert.IsType<JObject>(health["request"])
                        .Value<string>("frameHex")));
            Assert.Equal("health_ping", healthMessage.Operation);
            Assert.Equal(health.Value<string>("requestId"), healthMessage.RequestId);
            Assert.False(healthMessage.AcquiresDataLease);

            var preparation = Assert.IsType<JObject>(fixture["preparePublisher"]);
            var preparationMessage = U2R2LegacyV1Codec.ParseFirstFrame(
                HexToBytes(
                    Assert.IsType<JObject>(preparation["request"])
                        .Value<string>("frameHex")));
            Assert.Equal("prepare_publisher", preparationMessage.Operation);
            Assert.Equal(
                preparation.Value<string>("requestId"),
                preparationMessage.RequestId);
            Assert.True(preparationMessage.AcquiresDataLease);

            var state = new U2R2SessionStateMachine();
            state.AcceptLegacy(healthMessage);
            Assert.Equal(U2R2Dialect.V1, state.Dialect);
            Assert.Equal(U2R2ConnectionState.V1Probe, state.State);
            Assert.False(state.AcquiresDataLease);
        }

        [Fact]
        public void SharedLedgersExecuteEveryErrorTransitionAndNegativeVector()
        {
            var authority = LoadV2Authority();
            var errorCodes = Assert.IsType<JArray>(authority["errorCodes"])
                .Values<JObject>()
                .ToArray();
            Assert.Equal(23, errorCodes.Length);
            Assert.Equal(
                errorCodes.Length,
                errorCodes.Select(item => item.Value<string>("code"))
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            foreach (var entry in errorCodes)
            {
                var code = entry.Value<string>("code");
                var wire = entry.Value<bool>("wire");
                var terminal = entry.Value<bool>("terminal");
                Assert.True(
                    U2R2ProtocolCodec.TryGetStableErrorTerminal(code, out var actualTerminal));
                Assert.Equal(terminal, actualTerminal);

                var allowed = Assert.IsType<JArray>(entry["responseOps"])
                    .Values<string>()
                    .Select(ParseOperation)
                    .ToArray();
                if (wire)
                    Assert.NotEmpty(allowed);
                else
                    Assert.Empty(allowed);
                foreach (var operation in allowed)
                {
                    Assert.True(
                        U2R2ProtocolCodec.IsStableErrorAllowedForResponse(
                            code,
                            operation));
                    var encoded = U2R2ProtocolCodec.EncodeFrame(
                        ErrorResponseHeader(
                            operation,
                            code,
                            terminal),
                        Array.Empty<byte>());
                    var parsed = U2R2ProtocolCodec.ParseV2(
                        U2R2ProtocolCodec.DecodeFrame(encoded));
                    Assert.Equal(operation, parsed.Operation);
                    Assert.Equal(code, parsed.ErrorCode);
                    Assert.Equal(terminal, parsed.Terminal);
                }
                foreach (var operation in ResponseOperations().Except(allowed))
                {
                    Assert.False(
                        U2R2ProtocolCodec.IsStableErrorAllowedForResponse(
                            code,
                            operation));
                }
                if (!wire)
                {
                    var forged = U2R2ProtocolCodec.EncodeFrame(
                        ErrorResponseHeader(
                            U2R2Operation.Fault,
                            code,
                            terminal),
                        Array.Empty<byte>());
                    var error = Assert.Throws<U2R2ProtocolException>(
                        () => U2R2ProtocolCodec.ParseV2(
                            U2R2ProtocolCodec.DecodeFrame(forged)));
                    Assert.Equal("invalid_frame", error.ErrorCode);
                }
            }
            Assert.False(
                U2R2ProtocolCodec.TryGetStableErrorTerminal(
                    "not_in_authority",
                    out _));

            var transitions = Assert.IsType<JArray>(authority["stateTransitions"])
                .Values<JObject>()
                .ToArray();
            Assert.Equal(6, transitions.Length);
            foreach (var transition in transitions)
                ExecuteStateTransition(transition);
            var mistypedTransition = (JObject)transitions[0].DeepClone();
            mistypedTransition["from"] = "v2_actvie";
            Assert.Throws<InvalidOperationException>(
                () => ExecuteStateTransition(mistypedTransition));

            var negatives = Assert.IsType<JArray>(authority["negativeVectors"])
                .Values<JObject>()
                .ToArray();
            Assert.Equal(51, negatives.Length);
            Assert.Equal(
                negatives.Length,
                negatives.Select(item => item.Value<string>("id"))
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            foreach (var negative in negatives)
            {
                AssertProtocolError(
                    negative.Value<string>("expectedErrorCode"),
                    negative.Value<bool>("terminal"),
                    BuildNegativeAction(negative));
            }
        }

    }
}
