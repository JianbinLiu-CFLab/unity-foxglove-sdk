// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 50 regression coverage for critical stability/security fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates Phase 50 P0/P1 stability and security fixes.
    /// </summary>
    public static class Phase50Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 50: Critical Stability and Security Fixes ===");
            _passed = 0;

            VerifyPlaybackClockNowNsIsPureRead();
            VerifyPlaybackClockExplicitTickAdvancesAndClamps();
            VerifyPlaybackClockToStateIsPure();
            VerifyRuntimeTickAdvancesPlaybackClockBeforeReplay();
            VerifyManagedWebSocketOrderedShutdownSource();
            VerifyManagedWebSocketHandshakeBounds();
            VerifyFoxRunPhysicalGeneratedFileFreshness();
            VerifySessionUsesVolatileRuntimeAndRecorderAccess();
            VerifyClearSessionRemovesClientChannelsAndGraph();
            VerifyDisposeDetachesClientMessageSubscribers();
            VerifySourceEmitterEscapesGeneratedStringLiterals();
            VerifyFoxgloveLogHubDomainReloadResetSource();
            VerifyRecordingControllerDoubleAttachGuard();
            VerifyMcapReplayRejectsOversizedInnerRecordLength();
            VerifySceneCubeColorSetterSource();

            Console.WriteLine($"Phase 50: {_passed} checks passed.");
        }

        private static void VerifyPlaybackClockNowNsIsPureRead()
        {
            var clock = new PlaybackClock();
            clock.EnableRange(0, 10_000_000_000UL);
            clock.Play();
            SetPrivateField(clock, "_lastTickWallTime", DateTime.UtcNow - TimeSpan.FromSeconds(1));

            var before = GetPrivateField<ulong>(clock, "_currentTimeNs");
            var read = clock.NowNs;
            var after = GetPrivateField<ulong>(clock, "_currentTimeNs");

            Check(read == before && after == before, "50A-1: NowNs is a pure read and does not advance playback");
        }

        private static void VerifyPlaybackClockExplicitTickAdvancesAndClamps()
        {
            var tick = typeof(PlaybackClock).GetMethod(
                "Tick",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(DateTime) },
                null);
            Check(tick != null, "50A-2: PlaybackClock exposes deterministic Tick(DateTime) for tests");

            var clock = new PlaybackClock();
            clock.EnableRange(1_000UL, 1_000_001_000UL);
            var t0 = new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);
            tick.Invoke(clock, new object[] { t0 });
            clock.Apply(0, 2f, false, 0);
            tick.Invoke(clock, new object[] { t0.AddMilliseconds(1) });
            Check(clock.NowNs == 2_001_000UL, "50A-3: Tick(DateTime) advances by elapsed wall time and speed");

            tick.Invoke(clock, new object[] { t0.AddSeconds(5) });
            var state = clock.ToState(false, "end");
            Check(clock.NowNs == 1_000_001_000UL && state.Status == 3,
                "50A-4: Tick(DateTime) clamps to end time and marks playback ended");
        }

        private static void VerifyPlaybackClockToStateIsPure()
        {
            var clock = new PlaybackClock();
            clock.EnableRange(0, 10_000_000_000UL);
            clock.Play();
            SetPrivateField(clock, "_lastTickWallTime", DateTime.UtcNow - TimeSpan.FromSeconds(1));
            var before = GetPrivateField<ulong>(clock, "_currentTimeNs");
            var state = clock.ToState(false, "pure");
            var after = GetPrivateField<ulong>(clock, "_currentTimeNs");

            Check(state.CurrentTimeNs == before && after == before, "50A-5: ToState() does not mutate playback time");
        }

        private static void VerifyRuntimeTickAdvancesPlaybackClockBeforeReplay()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs");
            var tickIndex = source.IndexOf("_playbackClock.Tick()", StringComparison.Ordinal);
            var replayIndex = source.IndexOf("_replay.Tick(_session", StringComparison.Ordinal);
            Check(tickIndex >= 0 && replayIndex > tickIndex,
                "50A-6: FoxgloveRuntime.Tick() advances PlaybackClock before replay dispatch");
        }

        private static void VerifyManagedWebSocketOrderedShutdownSource()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/ManagedWsBackend.cs");
            Check(!source.Contains("using (tcpClient)"),
                "50B-1: HandleClient does not dispose TcpClient before the send loop owns shutdown");
            Check(source.Contains("WaitForSendLoop") && source.Contains(".Wait(timeout)"),
                "50B-2: WsConnection waits for send loop completion before closing stream/socket");
            Check(source.Contains("stream.WriteTimeout = Timeout.Infinite"),
                "50B-3: handshake write timeout is reset after upgrade");
        }

        private static void VerifyManagedWebSocketHandshakeBounds()
        {
            Check(HandshakeRejected(BuildHandshake(requestTargetLength: 9_000), GetFreeTcpPort()),
                "50F-1: overlong request line is rejected");
            Check(HandshakeRejected(BuildHandshake(extraHeader: "X-Long: " + new string('a', 9_000)), GetFreeTcpPort()),
                "50F-2: overlong header line is rejected");
            Check(HandshakeRejected(BuildHandshake(extraHeaderCount: 101), GetFreeTcpPort()),
                "50F-3: more than 100 headers is rejected");
            Check(HandshakeAccepted(BuildHandshake(), GetFreeTcpPort()),
                "50F-4: normal Foxglove handshake still succeeds");
        }

        private static void VerifyFoxRunPhysicalGeneratedFileFreshness()
        {
            var source = ReadRepoText("Unity2Foxglove/Assets/Scripts/TestLog.cs");
            var generated = ReadRepoText("Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs");

            var declaredTopics = Regex.Matches(source, @"\[FoxRun\(""([^""]+)""")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();
            var topicCountMatch = Regex.Match(generated, @"TopicCount\s*=>\s*(\d+)");
            var generatedCount = topicCountMatch.Success ? int.Parse(topicCountMatch.Groups[1].Value) : -1;

            Check(generatedCount == declaredTopics.Count,
                "50C-1: generated TopicCount matches source FoxRun declarations");
            foreach (var topic in declaredTopics)
                Check(generated.Contains(topic), $"50C-2: generated file contains declared topic {topic}");
            Check(generated.Contains("IFoxgloveLogPolicySource")
                  && generated.Contains("FoxRunPublishMode.OnChangeOrInterval")
                  && generated.Contains("0.01f")
                  && generated.Contains("1f"),
                "50C-3: generated file contains policy metadata for /debug/position2");
        }

        private static void VerifySessionUsesVolatileRuntimeAndRecorderAccess()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveSession.cs")
                + ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveSession.Connection.cs");

            Check(source.Contains("Volatile.Write(ref _runtime") && source.Contains("Volatile.Read(ref _runtime"),
                "50D-1: runtime reference uses volatile publication and reads");
            Check(source.Contains("Volatile.Write(ref _recorder") && source.Contains("Volatile.Read(ref _recorder"),
                "50D-2: recorder reference uses volatile publication and reads");
        }

        private static void VerifyClearSessionRemovesClientChannelsAndGraph()
        {
            var transport = new Phase50FakeTransport();
            var session = new FoxgloveSession("phase50", transport, new FixedClock(123), new DefaultSchemaRegistry());

            transport.RaiseText(7, JsonConvert.SerializeObject(new Advertise
            {
                Channels = new List<AdvertiseChannel>
                {
                    new AdvertiseChannel { Id = 42, Topic = "/client/topic", Encoding = "json" }
                }
            }));
            session.ClearSession();

            var delivered = false;
            session.OnClientMessage += (_, _, _, _) => delivered = true;
            transport.RaiseBinary(7, BuildClientMessageData(42, new byte[] { 1 }));
            Check(!delivered, "50E-1: ClearSession removes client-published channel state");

            transport.RaiseText(7, "{\"op\":\"subscribeConnectionGraph\"}");
            var graphJson = transport.SentText.LastOrDefault();
            Check(graphJson != null && !graphJson.Contains("/client/topic"),
                "50E-2: ClearSession removes stale connection graph state");
        }

        private static void VerifyDisposeDetachesClientMessageSubscribers()
        {
            var transport = new Phase50FakeTransport();
            var session = new FoxgloveSession("phase50", transport, new FixedClock(0), new DefaultSchemaRegistry());
            session.OnClientMessage += (_, _, _, _) => { };
            session.Dispose();
            var field = typeof(FoxgloveSession).GetField(
                "OnClientMessage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Check(field.GetValue(session) == null, "50E-3: Dispose detaches OnClientMessage subscribers");
        }

        private static void VerifySourceEmitterEscapesGeneratedStringLiterals()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "EscapedSource", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("Value", "string", "/debug/quote\"topic", 10f, "schema\"name"),
                new FoxgloveSourceEmitter.TopicMember("Other", "string", "/debug/back\\slash", 10f, "")
            });

            Check(source.Contains("/debug/quote\\\"topic") && source.Contains("schema\\\"name"),
                "50G-1: generated C# escapes quotes in topic and schema strings");
            Check(source.Contains("/debug/back\\\\slash"),
                "50G-2: generated C# escapes backslashes in topic strings");
        }

        private static void VerifyFoxgloveLogHubDomainReloadResetSource()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveLogHub.cs");
            Check(source.Contains("RuntimeInitializeLoadType.SubsystemRegistration"),
                "50H-1: FoxgloveLogHub has a subsystem registration reset hook");
            Check(Regex.IsMatch(source, @"static\s+void\s+Reset[^(\r\n]*\([^)]*\)[\s\S]*?_instance\s*=\s*null"),
                "50H-2: FoxgloveLogHub reset clears the static singleton instance");
        }

        private static void VerifyRecordingControllerDoubleAttachGuard()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/RecordingController.cs");
            var unsubscribeIndex = source.IndexOf("parameters.OnParameterChanged -= OnParameterChanged", StringComparison.Ordinal);
            var subscribeIndex = source.IndexOf("parameters.OnParameterChanged += OnParameterChanged", StringComparison.Ordinal);
            Check(unsubscribeIndex >= 0 && subscribeIndex > unsubscribeIndex,
                "50I-1: AttachToSession unsubscribes before subscribing parameter changes");
        }

        private static void VerifyMcapReplayRejectsOversizedInnerRecordLength()
        {
            var engine = new McapReplayEngine();
            SetPrivateField(engine, "_summary", new McapFileSummary
            {
                Statistics = new McapStatistics { MessageStartTime = 0, MessageEndTime = 10 },
                ChunkIndexes = new List<McapChunkIndex>
                {
                    new McapChunkIndex { MessageStartTime = 0, MessageEndTime = 10 }
                }
            });
            SetPrivateField(engine, "_currentChunkIdx", 0);
            SetPrivateField(engine, "_currentUncompressed", BuildChunkRecordHeader(McapWriter.OpcodeMessage, (ulong)int.MaxValue + 1UL));
            SetPrivateField(engine, "_readOffset", 0);
            SetPrivateProperty(engine, "IsLoaded", true);
            SetPrivateProperty(engine, "CanSeek", true);
            SetPrivateProperty(engine, "EndTimeNs", 10UL);
            SetPrivateProperty(engine, "CurrentStatus", McapReplayEngine.Status.Playing);

            Check(Throws<InvalidDataException>(() => engine.Tick(10)),
                "50J-1: oversized chunk inner message length throws InvalidDataException");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/McapReplayEngine.cs");
            Check(source.Contains("len > int.MaxValue") && source.Contains("InvalidDataException"),
                "50J-2: non-message chunk record length casts are guarded");
        }

        private static void VerifySceneCubeColorSetterSource()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/FoxgloveSceneCubePublisher.cs");
            Check(Regex.IsMatch(source, @"if\s*\(_color\s*==\s*value\)\s*\{\s*return;\s*\}"),
                "50K-1: SceneCubeColor returns immediately when color is unchanged");
            Check(Regex.IsMatch(source, @"_color\s*=\s*value;[\s\S]*?ApplyColorToRenderer\(value\);[\s\S]*?OnSceneCubeColorChanged\?\.Invoke\(value\);"),
                "50K-2: SceneCubeColor updates color, renderer, and event when changed");
        }

        private static string BuildHandshake(int requestTargetLength = 1, string extraHeader = null, int extraHeaderCount = 0)
        {
            var target = "/" + new string('x', Math.Max(0, requestTargetLength - 1));
            var sb = new StringBuilder();
            sb.Append("GET ").Append(target).Append(" HTTP/1.1\r\n");
            sb.Append("Host: 127.0.0.1\r\n");
            sb.Append("Connection: Upgrade\r\n");
            sb.Append("Upgrade: websocket\r\n");
            sb.Append("Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n");
            sb.Append("Sec-WebSocket-Version: 13\r\n");
            sb.Append("Sec-WebSocket-Protocol: foxglove.sdk.v1\r\n");
            if (extraHeader != null)
                sb.Append(extraHeader).Append("\r\n");
            for (var i = 0; i < extraHeaderCount; i++)
                sb.Append("X-Test-").Append(i).Append(": value\r\n");
            sb.Append("\r\n");
            return sb.ToString();
        }

        private static bool HandshakeRejected(string request, int port)
        {
            var response = SendRawHandshake(request, port);
            return !response.Contains("101 Switching Protocols");
        }

        private static bool HandshakeAccepted(string request, int port)
        {
            var response = SendRawHandshake(request, port);
            return response.Contains("101 Switching Protocols")
                   && response.Contains("Sec-WebSocket-Protocol: foxglove.sdk.v1");
        }

        private static string SendRawHandshake(string request, int port)
        {
            using var backend = new ManagedWsBackend();
            backend.Start("127.0.0.1", port);
            Thread.Sleep(50);

            using var client = new TcpClient();
            client.ReceiveTimeout = 2000;
            client.SendTimeout = 2000;
            client.Connect("127.0.0.1", port);
            var stream = client.GetStream();
            var bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);

            var buffer = new byte[4096];
            try
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                return Encoding.ASCII.GetString(buffer, 0, read);
            }
            catch (IOException)
            {
                return "";
            }
            catch (SocketException)
            {
                return "";
            }
        }

        private static int GetFreeTcpPort()
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

        private static byte[] BuildClientMessageData(uint channelId, byte[] payload)
        {
            var frame = new byte[5 + (payload?.Length ?? 0)];
            frame[0] = ClientOpcode.MessageData;
            frame[1] = (byte)(channelId & 0xFF);
            frame[2] = (byte)((channelId >> 8) & 0xFF);
            frame[3] = (byte)((channelId >> 16) & 0xFF);
            frame[4] = (byte)((channelId >> 24) & 0xFF);
            if (payload != null)
                Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
            return frame;
        }

        private static byte[] BuildChunkRecordHeader(byte opcode, ulong length)
        {
            var bytes = new byte[9];
            bytes[0] = opcode;
            for (var i = 0; i < 8; i++)
                bytes[1 + i] = (byte)((length >> (8 * i)) & 0xFF);
            return bytes;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repo root.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateProperty(object target, string name, object value)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
                throw new MissingMemberException(target.GetType().FullName, name);
            property.SetValue(target, value);
        }

        private static bool Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is TException)
            {
                return true;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }

        private sealed class FixedClock : IFoxgloveClock
        {
            public FixedClock(ulong nowNs) => NowNs = nowNs;
            public ulong NowNs { get; }
        }

        private sealed class Phase50FakeTransport : IFoxgloveTransport
        {
            public readonly List<string> SentText = new List<string>();
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) => SentText.Add(json);
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) => SentText.Add(json);
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() => Stop();
            public void RaiseConnected(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void RaiseDisconnected(uint clientId) => OnClientDisconnected?.Invoke(clientId);
            public void RaiseText(uint clientId, string text) => OnTextReceived?.Invoke(clientId, text);
            public void RaiseBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
