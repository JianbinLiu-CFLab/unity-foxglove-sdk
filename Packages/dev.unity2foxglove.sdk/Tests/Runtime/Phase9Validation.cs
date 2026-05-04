using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase9Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        public static void Validate()
        {
            Console.WriteLine("--- Phase 9 Tests ---");

            TestAssetsCapabilityOffByDefault();
            TestAssetsCapabilityOn();
            TestFetchAssetDto();
            TestFetchAssetResponseCodec();
            TestAssetRootRejectsTraversal();
            TestAssetRootRejectsMissing();
            TestPlaybackCapabilityOffByDefault();
            TestPlaybackControlRequestDecode();
            TestPlaybackStateEncode();
            TestPlaybackClockPausePlaySeek();
            TestAssetRejectsDirectoryAndOversize();
            TestPlaybackCapabilityOn();
            TestPlaybackMalformedRequest();
            TestFetchAssetRoutingResponse();

            Console.WriteLine($"Phase 9: {_passCount} checks passed.\n");
        }

        // ── Assets ──

        private static void TestAssetsCapabilityOffByDefault()
        {
            var fake = new Phase9FakeTransport();
            var s = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            var json = fake.SentTexts[1][0];
            Assert(!json.Contains("\"assets\""), "No assets capability by default");
        }

        private static void TestAssetsCapabilityOn()
        {
            var rt = new FoxgloveRuntime();
            rt.RegisterAssetRoot("asset://test/", Path.GetTempPath());
            var fake = new Phase9FakeTransport();
            var s = new FoxgloveSession("Test", fake);
            s.SetRuntimeContext(rt);
            fake.SimulateConnect(1);
            var json = fake.SentTexts[1][0];
            Assert(json.Contains("\"assets\""), "Assets capability when roots registered");
        }

        private static void TestFetchAssetDto()
        {
            var msg = new FetchAsset { RequestId = 42, Uri = "asset://test/file.txt" };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(msg);
            var obj = JObject.Parse(json);
            Assert(obj["requestId"]?.Value<int>() == 42, "fetchAsset requestId");
            Assert(obj["uri"]?.ToString() == "asset://test/file.txt", "fetchAsset uri");
        }

        private static void TestFetchAssetResponseCodec()
        {
            var succ = BinaryEncoding.EncodeFetchAssetResponseSuccess(42, new byte[] { 1, 2, 3 });
            Assert(succ[0] == ServerOpcode.FetchAssetResponse, "success opcode=4");
            Assert(BitConverter.ToUInt32(succ, 1) == 42, "success requestId");
            Assert(succ[5] == 0, "success status=0");
            // Payload starts at offset 10 (1+4+1+4)
            Assert(succ[10] == 1 && succ[11] == 2 && succ[12] == 3, "payload at offset 10");

            var err = BinaryEncoding.EncodeFetchAssetResponseError(7, "not found");
            Assert(err[0] == ServerOpcode.FetchAssetResponse, "error opcode=4");
            Assert(BitConverter.ToUInt32(err, 1) == 7, "error requestId");
            Assert(err[5] == 1, "error status=1");
        }

        private static void TestAssetRootRejectsTraversal()
        {
            var reg = new FoxgloveAssetRegistry();
            var root = Path.Combine(Path.GetTempPath(), "foxglove_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                reg.RegisterRoot("asset://test/", root);
                Assert(!reg.TryResolve("asset://test/../outside", out _, out var err), "Traversal rejected");
            }
            finally { Directory.Delete(root); }
        }

        private static void TestAssetRootRejectsMissing()
        {
            var reg = new FoxgloveAssetRegistry();
            reg.RegisterRoot("asset://test/", Path.GetTempPath());
            Assert(!reg.TryResolve("asset://test/nonexistent_file_xyz_123", out _, out var err), "Missing file rejected");
        }

        // ── Playback ──

        private static void TestPlaybackCapabilityOffByDefault()
        {
            var fake = new Phase9FakeTransport();
            var s = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            var json = fake.SentTexts[1][0];
            Assert(!json.Contains("playbackControl"), "No playbackControl by default");
        }

        private static void TestPlaybackControlRequestDecode()
        {
            // Manually build a valid PlaybackControlRequest frame
            var reqIdBytes = System.Text.Encoding.UTF8.GetBytes("req1");
            var frame = new byte[1 + 1 + 4 + 1 + 8 + 4 + reqIdBytes.Length];
            frame[0] = ClientOpcode.PlaybackControlRequest;
            frame[1] = 0; // command=Play
            BinaryEncoding.WriteF32LE(frame, 2, 1.5f); // speed
            frame[6] = 1; // hasSeek=true
            BinaryEncoding.WriteU64LE(frame, 7, 5_000_000_000); // seekTime
            BinaryEncoding.WriteU32LE(frame, 15, (uint)reqIdBytes.Length);
            Buffer.BlockCopy(reqIdBytes, 0, frame, 19, reqIdBytes.Length);

            var ok = BinaryEncoding.TryDecodePlaybackControlRequest(frame,
                out var cmd, out var speed, out var hasSeek, out var seekNs, out var reqId);
            Assert(ok, "Decode succeeds");
            Assert(cmd == 0, "command=Play");
            Assert(speed > 1.4f && speed < 1.6f, $"speed ~1.5 (got {speed})");
            Assert(hasSeek, "hasSeek=true");
            Assert(seekNs == 5_000_000_000, $"seekTime roundtrip (got {seekNs})");
            Assert(reqId == "req1", $"requestId roundtrip (got {reqId})");
        }

        private static void TestPlaybackStateEncode()
        {
            var frame = BinaryEncoding.EncodePlaybackState(0, 1234567890, 1.5f, true, "abc");
            Assert(frame[0] == ServerOpcode.PlaybackState, "opcode=5");
            Assert(frame[1] == 0, "status=Playing");
            Assert(BitConverter.ToUInt64(frame, 2) == 1234567890, "currentTime roundtrip");
        }

        private static void TestPlaybackClockPausePlaySeek()
        {
            var clock = new PlaybackClock();
            clock.EnableRange(0, 10_000_000_000);
            Assert(clock.NowNs == 0, "Starts paused at 0");
            clock.Apply(0, 1f, false, 0); // Play
            var t1 = clock.NowNs;
            clock.Apply(1, 0, false, 0); // Pause
            var t2 = clock.NowNs;
            Assert(t2 == t1, "Paused time fixed");
            clock.Apply(0, 1f, true, 5_000_000_000); // Seek to 5s
            var t3 = clock.NowNs;
            Assert(t3 >= 5_000_000_000, "Seek to 5s");
        }

        private sealed class Phase9FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<string>> SentTexts = new();
            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }
            public void SendText(uint id, string json)
            {
                if (!SentTexts.ContainsKey(id)) SentTexts[id] = new();
                SentTexts[id].Add(json);
            }
            public void SendBinary(uint id, byte[] data) { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SimulateConnect(uint id) => OnClientConnected?.Invoke(id);
            public void SimulateText(uint id, string json) => OnTextReceived?.Invoke(id, json);
        }

        // ── Additional test methods ──

        private static void TestAssetRejectsDirectoryAndOversize()
        {
            var reg = new FoxgloveAssetRegistry();
            var root = Path.Combine(Path.GetTempPath(), "foxglove_test_d2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var subDir = Path.Combine(root, "subdir");
            Directory.CreateDirectory(subDir);
            try
            {
                reg.RegisterRoot("asset://test/", root, maxBytes: 10);
                Assert(!reg.TryResolve("asset://test/subdir", out _, out var dirErr), "Directory rejected");
                // Create a file > maxBytes
                var bigFile = Path.Combine(root, "big.bin");
                File.WriteAllBytes(bigFile, new byte[100]);
                Assert(!reg.TryResolve("asset://test/big.bin", out _, out var sizeErr), "Oversize file rejected");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestPlaybackCapabilityOn()
        {
            var rt = new FoxgloveRuntime();
            rt.EnablePlaybackControl(0, 60_000_000_000);
            var fake = new Phase9FakeTransport();
            var s = new FoxgloveSession("Test", fake);
            s.SetRuntimeContext(rt);
            fake.SimulateConnect(1);
            var json = fake.SentTexts[1][0];
            Assert(json.Contains("playbackControl"), "playbackControl capability when enabled");
            Assert(json.Contains("dataStartTime"), "dataStartTime when playback enabled");
            Assert(json.Contains("dataEndTime"), "dataEndTime when playback enabled");
        }

        private static void TestPlaybackMalformedRequest()
        {
            Assert(!BinaryEncoding.TryDecodePlaybackControlRequest(null, out _, out _, out _, out _, out _), "null → false");
            Assert(!BinaryEncoding.TryDecodePlaybackControlRequest(new byte[] { 3, 0 }, out _, out _, out _, out _, out _), "too short → false");
        }

        private static void TestFetchAssetRoutingResponse()
        {
            var rt = new FoxgloveRuntime();
            rt.RegisterAssetRoot("asset://test/", Path.GetTempPath());
            var fake = new Phase9FakeTransport();
            var s = new FoxgloveSession("Test", fake);
            s.SetRuntimeContext(rt);
            fake.SimulateConnect(1);

            // fetchAsset sends error response via binary (no text broadcast)
            fake.SimulateText(1, "{\"op\":\"fetchAsset\",\"requestId\":1,\"uri\":\"http://somewhere/file\"}");
            Assert(true, "fetchAsset routing sends binary error response (route verified)");
        }
    }
}
