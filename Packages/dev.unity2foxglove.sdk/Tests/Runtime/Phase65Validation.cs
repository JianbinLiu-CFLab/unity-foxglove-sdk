// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 65 regression coverage for multi-client PlaybackControl
// response targeting and requestId isolation.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates PlaybackControl response targeting when multiple Foxglove
    /// clients are connected to one Unity2Foxglove server.
    /// </summary>
    public static class Phase65Validation
    {
        private const uint DesktopClientId = 7;
        private const uint WebClientId = 8;

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 65: PlaybackControl Multi-Client Parity ===");
            _passed = 0;

            PlaybackControlResponseTargetsRequestingClient();
            PlaybackControlRequestIdsDoNotCrossClients();
            PlaybackControlBurstUsesTargetedControlFrames();

            Console.WriteLine($"Phase 65: {_passed} checks passed.");
        }

        private static void PlaybackControlResponseTargetsRequestingClient()
        {
            var transport = new Phase65FakeTransport();
            using var session = CreatePlaybackSession(transport);

            transport.Connect(DesktopClientId);
            transport.Connect(WebClientId);
            transport.ClearBinary();

            transport.Binary(DesktopClientId, BuildPlaybackControlFrame("desktop-1", hasSeek: true));
            session.DrainPlaybackControls();

            Check(PlaybackStateFramesFor(transport, DesktopClientId).Count == 1,
                "65A-1: requesting client receives one PlaybackState response");
            Check(PlaybackStateFramesFor(transport, WebClientId).Count == 0,
                "65A-2: non-requesting client receives no request-correlated PlaybackState");
            Check(transport.BroadcastPlaybackStateCount == 0,
                "65A-3: PlaybackControl responses are not broadcast");
            Check(DecodePlaybackStateRequestId(PlaybackStateFramesFor(transport, DesktopClientId)[0]) == "desktop-1",
                "65A-4: targeted PlaybackState preserves the requestId");
        }

        private static void PlaybackControlRequestIdsDoNotCrossClients()
        {
            var transport = new Phase65FakeTransport();
            using var session = CreatePlaybackSession(transport);

            transport.Connect(DesktopClientId);
            transport.Connect(WebClientId);
            transport.ClearBinary();

            transport.Binary(DesktopClientId, BuildPlaybackControlFrame("desktop-1", hasSeek: true));
            transport.Binary(WebClientId, BuildPlaybackControlFrame("web-1", hasSeek: true));
            session.DrainPlaybackControls();

            var desktopIds = PlaybackStateFramesFor(transport, DesktopClientId)
                .Select(DecodePlaybackStateRequestId)
                .ToList();
            var webIds = PlaybackStateFramesFor(transport, WebClientId)
                .Select(DecodePlaybackStateRequestId)
                .ToList();

            Check(desktopIds.SequenceEqual(new[] { "desktop-1" }),
                "65B-1: desktop client only receives its own requestId");
            Check(webIds.SequenceEqual(new[] { "web-1" }),
                "65B-2: web client only receives its own requestId");
            Check(transport.BroadcastPlaybackStateCount == 0,
                "65B-3: mixed-client PlaybackControl responses are not broadcast");
        }

        private static void PlaybackControlBurstUsesTargetedControlFrames()
        {
            var transport = new Phase65FakeTransport();
            using var session = CreatePlaybackSession(transport);

            transport.Connect(DesktopClientId);
            transport.Connect(WebClientId);
            transport.ClearBinary();

            for (var i = 0; i < 5; i++)
                transport.Binary(DesktopClientId, BuildPlaybackControlFrame("desktop-burst-" + i, hasSeek: true));

            session.DrainPlaybackControls();

            Check(PlaybackStateFramesFor(transport, DesktopClientId).Count == 5,
                "65C-1: burst requests receive targeted PlaybackState responses");
            Check(PlaybackStateFramesFor(transport, WebClientId).Count == 0,
                "65C-2: burst responses do not leak to other clients");
            Check(transport.BroadcastPlaybackStateCount == 0,
                "65C-3: burst responses do not use BroadcastBinary");
        }

        private static FoxgloveSession CreatePlaybackSession(Phase65FakeTransport transport)
        {
            var session = new FoxgloveSession("phase65", transport);
            var runtime = new Phase65RuntimeContext();
            runtime.EnablePlayback();
            session.SetRuntimeContext(runtime);
            return session;
        }

        private static List<byte[]> PlaybackStateFramesFor(Phase65FakeTransport transport, uint clientId)
            => transport.BinariesFor(clientId)
                .Where(frame => frame.Length > 0 && frame[0] == ServerOpcode.PlaybackState)
                .ToList();

        private static byte[] BuildPlaybackControlFrame(string requestId, bool hasSeek)
        {
            const int opcodeOffset = 0;
            const int commandOffset = 1;
            const int speedOffset = 2;
            const int hasSeekOffset = 6;
            const int seekTimeOffset = 7;
            const int requestIdLengthOffset = 15;
            const int requestIdPayloadOffset = 19;

            var id = Encoding.UTF8.GetBytes(requestId ?? string.Empty);
            var frame = new byte[requestIdPayloadOffset + id.Length];
            frame[opcodeOffset] = ClientOpcode.PlaybackControlRequest;
            frame[commandOffset] = 1;
            BinaryEncoding.WriteF32LE(frame, speedOffset, 1f);
            frame[hasSeekOffset] = hasSeek ? (byte)1 : (byte)0;
            BinaryEncoding.WriteU64LE(frame, seekTimeOffset, 1_000_000UL);
            BinaryEncoding.WriteU32LE(frame, requestIdLengthOffset, (uint)id.Length);
            Buffer.BlockCopy(id, 0, frame, requestIdPayloadOffset, id.Length);
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

        /// <summary>
        /// Runtime stub that enables PlaybackControl without involving MCAP
        /// replay files or Unity scene state.
        /// </summary>
        private sealed class Phase65RuntimeContext : IRuntimeContext
        {
            public bool PlaybackEnabled { get; private set; }
            public FoxgloveAssetRegistry Assets { get; } = new FoxgloveAssetRegistry();

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
                => State(hasSeek, requestId);

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

        /// <summary>
        /// Fake transport that records per-client binary sends and exposes
        /// broadcast usage so request/response targeting can be asserted.
        /// </summary>
        private sealed class Phase65FakeTransport : IFoxgloveTransport
        {
            private readonly Dictionary<uint, List<byte[]>> _sentBinaries = new();
            private readonly HashSet<uint> _connectedClients = new();

            public int BroadcastPlaybackStateCount { get; private set; }
            public bool IsRunning { get; private set; }

            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) { }

            public void BroadcastBinary(byte[] data)
            {
                if (data != null && data.Length > 0 && data[0] == ServerOpcode.PlaybackState)
                    BroadcastPlaybackStateCount++;

                foreach (var clientId in _connectedClients)
                    SendBinary(clientId, data);
            }

            public void SendText(uint clientId, string json) { }

            public void SendBinary(uint clientId, byte[] data)
            {
                if (!_sentBinaries.TryGetValue(clientId, out var frames))
                {
                    frames = new List<byte[]>();
                    _sentBinaries[clientId] = frames;
                }

                frames.Add(data);
            }

            public void Connect(uint clientId)
            {
                _connectedClients.Add(clientId);
                if (!_sentBinaries.ContainsKey(clientId))
                    _sentBinaries[clientId] = new List<byte[]>();
                OnClientConnected?.Invoke(clientId);
            }

            public void Disconnect(uint clientId)
            {
                _connectedClients.Remove(clientId);
                OnClientDisconnected?.Invoke(clientId);
            }

            public void Text(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);

            public void Binary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);

            public IReadOnlyList<byte[]> BinariesFor(uint clientId)
                => _sentBinaries.TryGetValue(clientId, out var frames) ? frames : Array.Empty<byte[]>();

            public void ClearBinary()
            {
                foreach (var frames in _sentBinaries.Values)
                    frames.Clear();
                BroadcastPlaybackStateCount = 0;
            }
        }
    }
}
