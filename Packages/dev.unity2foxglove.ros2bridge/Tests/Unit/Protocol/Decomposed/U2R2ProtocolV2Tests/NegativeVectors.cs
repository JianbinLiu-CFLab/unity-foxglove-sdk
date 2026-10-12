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
        public void RecoverableReplayErrorsAllowHealthPongResponses()
        {
            var recoverableErrors = new[]
            {
                "request_in_flight",
                "stale_request",
                "capacity_exceeded",
            };
            foreach (var errorCode in recoverableErrors)
            {
                Assert.True(
                    U2R2ProtocolCodec.IsStableErrorAllowedForResponse(
                        errorCode,
                        U2R2Operation.HealthPong));
                var encoded = U2R2ProtocolCodec.EncodeFrame(
                    ErrorResponseHeader(
                        U2R2Operation.HealthPong,
                        errorCode,
                        terminal: false),
                    Array.Empty<byte>());
                var parsed = U2R2ProtocolCodec.ParseV2(
                    U2R2ProtocolCodec.DecodeFrame(encoded));
                Assert.Equal(U2R2Operation.HealthPong, parsed.Operation);
                Assert.Equal(errorCode, parsed.ErrorCode);
                Assert.False(parsed.Terminal);
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
                case "oversized_topic":
                    return () =>
                    {
                        var source = Vector(negative.Value<string>("baseVector"));
                        var header = (JObject)Assert.IsType<JObject>(source["header"]).DeepClone();
                        header["topic"] = "/" + new string(
                            'a',
                            negative.Value<int>("topicLength") - 1);
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

    }
}
