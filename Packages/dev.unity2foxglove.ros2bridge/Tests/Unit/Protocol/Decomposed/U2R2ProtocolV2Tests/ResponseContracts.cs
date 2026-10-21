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

    }
}
