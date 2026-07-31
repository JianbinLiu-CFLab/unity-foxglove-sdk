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
    [Trait("Phase", "186-B")]
    [Trait("Domain", "U2R2Protocol")]
    public sealed class U2R2ProtocolV2Tests
    {
        [Fact]
        public void SharedV2AuthorityMatchesCanonicalCSharpFramesAndCorrelation()
        {
            var authority = LoadV2Authority();
            Assert.Equal(U2R2ProtocolCodec.ProtocolVersion, authority.Value<int>("protocolVersion"));
            Assert.Equal(U2R2ProtocolCodec.EnvelopeVersion, authority.Value<int>("envelopeVersion"));
            Assert.Equal("unsigned_64_nonzero", authority.Value<string>("requestId"));
            Assert.Equal("unsigned_64_nonzero", authority.Value<string>("messageId"));
            Assert.Equal(64, authority.Value<int>("jsonMaxDepth"));
            Assert.Equal(
                "utf8_byte_ordinal",
                authority.Value<string>("canonicalKeyOrder"));

            var vectors = Assert.IsType<JArray>(authority["operations"])
                .Values<JObject>()
                .ToDictionary(vector => vector.Value<string>("id"), StringComparer.Ordinal);
            Assert.Equal(21, vectors.Count);
            var helloHeaderJson = vectors["hello_request"].Value<string>("headerJson");
            Assert.True(
                helloHeaderJson.IndexOf("\\ue000\":1", StringComparison.Ordinal)
                < helloHeaderJson.IndexOf("\\ud83d\\ude00\":2", StringComparison.Ordinal));
            Assert.Contains(
                "\"unicodeSample\":\"\\u0085\\u2028\\u2029"
                + "\\ud83d\\ude00\\ue000\"",
                helloHeaderJson,
                StringComparison.Ordinal);

            foreach (var vector in vectors.Values)
            {
                var header = Assert.IsType<JObject>(vector["header"]);
                var payload = HexToBytes(vector.Value<string>("payloadHex"));
                var kind = vector.Value<string>("kind");
                var direction = vector.Value<string>("direction");
                Assert.Contains(kind, new[] { "request", "response", "event" });
                Assert.Equal(
                    string.Equals(kind, "request", StringComparison.Ordinal)
                        ? "client_to_sidecar"
                        : "sidecar_to_client",
                    direction);
                Assert.Equal(payload.Length, vector.Value<int>("payloadLength"));
                Assert.Equal(
                    string.Equals(kind, "response", StringComparison.Ordinal),
                    vector["correlatesTo"] != null);
                Assert.Equal(
                    string.Equals(kind, "response", StringComparison.Ordinal)
                    && string.Equals(
                        header.Value<string>("status"),
                        "error",
                        StringComparison.Ordinal)
                        ? header.Value<bool>("terminal")
                        : false,
                    vector.Value<bool>("terminal"));
                var encoded = U2R2ProtocolCodec.EncodeFrame(header, payload);
                Assert.Equal(vector.Value<string>("frameHex"), BytesToHex(encoded));
                Assert.Equal(
                    vector.Value<string>("headerJson"),
                    HeaderJson(encoded));

                var decoded = U2R2ProtocolCodec.DecodeFrame(encoded);
                Assert.True(JToken.DeepEquals(header, decoded.Header));
                Assert.Equal(payload, decoded.Payload.ToArray());

                if (!string.Equals(
                        vector.Value<string>("id"),
                        "hello_unsupported_version",
                        StringComparison.Ordinal))
                {
                    var message = U2R2ProtocolCodec.ParseV2(decoded);
                    Assert.Equal(
                        string.Equals(kind, "request", StringComparison.Ordinal),
                        message.IsRequest);
                    Assert.Equal(
                        string.Equals(kind, "response", StringComparison.Ordinal),
                        message.IsResponse);
                    Assert.Equal(vector.Value<bool>("terminal"), message.Terminal);
                    if (message.IsRequest)
                    {
                        Assert.NotEqual(0UL, message.RequestId);
                    }
                    if (header["sessionId"] != null)
                    {
                        Assert.Equal(
                            authority.Value<string>("sessionId"),
                            message.SessionId);
                        Assert.Equal(
                            authority.Value<ulong>("connectionGeneration"),
                            message.ConnectionGeneration);
                    }
                    if (message.IsResponse)
                    {
                        Assert.Equal(header.Value<string>("status"), message.Status);
                        Assert.Equal(
                            header.Value<string>("errorCode") ?? string.Empty,
                            message.ErrorCode);
                        Assert.Equal(
                            header.Value<string>("message") ?? string.Empty,
                            message.ErrorMessage);
                        if (string.Equals(message.Status, "error", StringComparison.Ordinal))
                        {
                            Assert.Equal(header.Value<bool>("terminal"), message.Terminal);
                        }
                    }
                    if (message.Operation == U2R2Operation.Publish)
                    {
                        Assert.Equal(header.Value<ulong>("logTimeNs"), message.LogTimeNs);
                        Assert.Equal(header.Value<ulong>("sequence"), message.Sequence);
                        Assert.Equal(0UL, message.ReceiveTimeNs);
                    }
                    if (message.Operation == U2R2Operation.Message)
                    {
                        Assert.Null(header["logTimeNs"]);
                        Assert.Equal(
                            header.Value<ulong>("receiveTimeNs"),
                            message.ReceiveTimeNs);
                        Assert.Equal(
                            header.Value<ulong>("sequence"),
                            message.Sequence);
                        Assert.Equal(0UL, message.LogTimeNs);
                        Assert.Equal("cdr", message.Encoding);
                        Assert.Equal(
                            "xcdr1-le",
                            message.Representation);
                    }
                }
            }

            foreach (var response in vectors.Values.Where(
                         vector => string.Equals(
                             vector.Value<string>("kind"),
                             "response",
                             StringComparison.Ordinal)))
            {
                var request = vectors[response.Value<string>("correlatesTo")];
                var parsedResponse = U2R2ProtocolCodec.ParseV2(
                    U2R2ProtocolCodec.DecodeFrame(
                        HexToBytes(response.Value<string>("frameHex"))));
                U2R2ProtocolCodec.ValidateResponseCorrelation(
                    ResponseExpectation(request),
                    parsedResponse);
            }

            var unsignedVectors = Assert.IsType<JArray>(authority["unsigned64Vectors"]);
            Assert.Equal(2, unsignedVectors.Count);
            foreach (var vector in unsignedVectors.Values<JObject>())
            {
                var encoded = U2R2ProtocolCodec.EncodeFrame(
                    Assert.IsType<JObject>(vector["header"]),
                    Array.Empty<byte>());
                Assert.Equal(vector.Value<string>("frameHex"), BytesToHex(encoded));
                Assert.Equal(vector.Value<string>("headerJson"), HeaderJson(encoded));
                var message = U2R2ProtocolCodec.ParseV2(
                    U2R2ProtocolCodec.DecodeFrame(encoded));
                Assert.Equal(
                    ulong.Parse(
                        vector.Value<string>("valueDecimal"),
                        CultureInfo.InvariantCulture),
                    message.RequestId);
            }

            var strictJsonVectors =
                Assert.IsType<JArray>(authority["strictJsonVectors"])
                    .Values<JObject>()
                    .ToArray();
            Assert.Equal(3, strictJsonVectors.Length);
            foreach (var vector in strictJsonVectors)
            {
                var action = BuildStrictJsonAction(vector);
                if (vector.Value<bool>("valid"))
                {
                    var message = action();
                    Assert.Equal(U2R2Operation.Hello, message.Operation);
                }
                else
                {
                    AssertProtocolError(
                        vector.Value<string>("expectedErrorCode"),
                        vector.Value<bool>("terminal"),
                        () => action());
                }
            }

            var encodeNegatives =
                Assert.IsType<JArray>(authority["encodeNegativeVectors"])
                    .Values<JObject>()
                    .ToArray();
            Assert.Equal(5, encodeNegatives.Length);
            foreach (var vector in encodeNegatives)
            {
                AssertProtocolError(
                    vector.Value<string>("expectedErrorCode"),
                    vector.Value<bool>("terminal"),
                    BuildEncodeNegativeAction(vector));
            }
        }

        [Fact]
        public void DataOperationsRequireNonzeroSequence()
        {
            var vector = Vector("message");
            var payload = HexToBytes(vector.Value<string>("payloadHex"));
            var header = (JObject)vector["header"].DeepClone();

            header.Remove("sequence");
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.ParseV2(
                    new U2R2Frame(header, payload)));

            header["sequence"] = 0UL;
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.ParseV2(
                    new U2R2Frame(header, payload)));

            var publish = Vector("publish");
            var publishHeader = (JObject)publish["header"].DeepClone();
            var publishPayload = HexToBytes(publish.Value<string>("payloadHex"));
            publishHeader.Remove("sequence");
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.ParseV2(
                    new U2R2Frame(publishHeader, publishPayload)));

            publishHeader["sequence"] = 0UL;
            AssertProtocolError(
                "invalid_frame",
                terminal: true,
                () => U2R2ProtocolCodec.ParseV2(
                    new U2R2Frame(publishHeader, publishPayload)));
        }

        [Fact]
        public void ResponseCorrelationIsDerivedOnlyFromRequestAndChecksEveryContextDimension()
        {
            var authority = LoadV2Authority();
            var helloExpectation =
                U2R2ResponseExpectation.FromRequest(ParseVector("hello_request"));
            Assert.Equal(
                new[]
                {
                    U2R2Operation.HelloAck,
                    U2R2Operation.Busy,
                    U2R2Operation.Fault,
                },
                helloExpectation.AllowedResponseOperations);
            U2R2ProtocolCodec.ValidateResponseCorrelation(
                helloExpectation,
                ParseVector("hello_ack"));
            U2R2ProtocolCodec.ValidateResponseCorrelation(
                helloExpectation,
                ParseVector("busy"));

            var unsupportedHello = U2R2ResponseExpectation.FromHelloRequest(
                Vector("hello_unsupported_version")["header"].Value<ulong>("requestId"));
            U2R2ProtocolCodec.ValidateResponseCorrelation(
                unsupportedHello,
                ParseVector("protocol_rejected"));

            var healthExpectation =
                U2R2ResponseExpectation.FromRequest(ParseVector("health_ping"));
            Assert.Equal(
                new[]
                {
                    U2R2Operation.HealthPong,
                    U2R2Operation.Busy,
                    U2R2Operation.Fault,
                },
                healthExpectation.AllowedResponseOperations);
            var healthResponse = ParseVector("health_pong");
            U2R2ProtocolCodec.ValidateResponseCorrelation(
                healthExpectation,
                healthResponse);

            Assert.Throws<ArgumentException>(
                () => U2R2ResponseExpectation.FromRequest(
                    ParseVector("health_pong")));

            var mismatchedRequestId = RequestWithMutation(
                "health_ping",
                header => header["requestId"] = 3);
            AssertResponseMismatch(
                U2R2ResponseExpectation.FromRequest(mismatchedRequestId),
                healthResponse);

            var mismatchedOperation = RequestWithMutation(
                "prepare_publisher",
                header => header["requestId"] = 2);
            AssertResponseMismatch(
                U2R2ResponseExpectation.FromRequest(mismatchedOperation),
                healthResponse);

            var mismatchedSession = RequestWithMutation(
                "health_ping",
                header => header["sessionId"] = "different-session");
            AssertResponseMismatch(
                U2R2ResponseExpectation.FromRequest(mismatchedSession),
                healthResponse);

            var mismatchedGeneration = RequestWithMutation(
                "health_ping",
                header => header["connectionGeneration"] =
                    authority.Value<ulong>("connectionGeneration") + 1);
            AssertResponseMismatch(
                U2R2ResponseExpectation.FromRequest(mismatchedGeneration),
                healthResponse);

            var subscriptionResponse = ParseVector("subscription_ready");
            var mismatchedContract = RequestWithMutation(
                "register_subscription",
                header => header["contractId"] = 42);
            AssertResponseMismatch(
                U2R2ResponseExpectation.FromRequest(mismatchedContract),
                subscriptionResponse);

            var publishResponse = ParseVector("publish_result");
            var mismatchedMessage = RequestWithMutation(
                "publish",
                header => header["messageId"] = 2);
            AssertResponseMismatch(
                U2R2ResponseExpectation.FromRequest(mismatchedMessage),
                publishResponse);

            var forgedBusyHeader =
                (JObject)Vector("busy")["header"].DeepClone();
            forgedBusyHeader["sessionId"] = authority.Value<string>("sessionId");
            forgedBusyHeader["connectionGeneration"] =
                authority.Value<ulong>("connectionGeneration");
            AssertResponseMismatch(
                helloExpectation,
                ParseHeader(forgedBusyHeader, Array.Empty<byte>()));

            var terminalExpectation =
                U2R2ResponseExpectation.FromRequest(
                    ParseVector("terminal_fault_request"));
            var unboundFaultHeader =
                (JObject)Vector("terminal_fault")["header"].DeepClone();
            unboundFaultHeader.Remove("sessionId");
            unboundFaultHeader.Remove("connectionGeneration");
            AssertResponseMismatch(
                terminalExpectation,
                ParseHeader(unboundFaultHeader, Array.Empty<byte>()));
        }

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
            Assert.Equal(49, negatives.Length);
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

        [Fact]
        public void PublicProtocolModelsDoNotExposeMutableHeaderOrCapabilities()
        {
            var encoded = HexToBytes(Vector("hello_request").Value<string>("frameHex"));
            var frame = U2R2ProtocolCodec.DecodeFrame(encoded);
            var leakedHeader = frame.Header;
            leakedHeader["op"] = "fault";
            Assert.Equal("hello", frame.Header.Value<string>("op"));

            var message = U2R2ProtocolCodec.ParseV2(frame);
            var capabilities = Assert.IsAssignableFrom<IList<U2R2Capability>>(
                message.Capabilities);
            Assert.True(capabilities.IsReadOnly);
            Assert.Throws<NotSupportedException>(
                () => capabilities[0] = U2R2Capability.Subscribe);
            Assert.Equal(
                new[] { U2R2Capability.Publish, U2R2Capability.Subscribe },
                message.Capabilities);
        }

        private static Action BuildNegativeAction(JObject negative)
        {
            switch (negative.Value<string>("action"))
            {
                case "mutate_header":
                    return () =>
                    {
                        var source = Vector(negative.Value<string>("baseVector"));
                        var header = (JObject)Assert.IsType<JObject>(source["header"]).DeepClone();
                        header[negative.Value<string>("field")] =
                            negative["value"]?.DeepClone();
                        ParseHeader(header, HexToBytes(source.Value<string>("payloadHex")));
                    };
                case "remove_header":
                    return () =>
                    {
                        var source = Vector(negative.Value<string>("baseVector"));
                        var header = (JObject)Assert.IsType<JObject>(source["header"]).DeepClone();
                        header.Remove(negative.Value<string>("field"));
                        ParseHeader(header, HexToBytes(source.Value<string>("payloadHex")));
                    };
                case "raw_header_json":
                    return () =>
                    {
                        var frame = U2R2ProtocolCodec.DecodeFrame(
                            BuildFrame(
                                Encoding.UTF8.GetBytes(
                                    negative.Value<string>("rawHeaderJson")),
                                Array.Empty<byte>()));
                        U2R2ProtocolCodec.ParseV2(frame);
                    };
                case "raw_header_hex":
                    return () => U2R2ProtocolCodec.DecodeFrame(
                        BuildFrame(
                            HexToBytes(negative.Value<string>("headerHex")),
                            Array.Empty<byte>()));
                case "counter_wrap":
                    return () => new U2R2MonotonicCounter(ulong.MaxValue).Next();
                case "state_downgrade":
                    return () =>
                    {
                        var state = ActiveV2State();
                        state.AcceptLegacy(LegacyFrame("publish"));
                    };
                case "missing_capability":
                    return () =>
                    {
                        var state = new U2R2SessionStateMachine();
                        state.AcceptV2(
                            ParseVector("hello_missing_capability"),
                            new[]
                            {
                                U2R2Capability.Publish,
                                U2R2Capability.Subscribe,
                            });
                    };
                case "forge_hello_identity":
                    return () =>
                    {
                        var header =
                            (JObject)Assert.IsType<JObject>(
                                Vector("hello_request")["header"]).DeepClone();
                        header["sessionId"] = LoadV2Authority().Value<string>("sessionId");
                        header["connectionGeneration"] =
                            LoadV2Authority().Value<ulong>("connectionGeneration");
                        ParseHeader(header, Array.Empty<byte>());
                    };
                case "replace_payload":
                    return () =>
                    {
                        var source = Vector(negative.Value<string>("baseVector"));
                        ParseHeader(
                            (JObject)Assert.IsType<JObject>(source["header"]).DeepClone(),
                            HexToBytes(negative.Value<string>("payloadHex")));
                    };
                case "response_status_ok":
                    return () =>
                    {
                        var source = Vector(negative.Value<string>("baseVector"));
                        var header =
                            (JObject)Assert.IsType<JObject>(source["header"]).DeepClone();
                        header["status"] = "ok";
                        header.Remove("errorCode");
                        header.Remove("message");
                        header.Remove("terminal");
                        ParseHeader(
                            header,
                            HexToBytes(source.Value<string>("payloadHex")));
                    };
                default:
                    throw new InvalidOperationException(
                        "Unhandled negative-vector action: "
                        + negative.Value<string>("action"));
            }
        }

        private static Action BuildEncodeNegativeAction(JObject vector)
            => () =>
            {
                var header =
                    (JObject)Assert.IsType<JObject>(
                        Vector("hello_request")["header"]).DeepClone();
                switch (vector.Value<string>("action"))
                {
                    case "undefined_value":
                        header["padding"] = JValue.CreateUndefined();
                        break;
                    case "nonfinite_value":
                        header["padding"] = double.NaN;
                        break;
                    case "nested_padding":
                        JToken nested = new JValue(0UL);
                        for (var depth = 0;
                             depth < vector.Value<int>("arrayNesting");
                             depth++)
                        {
                            nested = new JArray(nested);
                        }
                        header["padding"] = nested;
                        break;
                    case "invalid_utf8_key":
                        header["\uD800"] = 0UL;
                        break;
                    case "invalid_utf8_value":
                        header["padding"] = "\uD800";
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unhandled encode-negative action: "
                            + vector.Value<string>("action"));
                }
                U2R2ProtocolCodec.EncodeFrame(header, Array.Empty<byte>());
            };

        private static Func<U2R2Message> BuildStrictJsonAction(JObject vector)
        {
            switch (vector.Value<string>("action"))
            {
                case "raw_header_json":
                    return () => U2R2ProtocolCodec.ParseV2(
                        U2R2ProtocolCodec.DecodeFrame(
                            BuildFrame(
                                Encoding.UTF8.GetBytes(
                                    vector.Value<string>("rawHeaderJson")),
                                Array.Empty<byte>())));
                case "nested_padding":
                    return () =>
                    {
                        var nesting = vector.Value<int>("arrayNesting");
                        var rawJson =
                            "{\"capabilities\":[\"publish\",\"subscribe\"],"
                            + "\"clientName\":\"unity2foxglove\",\"op\":\"hello\","
                            + "\"padding\":"
                            + new string('[', nesting)
                            + "0"
                            + new string(']', nesting)
                            + ",\"protocolVersion\":2,\"requestId\":11}";
                        return U2R2ProtocolCodec.ParseV2(
                            U2R2ProtocolCodec.DecodeFrame(
                                BuildFrame(
                                    Encoding.UTF8.GetBytes(rawJson),
                                    Array.Empty<byte>())));
                    };
                default:
                    throw new InvalidOperationException(
                        "Unhandled strict-JSON vector action: "
                        + vector.Value<string>("action"));
            }
        }

        private static void ExecuteStateTransition(JObject transition)
        {
            var state = StateFromFixture(transition.Value<string>("from"));
            Action action;
            var operation = transition.Value<string>("operation");
            var protocolVersion = transition.Value<int>("protocolVersion");
            if (protocolVersion == 2 && operation == "hello")
            {
                action = () => state.AcceptV2(
                    ParseVector("hello_request"),
                    new[] { U2R2Capability.Publish, U2R2Capability.Subscribe });
            }
            else if (protocolVersion == 2 && operation == "fault")
            {
                action = () => state.Fault(
                    transition.Value<string>("errorCode"),
                    "fixture-driven terminal fault");
            }
            else if (protocolVersion == 1)
            {
                action = () => state.AcceptLegacy(LegacyFrame(operation));
            }
            else
            {
                throw new InvalidOperationException(
                    "Unhandled fixture state transition.");
            }

            var expectedError = transition.Value<string>("errorCode");
            if (!string.IsNullOrEmpty(expectedError))
            {
                AssertProtocolError(expectedError, terminal: true, action);
            }
            else
            {
                action();
            }

            Assert.Equal(
                ParseConnectionState(transition.Value<string>("to")),
                state.State);
            if (transition["dialect"] != null)
            {
                Assert.Equal(
                    ParseDialect(transition.Value<string>("dialect")),
                    state.Dialect);
            }
            if (transition["acquiresDataLease"] != null)
            {
                Assert.Equal(
                    transition.Value<bool>("acquiresDataLease"),
                    state.AcquiresDataLease);
            }
        }

        private static U2R2SessionStateMachine StateFromFixture(string value)
        {
            switch (value)
            {
                case "awaiting_first_frame":
                    return new U2R2SessionStateMachine();
                case "v2_active":
                    return ActiveV2State();
                default:
                    throw new InvalidOperationException(
                        "Unknown or nonconstructible fixture source state: " + value);
            }
        }

        private static U2R2SessionStateMachine ActiveV2State()
        {
            var state = new U2R2SessionStateMachine();
            state.AcceptV2(
                ParseVector("hello_request"),
                new[] { U2R2Capability.Publish, U2R2Capability.Subscribe });
            return state;
        }

        private static U2R2LegacyV1Message LegacyFrame(string operation)
        {
            var fixture = LoadFixture();
            string frameHex;
            switch (operation)
            {
                case "health_ping":
                    frameHex = fixture["health"]["request"].Value<string>("frameHex");
                    break;
                case "prepare_publisher":
                    frameHex = fixture["preparePublisher"]["request"]
                        .Value<string>("frameHex");
                    break;
                case "publish":
                    frameHex = fixture["publish"]["frame"].Value<string>("frameHex");
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unhandled legacy transition operation: " + operation);
            }
            return U2R2LegacyV1Codec.ParseFirstFrame(HexToBytes(frameHex));
        }

        private static U2R2Message ParseHeader(JObject header, byte[] payload)
            => U2R2ProtocolCodec.ParseV2(new U2R2Frame(header, payload));

        private static IEnumerable<U2R2Operation> ResponseOperations()
        {
            yield return U2R2Operation.HelloAck;
            yield return U2R2Operation.HealthPong;
            yield return U2R2Operation.PublisherReady;
            yield return U2R2Operation.PublishResult;
            yield return U2R2Operation.SubscriptionReady;
            yield return U2R2Operation.SubscriptionRemoved;
            yield return U2R2Operation.Busy;
            yield return U2R2Operation.Fault;
        }

        private static JObject ErrorResponseHeader(
            U2R2Operation operation,
            string code,
            bool terminal)
        {
            var header = new JObject
            {
                ["op"] = OperationName(operation),
                ["protocolVersion"] = 2,
                ["requestId"] = 1,
                ["status"] = "error",
                ["errorCode"] = code,
                ["message"] = "fixture error",
                ["terminal"] = terminal,
            };
            if (operation != U2R2Operation.Busy
                && operation != U2R2Operation.Fault)
            {
                header["sessionId"] =
                    "5e7c4e90-b5b2-4db4-b27f-5a30e8086e1b";
                header["connectionGeneration"] = 7;
            }
            return header;
        }

        private static string OperationName(U2R2Operation operation)
        {
            switch (operation)
            {
                case U2R2Operation.HelloAck:
                    return "hello_ack";
                case U2R2Operation.HealthPong:
                    return "health_pong";
                case U2R2Operation.PublisherReady:
                    return "publisher_ready";
                case U2R2Operation.PublishResult:
                    return "publish_result";
                case U2R2Operation.SubscriptionReady:
                    return "subscription_ready";
                case U2R2Operation.SubscriptionRemoved:
                    return "subscription_removed";
                case U2R2Operation.Busy:
                    return "busy";
                case U2R2Operation.Fault:
                    return "fault";
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        private static U2R2Operation ParseOperation(string value)
        {
            switch (value)
            {
                case "hello":
                    return U2R2Operation.Hello;
                case "hello_ack":
                    return U2R2Operation.HelloAck;
                case "health_ping":
                    return U2R2Operation.HealthPing;
                case "health_pong":
                    return U2R2Operation.HealthPong;
                case "prepare_publisher":
                    return U2R2Operation.PreparePublisher;
                case "publisher_ready":
                    return U2R2Operation.PublisherReady;
                case "publish":
                    return U2R2Operation.Publish;
                case "publish_result":
                    return U2R2Operation.PublishResult;
                case "register_subscription":
                    return U2R2Operation.RegisterSubscription;
                case "subscription_ready":
                    return U2R2Operation.SubscriptionReady;
                case "message":
                    return U2R2Operation.Message;
                case "unregister_subscription":
                    return U2R2Operation.UnregisterSubscription;
                case "subscription_removed":
                    return U2R2Operation.SubscriptionRemoved;
                case "busy":
                    return U2R2Operation.Busy;
                case "fault":
                    return U2R2Operation.Fault;
                default:
                    throw new InvalidOperationException(
                        "Unknown fixture operation: " + value);
            }
        }

        private static U2R2ResponseExpectation ResponseExpectation(
            JObject request)
        {
            var requestHeader = Assert.IsType<JObject>(request["header"]);
            if (requestHeader.Value<ulong>("protocolVersion")
                != U2R2ProtocolCodec.ProtocolVersion)
            {
                return U2R2ResponseExpectation.FromHelloRequest(
                    requestHeader.Value<ulong>("requestId"));
            }
            return U2R2ResponseExpectation.FromRequest(
                ParseHeader(
                    (JObject)requestHeader.DeepClone(),
                    HexToBytes(request.Value<string>("payloadHex"))));
        }

        private static U2R2Message RequestWithMutation(
            string vectorId,
            Action<JObject> mutation)
        {
            var vector = Vector(vectorId);
            var header =
                (JObject)Assert.IsType<JObject>(vector["header"]).DeepClone();
            mutation(header);
            return ParseHeader(
                header,
                HexToBytes(vector.Value<string>("payloadHex")));
        }

        private static void AssertResponseMismatch(
            U2R2ResponseExpectation expectation,
            U2R2Message response)
            => AssertProtocolError(
                "response_mismatch",
                terminal: true,
                () => U2R2ProtocolCodec.ValidateResponseCorrelation(
                    expectation,
                    response));

        private static U2R2ConnectionState ParseConnectionState(string value)
        {
            switch (value)
            {
                case "v1_probe":
                    return U2R2ConnectionState.V1Probe;
                case "v1_data":
                    return U2R2ConnectionState.V1Data;
                case "v2_active":
                    return U2R2ConnectionState.V2Active;
                case "terminal":
                    return U2R2ConnectionState.Terminal;
                default:
                    throw new InvalidOperationException(
                        "Unknown fixture connection state: " + value);
            }
        }

        private static U2R2Dialect ParseDialect(string value)
        {
            switch (value)
            {
                case "v1":
                    return U2R2Dialect.V1;
                case "v2":
                    return U2R2Dialect.V2;
                default:
                    throw new InvalidOperationException(
                        "Unknown fixture dialect: " + value);
            }
        }

        private static U2R2Message ParseVector(string id)
        {
            var vector = Vector(id);
            return U2R2ProtocolCodec.ParseV2(
                U2R2ProtocolCodec.DecodeFrame(
                    HexToBytes(vector.Value<string>("frameHex"))));
        }

        private static JObject Vector(string id)
            => Assert.IsType<JArray>(LoadV2Authority()["operations"])
                .Values<JObject>()
                .Single(vector => string.Equals(
                    vector.Value<string>("id"),
                    id,
                    StringComparison.Ordinal));

        private static JObject LoadV2Authority()
            => Assert.IsType<JObject>(LoadFixture()["v2"]);

        private static JObject LoadFixture()
            => JObject.Parse(File.ReadAllText(FindFixture()));

        private static string FindFixture()
        {
            const string relative =
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/" +
                "u2r2_protocol_vectors.json";
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var current = new DirectoryInfo(start);
                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, "Packages"))
                        && Directory.Exists(Path.Combine(current.FullName, "Tools")))
                    {
                        return Path.Combine(
                            current.FullName,
                            relative.Replace('/', Path.DirectorySeparatorChar));
                    }
                    current = current.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not locate the U2R2 authority fixture.");
        }

        private static void AssertProtocolError(
            string code,
            bool terminal,
            Action action)
        {
            var exception = Assert.Throws<U2R2ProtocolException>(action);
            Assert.Equal(code, exception.ErrorCode);
            Assert.Equal(terminal, exception.Terminal);
        }

        private static byte[] Mutate(byte[] source, Action<byte[]> mutation)
        {
            var clone = (byte[])source.Clone();
            mutation(clone);
            return clone;
        }

        private static byte[] BuildFrame(byte[] header, byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            var result = new byte[16 + header.Length + payload.Length];
            result[0] = (byte)'U';
            result[1] = (byte)'2';
            result[2] = (byte)'R';
            result[3] = (byte)'2';
            result[4] = 1;
            WriteUInt32(result, 8, checked((uint)header.Length));
            WriteUInt32(result, 12, checked((uint)payload.Length));
            Buffer.BlockCopy(header, 0, result, 16, header.Length);
            Buffer.BlockCopy(payload, 0, result, 16 + header.Length, payload.Length);
            return result;
        }

        private static string HeaderJson(byte[] frame)
        {
            var length = checked((int)ReadUInt32(frame, 8));
            return Encoding.UTF8.GetString(frame, 16, length);
        }

        private static byte[] HexToBytes(string hex)
        {
            var result = new byte[hex.Length / 2];
            for (var index = 0; index < result.Length; index++)
                result[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            return result;
        }

        private static string BytesToHex(IEnumerable<byte> bytes)
            => string.Concat(bytes.Select(value => value.ToString("x2")));

        private static uint ReadUInt32(byte[] data, int offset)
            => (uint)(data[offset]
                      | data[offset + 1] << 8
                      | data[offset + 2] << 16
                      | data[offset + 3] << 24);

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }
    }
}
