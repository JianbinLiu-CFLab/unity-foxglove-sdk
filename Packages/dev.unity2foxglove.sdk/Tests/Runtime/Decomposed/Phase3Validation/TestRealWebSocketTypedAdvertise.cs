// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates core schema registration, typed channel advertising, SceneUpdate DTO serialization, and real WebSocket integration with schemas.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class Phase3Validation
    {
        /// <summary>
        /// Integration test: starts a server, registers a typed
        /// FrameTransform channel, connects a client, and verifies the
        /// advertise message carries <c>schemaName</c>,
        /// <c>schemaEncoding</c>, and inline schema content.
        /// </summary>
        private static void TestRealWebSocketTypedAdvertise()
        {
            using var runtime = new FoxgloveRuntime();
            var port = GetEphemeralTcpPort();
            runtime.Start("SchemaTest", "127.0.0.1", port);
            runtime.RegisterSchemaChannel(1, "/tf", "foxglove.FrameTransform");

            ClientWebSocket ws = null;
            try
            {
                ws = new ClientWebSocket();
                ws.Options.AddSubProtocol(Subprotocol.SdkV1);
                var cts = new CancellationTokenSource(5000);
                ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token).GetAwaiter().GetResult();
                Assert(ws.State == WebSocketState.Open, "Integration: connected");

                var buf = new byte[8192];
                var seg = new ArraySegment<byte>(buf);

                // serverInfo
                var r1 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                var info = JObject.Parse(Encoding.UTF8.GetString(buf, 0, r1.Count));
                Assert(info["op"]?.ToString() == "serverInfo", "Got serverInfo");

                // typed advertise
                var r2 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                var adv = JObject.Parse(Encoding.UTF8.GetString(buf, 0, r2.Count));
                Assert(adv["op"]?.ToString() == "advertise", "Got advertise");
                var ch = adv["channels"]?[0] as JObject;
                Assert(ch["schemaName"]?.ToString() == "foxglove.FrameTransform", "schemaName in advertise");
                Assert(ch["schemaEncoding"]?.ToString() == "jsonschema", "schemaEncoding in advertise");
                Assert(ch["schema"]?.ToString().Length > 0, "schema content non-empty");
            }
            finally
            {
                CloseClientWebSocketForCleanup(ws);
                runtime.Dispose();
            }
        }

        /// <summary>
        /// Integration test: registers a SceneUpdate channel, subscribes
        /// a client, publishes a scene entity with a cube via
        /// <c>PublishJson</c>, and verifies the binary frame contains
        /// the correct entity id and cube dimensions.
        /// </summary>
        private static void TestRealWebSocketPublishJsonSceneUpdate()
        {
            using var runtime = new FoxgloveRuntime();
            var port = GetEphemeralTcpPort();
            runtime.Start("SceneTest", "127.0.0.1", port);
            runtime.RegisterSchemaChannel(1, "/scene", "foxglove.SceneUpdate");

            ClientWebSocket ws = null;
            try
            {
                ws = new ClientWebSocket();
                ws.Options.AddSubProtocol(Subprotocol.SdkV1);
                var cts = new CancellationTokenSource(10000);
                ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token).GetAwaiter().GetResult();

                var buf = new byte[131072];
                var seg = new ArraySegment<byte>(buf);
                // Skip serverInfo
                ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                // Skip advertise (may be large due to embedded SceneUpdate schema)
                ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();

                // Subscribe
                var sub = "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}";
                ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(sub)), WebSocketMessageType.Text, true, cts.Token).GetAwaiter().GetResult();
                Assert(SpinUntil(() => runtime.Session.HasChannelDemand(1), 2000),
                    "Scene integration: subscribe processed before publish");

                // Publish SceneUpdate via PublishJson
                var msg = new SceneUpdateMessage
                {
                    Entities = new List<SceneEntity>
                    {
                        new SceneEntity
                        {
                            Id = "phase3_cube",
                            FrameId = "unity_world",
                            Timestamp = new FoxgloveTime { Sec = 1 },
                            Lifetime = new FoxgloveDuration(),
                            Cubes = new List<CubePrimitive>
                            {
                                new CubePrimitive
                                {
                                    Pose = new FoxglovePose
                                    {
                                        Position = new FoxgloveVector3(),
                                        Orientation = new FoxgloveQuaternion { W = 1 }
                                    },
                                    Size = new FoxgloveVector3 { X = 1, Y = 1, Z = 1 },
                                    Color = new FoxgloveColor { R = 0, G = 1, B = 0, A = 1 }
                                }
                            }
                        }
                    }
                };
                runtime.PublishJson(1, msg);

                // Receive binary frame
                var r3 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                Assert(r3.MessageType == WebSocketMessageType.Binary, "Received binary MessageData");
                var frame = new byte[r3.Count];
                Array.Copy(buf, 0, frame, 0, r3.Count);
                var subId = BitConverter.ToUInt32(frame, 1);
                AssertEqual(100u, subId, "subscriptionId=100");
                var payloadJson = Encoding.UTF8.GetString(frame, 13, frame.Length - 13);
                var payload = JObject.Parse(payloadJson);
                Assert(payload["entities"]?[0]?["id"]?.ToString() == "phase3_cube", "entity id in payload");
                Assert((double)payload["entities"][0]["cubes"][0]["size"]["x"] == 1.0, "cube size.x=1 in payload");
            }
            finally
            {
                CloseClientWebSocketForCleanup(ws);
                runtime.Dispose();
            }
        }

        private static void CloseClientWebSocketForCleanup(ClientWebSocket ws)
        {
            if (ws == null)
                return;

            try
            {
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (WebSocketException)
            {
                // Some runners observe the TCP close before the WebSocket
                // close handshake completes. Protocol assertions have already
                // run, so this is cleanup rather than test behavior.
            }
            catch (OperationCanceledException)
            {
                // Cancellation during cleanup should not fail completed assertions.
            }
            catch (ObjectDisposedException)
            {
                // Already disposed is cleanup.
            }
            catch (InvalidOperationException)
            {
                // Already closing/closed is also cleanup.
            }
            finally
            {
                ws.Dispose();
            }
        }

        private static int GetEphemeralTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static bool SpinUntil(Func<bool> condition, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }

            return condition();
        }
    }
}
