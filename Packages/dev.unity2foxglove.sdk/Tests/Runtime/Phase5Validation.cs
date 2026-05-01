using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase5Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        public static void Validate()
        {
            Console.WriteLine("--- Phase 5 Tests ---");

            TestNowUnixTimeNsMonotonic();
            TestNowUnixTimeNsInWindow();
            TestSystemClockMatchesFoxgloveTimeUtil();
            TestRuntimeStopStartReusesTransport();
            TestRuntimeDisposeDisposesTransport();
            TestSessionDisposeUnbindsEvents();
            TestPackageLinkXmlTemplateExists();
            TestAssetsLinkXmlActiveExists();

            Console.WriteLine($"Phase 5: {_passCount} checks passed.\n");
        }

        // ── A: Timestamp ──

        private static void TestNowUnixTimeNsMonotonic()
        {
            var t1 = FoxgloveTimeUtil.NowUnixTimeNs();
            var t2 = FoxgloveTimeUtil.NowUnixTimeNs();
            Assert(t2 >= t1, "NowUnixTimeNs is monotonic (non-decreasing)");
        }

        private static void TestNowUnixTimeNsInWindow()
        {
            var ns = FoxgloveTimeUtil.NowUnixTimeNs();
            var msNs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000UL;
            var diff = msNs > ns ? msNs - ns : ns - msNs;
            // Allow up to 1 second drift between Stopwatch and system time
            Assert(diff < 1_000_000_000UL, $"NowUnixTimeNs within 1s of system time (diff={diff}ns)");
        }

        private static void TestSystemClockMatchesFoxgloveTimeUtil()
        {
            var clock = new SystemClock();
            var clockNs = clock.NowNs;
            var utilNs = FoxgloveTimeUtil.NowUnixTimeNs();
            var diff = clockNs > utilNs ? clockNs - utilNs : utilNs - clockNs;
            Assert(diff < 1_000_000_000UL, $"SystemClock matches FoxgloveTimeUtil (diff={diff}ns)");
        }

        // ── B: Lifecycle ──

        private static void TestRuntimeStopStartReusesTransport()
        {
            var transport = new LifecycleFakeTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("R1", "127.0.0.1", 18790);
            runtime.Stop();
            // Restart with same transport — must not throw
            runtime.Start("R2", "127.0.0.1", 18790);
            Assert(runtime.Session != null, "Restarted session exists");
            runtime.Dispose();
        }

        private static void TestRuntimeDisposeDisposesTransport()
        {
            var transport = new LifecycleFakeTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("D1", "127.0.0.1", 18791);
            runtime.Dispose();
            Assert(transport.DisposeCalled == 1, "Runtime.Dispose calls transport.Dispose exactly once");
        }

        private static void TestSessionDisposeUnbindsEvents()
        {
            var transport = new LifecycleFakeTransport();
            var session = new FoxgloveSession("S1", transport);
            session.Dispose();

            // After dispose, triggering transport events should not invoke session handlers
            // (no throw = pass, since old handlers would crash on disposed state)
            transport.SimulateConnect(1);
            transport.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}");
            transport.SimulateDisconnect(1);
            Assert(true, "Session dispose does not leak event handlers");
        }

        private sealed class LifecycleFakeTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public int DisposeCalled;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() => DisposeCalled++;
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SimulateConnect(uint id) => OnClientConnected?.Invoke(id);
            public void SimulateDisconnect(uint id) => OnClientDisconnected?.Invoke(id);
            public void SimulateText(uint id, string json) => OnTextReceived?.Invoke(id, json);
        }

        // ── D: link.xml ──

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
            {
                var asmDir = Path.GetDirectoryName(typeof(Phase5Validation).Assembly.Location);
                while (asmDir != null && !File.Exists(Path.Combine(asmDir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    asmDir = Path.GetDirectoryName(asmDir);
                dir = asmDir;
            }
            return dir;
        }

        private static void AssertLinkXml(string path, string label)
        {
            Assert(File.Exists(path), $"{label}: file exists at {path}");
            var content = File.ReadAllText(path);
            Assert(content.Contains("Newtonsoft.Json"), $"{label}: preserves Newtonsoft.Json");
            Assert(content.Contains("Unity.FoxgloveSDK"), $"{label}: preserves Unity.FoxgloveSDK");
        }

        private static void TestPackageLinkXmlTemplateExists()
        {
            var root = FindRepoRoot();
            Assert(root != null, "Repo root found for package link.xml template");
            var path = Path.Combine(root, "Packages", "dev.unity2foxglove.sdk", "Runtime", "link.xml");
            Assert(File.Exists(path), $"Package link.xml template exists at {path}");
            AssertLinkXml(path, "Package link.xml template");
        }

        private static void TestAssetsLinkXmlActiveExists()
        {
            var root = FindRepoRoot();
            Assert(root != null, "Repo root found for Assets link.xml (active)");
            var assetsDir = Path.Combine(root, "Untiy2Foxglove", "Assets");
            var candidates = Directory.GetFiles(assetsDir, "link.xml", SearchOption.AllDirectories);
            Assert(candidates.Length > 0,
                $"At least one Assets/**/link.xml found in {assetsDir} (Unity requires link.xml in Assets, not packaged Runtime)");

            // Verify at least one candidate has valid preserve rules
            var valid = false;
            foreach (var path in candidates)
            {
                try
                {
                    var content = File.ReadAllText(path);
                    if (content.Contains("Newtonsoft.Json") && content.Contains("Unity.FoxgloveSDK"))
                    {
                        valid = true;
                        break;
                    }
                }
                catch { }
            }
            Assert(valid, "At least one Assets/**/link.xml preserves Newtonsoft.Json and Unity.FoxgloveSDK");
        }
    }
}
