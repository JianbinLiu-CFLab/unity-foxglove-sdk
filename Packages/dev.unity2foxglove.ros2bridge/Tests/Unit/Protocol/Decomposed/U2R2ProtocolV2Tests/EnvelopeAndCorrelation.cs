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

    }
}
