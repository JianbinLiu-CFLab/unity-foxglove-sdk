// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Core.Session
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Session")]
    public sealed class SessionPlaybackHandlerTests
    {
        [Fact]
        public void DrainProcessesOnlyControlsPresentAtTickStart()
        {
            const uint clientId = 7;
            const int totalControls = 4;
            SessionPlaybackHandler handler = null;
            var runtime = new ReentrantRuntimeContext();
            var transport = new RecordingTransport();

            handler = new SessionPlaybackHandler(() => runtime, transport, null, () => { });
            runtime.AfterApply = appliedCount =>
            {
                if (appliedCount < totalControls)
                    Assert.True(handler.HandleRequest(clientId, BuildPlaybackControlFrame($"request-{appliedCount + 1}")));
            };

            Assert.True(handler.HandleRequest(clientId, BuildPlaybackControlFrame("request-1")));

            for (var expectedApplied = 1; expectedApplied <= totalControls; expectedApplied++)
            {
                handler.Drain();
                Assert.Equal(expectedApplied, runtime.AppliedCount);
                Assert.Equal(expectedApplied, transport.SentFrames.Count);
            }

            handler.Drain();
            Assert.Equal(totalControls, runtime.AppliedCount);
            Assert.Equal(totalControls, transport.SentFrames.Count);
        }

        private static byte[] BuildPlaybackControlFrame(string requestId)
        {
            var id = Encoding.UTF8.GetBytes(requestId);
            var frame = new byte[19 + id.Length];
            frame[0] = ClientOpcode.PlaybackControlRequest;
            frame[1] = 1;
            BinaryEncoding.WriteF32LE(frame, 2, 1f);
            frame[6] = 0;
            BinaryEncoding.WriteU64LE(frame, 7, 0);
            BinaryEncoding.WriteU32LE(frame, 15, (uint)id.Length);
            Buffer.BlockCopy(id, 0, frame, 19, id.Length);
            return frame;
        }

        private sealed class ReentrantRuntimeContext : IRuntimeContext
        {
            public bool PlaybackEnabled => true;
            public FoxgloveAssetRegistry Assets { get; } = new();
            public int AppliedCount { get; private set; }
            public Action<int> AfterApply { get; set; }

            public ulong GetPlaybackStartNs() => 0;
            public ulong GetPlaybackEndNs() => 1;
            public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs) { }

            public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
            {
                return new PlaybackClock.PlaybackStateSnapshot
                {
                    Status = 1,
                    CurrentTimeNs = 0,
                    Speed = 1f,
                    DidSeek = didSeek,
                    RequestId = requestId
                };
            }

            public PlaybackClock.PlaybackStateSnapshot ApplyPlaybackControl(
                byte cmd, float speed, bool hasSeek, ulong seekNs, string requestId)
            {
                AppliedCount++;
                AfterApply?.Invoke(AppliedCount);
                return GetPlaybackState(hasSeek, requestId);
            }

            public void ReplaySeek(ulong timeNs) { }
            public void ReplayPlay() { }
            public void ReplayPause() { }
            public void RequestReplaySubscriberBackfill() { }
        }

        private sealed class RecordingTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public List<byte[]> SentFrames { get; } = new();

            public event Action<uint> OnClientConnected { add { } remove { } }
            public event Action<uint> OnClientDisconnected { add { } remove { } }
            public event Action<uint, string> OnTextReceived { add { } remove { } }
            public event Action<uint, byte[]> OnBinaryReceived { add { } remove { } }

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) => SentFrames.Add(data);
        }
    }
}
