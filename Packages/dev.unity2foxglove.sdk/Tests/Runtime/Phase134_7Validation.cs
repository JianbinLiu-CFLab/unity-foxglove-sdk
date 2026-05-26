// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-7 regression coverage for PlaybackControl request id
// bounds before transport frames enter runtime queues.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_7Validation
    {
        private const uint ClientId = 7;
        private const int ExpectedMaxPlaybackRequestIdBytes = 256;

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-7: Protocol Frames And Runtime Utilities ===");
            _passed = 0;

            PlaybackControlRequestIdAtAndBelowCapDecodes();
            PlaybackControlRequestIdAboveCapIsRejected();
            OversizedPlaybackControlFrameDoesNotReachRuntimeQueue();

            Console.WriteLine($"Phase 134-7: {_passed} checks passed.");
        }

        private static void PlaybackControlRequestIdAtAndBelowCapDecodes()
        {
            CheckDecodeAccepted(ExpectedMaxPlaybackRequestIdBytes - 1, "134-7A-1: request id below cap decodes");
            CheckDecodeAccepted(ExpectedMaxPlaybackRequestIdBytes, "134-7A-2: request id at cap decodes");
        }

        private static void PlaybackControlRequestIdAboveCapIsRejected()
        {
            var oversized = BuildPlaybackControlFrame(new string('x', ExpectedMaxPlaybackRequestIdBytes + 1));
            var ok = BinaryEncoding.TryDecodePlaybackControlRequest(
                oversized, out _, out _, out _, out _, out var requestId);

            Check(!ok, "134-7B-1: request id above cap is rejected by decoder");
            Check(requestId == null, "134-7B-2: rejected request does not allocate decoded id");
        }

        private static void OversizedPlaybackControlFrameDoesNotReachRuntimeQueue()
        {
            var transport = new Phase134_7FakeTransport();
            var session = CreatePlaybackSession(transport, out var runtime);

            transport.Connect(ClientId);
            transport.ClearBinary();

            transport.Binary(ClientId, BuildPlaybackControlFrame(new string('x', ExpectedMaxPlaybackRequestIdBytes + 1)));
            session.DrainPlaybackControls();

            Check(runtime.AppliedPlaybackControls == 0,
                "134-7C-1: oversized request id is not applied by runtime");
            Check(transport.BinariesFor(ClientId).Count == 0,
                "134-7C-2: oversized request id receives no PlaybackState response");

            transport.Binary(ClientId, BuildPlaybackControlFrame(new string('y', ExpectedMaxPlaybackRequestIdBytes)));
            session.DrainPlaybackControls();

            Check(runtime.AppliedPlaybackControls == 1,
                "134-7C-3: capped request id still reaches runtime");
            Check(DecodePlaybackStateRequestId(transport.BinariesFor(ClientId).Single()).Length
                  == ExpectedMaxPlaybackRequestIdBytes,
                "134-7C-4: capped request id is echoed in PlaybackState");
        }

        private static FoxgloveSession CreatePlaybackSession(
            Phase134_7FakeTransport transport,
            out Phase134_7RuntimeContext runtime)
        {
            var session = new FoxgloveSession("phase134-7", transport);
            runtime = new Phase134_7RuntimeContext();
            runtime.EnablePlayback();
            session.SetRuntimeContext(runtime);
            return session;
        }

        private static void CheckDecodeAccepted(int requestIdLength, string label)
        {
            var frame = BuildPlaybackControlFrame(new string('a', requestIdLength));
            var ok = BinaryEncoding.TryDecodePlaybackControlRequest(
                frame, out var command, out var speed, out var hasSeek, out var seekTimeNs, out var requestId);

            Check(ok, label);
            Check(command == 1, label + " command");
            Check(Math.Abs(speed - 1f) < 0.0001f, label + " speed");
            Check(hasSeek, label + " seek flag");
            Check(seekTimeNs == 1_000_000UL, label + " seek time");
            Check(requestId.Length == requestIdLength, label + " length");
        }

        private static byte[] BuildPlaybackControlFrame(string requestId)
        {
            var id = Encoding.UTF8.GetBytes(requestId ?? string.Empty);
            var frame = new byte[19 + id.Length];
            frame[0] = ClientOpcode.PlaybackControlRequest;
            frame[1] = 1;
            BinaryEncoding.WriteF32LE(frame, 2, 1f);
            frame[6] = 1;
            BinaryEncoding.WriteU64LE(frame, 7, 1_000_000UL);
            BinaryEncoding.WriteU32LE(frame, 15, (uint)id.Length);
            Buffer.BlockCopy(id, 0, frame, 19, id.Length);
            return frame;
        }

        private static string DecodePlaybackStateRequestId(byte[] frame)
        {
            if (frame == null || frame.Length < 19 || frame[0] != ServerOpcode.PlaybackState)
                throw new InvalidOperationException("Not a PlaybackState frame.");

            var idLength = BinaryEncoding.ReadU32LE(frame, 15);
            if (idLength > int.MaxValue || idLength > frame.Length - 19)
                throw new InvalidOperationException("Malformed PlaybackState requestId length.");

            return idLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(frame, 19, (int)idLength);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }

        private sealed class Phase134_7RuntimeContext : IRuntimeContext
        {
            public bool PlaybackEnabled { get; private set; }
            public FoxgloveAssetRegistry Assets { get; } = new();
            public int AppliedPlaybackControls { get; private set; }

            public void EnablePlayback() => PlaybackEnabled = true;
            public ulong GetPlaybackStartNs() => 0UL;
            public ulong GetPlaybackEndNs() => 10_000_000UL;
            public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs) { }
            public void ReplaySeek(ulong timeNs) { }
            public void ReplayPlay() { }
            public void ReplayPause() { }

            public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
                => State(didSeek, requestId);

            public PlaybackClock.PlaybackStateSnapshot ApplyPlaybackControl(
                byte cmd, float speed, bool hasSeek, ulong seekNs, string requestId)
            {
                AppliedPlaybackControls++;
                return State(hasSeek, requestId);
            }

            private static PlaybackClock.PlaybackStateSnapshot State(bool didSeek, string requestId)
            {
                return new PlaybackClock.PlaybackStateSnapshot
                {
                    Status = 1,
                    CurrentTimeNs = 1_000_000UL,
                    Speed = 1f,
                    DidSeek = didSeek,
                    RequestId = requestId
                };
            }
        }

        private sealed class Phase134_7FakeTransport : IFoxgloveTransport
        {
            private readonly Dictionary<uint, List<byte[]>> _sentBinaries = new();
            private readonly HashSet<uint> _connectedClients = new();

            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) { }
            public void SendText(uint clientId, string json) { }

            public void BroadcastBinary(byte[] data)
            {
                foreach (var clientId in _connectedClients)
                    SendBinary(clientId, data);
            }

            public void SendBinary(uint clientId, byte[] data)
            {
                if (!_sentBinaries.TryGetValue(clientId, out var frames))
                {
                    frames = new List<byte[]>();
                    _sentBinaries[clientId] = frames;
                }

                frames.Add(data);
            }

            public IReadOnlyList<byte[]> BinariesFor(uint clientId)
                => _sentBinaries.TryGetValue(clientId, out var frames)
                    ? frames
                    : Array.Empty<byte[]>();

            public void ClearBinary() => _sentBinaries.Clear();

            public void Connect(uint clientId)
            {
                _connectedClients.Add(clientId);
                OnClientConnected?.Invoke(clientId);
            }

            public void Binary(uint clientId, byte[] data)
                => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
