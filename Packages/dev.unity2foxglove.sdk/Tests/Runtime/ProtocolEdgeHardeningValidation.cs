// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 144 protocol-edge validation for WebSocket fragmentation,
// status wire messages, and Foxglove app URL helpers.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class ProtocolEdgeHardeningValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 144: Protocol Edge Hardening ===");
            _passed = 0;

            FrameCodecDecodesContinuationFrames();
            ReceiveLoopReassemblesFragmentedTextMessages();
            ReceiveLoopReassemblesFragmentedBinaryMessages();
            ReceiveLoopRejectsMalformedFragmentSequences();
            StatusWireShapeRemainsOfficial();
            FoxgloveAppUrlHelpersKeepOfficialQueryShape();
            ValidationRegistryWiresPhase144();

            Console.WriteLine($"Phase 144: {_passed} checks passed.");
        }

        private static void FrameCodecDecodesContinuationFrames()
        {
            var payload = Encoding.UTF8.GetBytes("tail");
            var frame = ReadFrameFromBytes(BuildClientFrame(WsOpcode.Continuation, payload, fin: true));

            Check(frame != null, "144A-1: codec accepts masked continuation frames");
            Check(frame != null && frame.Fin, "144A-2: continuation frame preserves FIN");
            Check(frame != null && frame.Opcode == WsOpcode.Continuation,
                "144A-3: continuation frame preserves opcode");
            Check(frame != null && frame.Payload.SequenceEqual(payload),
                "144A-4: continuation payload is unmasked");
        }

        private static void ReceiveLoopReassemblesFragmentedTextMessages()
        {
            var result = RunReceiveLoop(
                BuildClientFrame(WsOpcode.Text, Encoding.UTF8.GetBytes("{\"op\":\""), fin: false),
                BuildClientFrame(WsOpcode.Continuation, Encoding.UTF8.GetBytes("clientAdvertise\"}"), fin: true));

            Check(result.TextMessages.Count == 1,
                "144B-1: fragmented text message dispatches exactly once");
            Check(result.TextMessages.SingleOrDefault() == "{\"op\":\"clientAdvertise\"}",
                "144B-2: fragmented text payload is reassembled before dispatch");
            Check(result.BinaryMessages.Count == 0,
                "144B-3: fragmented text does not dispatch binary messages");
        }

        private static void ReceiveLoopReassemblesFragmentedBinaryMessages()
        {
            var result = RunReceiveLoop(
                BuildClientFrame(WsOpcode.Binary, new byte[] { 1, 2 }, fin: false),
                BuildClientFrame(WsOpcode.Continuation, new byte[] { 3 }, fin: false),
                BuildClientFrame(WsOpcode.Ping, Encoding.UTF8.GetBytes("ok"), fin: true),
                BuildClientFrame(WsOpcode.Continuation, new byte[] { 4, 5 }, fin: true));

            Check(result.BinaryMessages.Count == 1,
                "144C-1: fragmented binary message dispatches exactly once");
            Check(result.BinaryMessages.SingleOrDefault()?.SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }) == true,
                "144C-2: fragmented binary payload is reassembled before dispatch");
            Check(result.TextMessages.Count == 0,
                "144C-3: fragmented binary does not dispatch text messages");
            Check(result.Logger.ErrorCount == 0,
                "144C-4: ping inside a fragmented message does not produce receive errors");
        }

        private static void ReceiveLoopRejectsMalformedFragmentSequences()
        {
            var orphanContinuation = RunReceiveLoop(
                BuildClientFrame(WsOpcode.Continuation, Encoding.UTF8.GetBytes("tail"), fin: true));
            Check(orphanContinuation.TextMessages.Count == 0 && orphanContinuation.BinaryMessages.Count == 0,
                "144D-1: orphan continuation frame is rejected without dispatch");

            var nestedStart = RunReceiveLoop(
                BuildClientFrame(WsOpcode.Text, Encoding.UTF8.GetBytes("start"), fin: false),
                BuildClientFrame(WsOpcode.Binary, new byte[] { 9 }, fin: true));
            Check(nestedStart.TextMessages.Count == 0 && nestedStart.BinaryMessages.Count == 0,
                "144D-2: new data frame while fragmented message is open is rejected");

            var fragmentedControl = ReadFrameFromBytes(
                BuildClientFrame(WsOpcode.Ping, Encoding.UTF8.GetBytes("x"), fin: false));
            Check(fragmentedControl == null,
                "144D-3: fragmented control frames remain rejected by the codec");

            var oversizedFragments = new List<byte[]>
            {
                BuildClientFrame(WsOpcode.Binary, new byte[65535], fin: false)
            };
            for (var i = 0; i < 64; i++)
                oversizedFragments.Add(BuildClientFrame(WsOpcode.Continuation, new byte[65535], fin: i == 63));

            var oversized = RunReceiveLoop(oversizedFragments.ToArray());
            Check(oversized.TextMessages.Count == 0 && oversized.BinaryMessages.Count == 0,
                "144D-4: fragmented messages above the aggregate size limit are rejected");
        }

        private static void StatusWireShapeRemainsOfficial()
        {
            var status = JObject.Parse(JsonConvert.SerializeObject(new StatusMessage
            {
                Level = FoxgloveStatusLevel.Error,
                Message = "Phase144",
                Id = "phase144/status"
            }));
            Check((string)status["op"] == "status",
                "144E-1: status op remains official");
            Check((int)status["level"] == 2,
                "144E-2: error status level remains numeric");
            Check((string)status["id"] == "phase144/status",
                "144E-3: status id is included when provided");

            var withoutId = JObject.Parse(JsonConvert.SerializeObject(new StatusMessage
            {
                Level = FoxgloveStatusLevel.Info,
                Message = "No id"
            }));
            Check(withoutId["id"] == null,
                "144E-4: empty status id is omitted");

            var remove = JObject.Parse(JsonConvert.SerializeObject(new RemoveStatusMessage
            {
                StatusIds = new List<string> { "phase144/status" }
            }));
            Check((string)remove["op"] == "removeStatus",
                "144E-5: removeStatus op remains official");
            Check(remove["statusIds"] is JArray,
                "144E-6: removeStatus uses statusIds array");
        }

        private static void FoxgloveAppUrlHelpersKeepOfficialQueryShape()
        {
            Check(FoxgloveAppUrl.BuildWebSocketEndpoint("0.0.0.0", 8765, secure: false) == "ws://127.0.0.1:8765",
                "144F-1: wildcard bind host maps to loopback connect URL");
            Check(FoxgloveAppUrl.BuildWebSocketEndpoint("::1", 8765, secure: true) == "wss://[::1]:8765",
                "144F-2: IPv6 connect hosts are bracketed");

            var redacted = FoxgloveAppUrl.BuildHostedWebSocketUrl(
                "127.0.0.1",
                8765,
                secure: false,
                token: "secret token",
                redactToken: true);
            Check(redacted.Contains("ds=foxglove-websocket", StringComparison.Ordinal)
                  && redacted.Contains("token%3DREDACTED", StringComparison.Ordinal)
                  && !redacted.Contains("secret", StringComparison.Ordinal),
                "144F-3: hosted URL redacts only the token value");

            var remoteFile = FoxgloveAppUrl.BuildRemoteFileDesktopUrl("http://127.0.0.1:8891/v1/files/run.mcap");
            Check(remoteFile == "foxglove://open?ds=remote-file&ds.url=http%3A%2F%2F127.0.0.1%3A8891%2Fv1%2Ffiles%2Frun.mcap",
                "144F-4: remote-file desktop URL keeps foxglove open query shape");
        }

        private static void ValidationRegistryWiresPhase144()
        {
            var registry = File.ReadAllText(RepoPath("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs"));
            var project = File.ReadAllText(RepoPath("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj"));

            Check(registry.Contains("--phase144", StringComparison.Ordinal)
                  && registry.Contains("ProtocolEdgeHardeningValidation.Validate", StringComparison.Ordinal),
                "144G-1: validation registry wires --phase144");
            Check(project.Contains("ProtocolEdgeHardeningValidation.cs", StringComparison.Ordinal),
                "144G-2: runtime validation project includes protocol edge hardening validation");
        }

        private static ReceiveLoopResult RunReceiveLoop(params byte[][] frames)
        {
            var input = frames.SelectMany(frame => frame).ToArray();
            var stream = new Phase144LoopbackStream(input);
            var logger = new Phase144CaptureLogger();
            using var backend = new ManagedWsBackend(logger);
            using var tcpClient = new TcpClient();
            using var conn = new WsConnection(
                tcpClient,
                stream,
                ManagedWebSocketOptions.DefaultMaxQueuedFrames,
                ManagedWebSocketOptions.DefaultMaxQueuedBytes);
            var result = new ReceiveLoopResult(logger);

            backend.OnTextReceived += (_, text) => result.TextMessages.Add(text);
            backend.OnBinaryReceived += (_, data) => result.BinaryMessages.Add(data);

            var receiveLoop = typeof(ManagedWsBackend).GetMethod(
                "ReceiveLoop",
                BindingFlags.Instance | BindingFlags.NonPublic);
            receiveLoop.Invoke(backend, new object[] { 1u, conn, CancellationToken.None });

            return result;
        }

        private static WsFrame ReadFrameFromBytes(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            return WsFrameCodec.TryReadFrame(stream, out var frame) ? frame : null;
        }

        private static byte[] BuildClientFrame(byte opcode, byte[] payload, bool fin)
        {
            payload ??= Array.Empty<byte>();
            var header = new List<byte>();
            header.Add((byte)((fin ? 0x80 : 0x00) | opcode));

            if (payload.Length <= 125)
            {
                header.Add((byte)(0x80 | payload.Length));
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                header.Add(0xFE);
                header.Add((byte)(payload.Length >> 8));
                header.Add((byte)payload.Length);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(payload));
            }

            var mask = new byte[] { 0x12, 0x34, 0x56, 0x78 };
            header.AddRange(mask);
            for (var i = 0; i < payload.Length; i++)
                header.Add((byte)(payload[i] ^ mask[i % mask.Length]));
            return header.ToArray();
        }

        private static string RepoPath(string relativePath)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.GetFullPath(Path.Combine(dir, relativePath));
                if (File.Exists(candidate))
                    return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }

            return Path.GetFullPath(relativePath);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private sealed class ReceiveLoopResult
        {
            public ReceiveLoopResult(Phase144CaptureLogger logger)
            {
                Logger = logger;
            }

            public List<string> TextMessages { get; } = new List<string>();
            public List<byte[]> BinaryMessages { get; } = new List<byte[]>();
            public Phase144CaptureLogger Logger { get; }
        }

        private sealed class Phase144LoopbackStream : Stream
        {
            private readonly MemoryStream _input;
            private readonly MemoryStream _output = new MemoryStream();

            public Phase144LoopbackStream(byte[] input)
            {
                _input = new MemoryStream(input ?? Array.Empty<byte>());
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _input.Length;
            public override long Position
            {
                get => _input.Position;
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
                => _input.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count)
                => _output.Write(buffer, offset, count);
        }

        private sealed class Phase144CaptureLogger : Unity.FoxgloveSDK.Core.IFoxgloveLogger
        {
            public int ErrorCount { get; private set; }

            public void Log(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message) => ErrorCount++;
        }
    }
}
