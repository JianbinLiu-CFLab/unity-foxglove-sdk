using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_48Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-48 Tests ---");
            _passed = 0;

            VerifyManagedWsBackendAvoidsClientSnapshots();
            VerifyHandshakeAllocationOptimizations();
            VerifyTokenAndValidationWaitOptimizations();
            VerifyRegistry();

            Console.WriteLine("Phase 164-48: " + _passed + " checks passed.\n");
        }

        private static void VerifyManagedWsBackendAvoidsClientSnapshots()
        {
            var backend = Read("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            var broadcastText = SourceMethod(backend, "public void BroadcastText(string json)");
            var broadcastBinary = SourceMethod(backend, "public void BroadcastBinary(byte[] data)");
            var stats = SourceMethod(backend, "public TransportStatsSnapshot GetStatsSnapshot()");

            Check(!broadcastText.Contains("_clients.ToArray()", StringComparison.Ordinal)
                  && !broadcastBinary.Contains("_clients.ToArray()", StringComparison.Ordinal)
                  && broadcastText.Contains("foreach (var (id, conn) in _clients)", StringComparison.Ordinal)
                  && broadcastBinary.Contains("foreach (var (id, conn) in _clients)", StringComparison.Ordinal),
                "164-48A-1: control broadcast paths iterate clients without array snapshots");
            Check(!stats.Contains("_clients.ToArray()", StringComparison.Ordinal)
                  && stats.Contains("foreach (var kv in _clients)", StringComparison.Ordinal),
                "164-48A-2: transport stats iterate clients without array snapshots");
        }

        private static void VerifyHandshakeAllocationOptimizations()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsHandshakeHandler.cs");
            var select = SourceMethod(source, "SelectSubprotocol(IReadOnlyDictionary<string, string> headers)");
            var accept = SourceMethod(source, "ComputeAcceptKey(string wsKey)");
            var writeBytes = SourceMethod(source, "WriteResponse(Stream stream, byte[] bytes)");

            Check(source.Contains("private static readonly byte[] ForbiddenResponse", StringComparison.Ordinal)
                  && source.Contains("private static readonly byte[] UnauthorizedResponse", StringComparison.Ordinal)
                  && source.Contains("WriteResponse(stream, ForbiddenResponse)", StringComparison.Ordinal)
                  && writeBytes.Contains("stream.Write(bytes, 0, bytes.Length)", StringComparison.Ordinal),
                "164-48B-1: fixed rejected-handshake responses are pre-encoded");
            Check(source.Contains("ThreadLocal<SHA1> AcceptKeySha1", StringComparison.Ordinal)
                  && accept.Contains("AcceptKeySha1.Value", StringComparison.Ordinal)
                  && !accept.Contains("SHA1.Create()", StringComparison.Ordinal),
                "164-48B-2: WebSocket accept-key hashing reuses a per-thread SHA1 instance");
            Check(!select.Contains(".Split(", StringComparison.Ordinal)
                  && !select.Contains(".Select(", StringComparison.Ordinal)
                  && select.Contains("IndexOf(',', start)", StringComparison.Ordinal)
                  && select.Contains("string.Compare(clientProtocols, start, accepted", StringComparison.Ordinal),
                "164-48B-3: subprotocol negotiation scans without Split/LINQ allocations");
        }

        private static void VerifyTokenAndValidationWaitOptimizations()
        {
            var options = Read("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWebSocketOptions.cs");
            var phase50 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase50Validation.cs");
            var sendRawHandshake = SourceMethod(phase50, "SendRawHandshake(string request, int port)");

            Check(options.Contains("private byte[] _sharedTokenBytes", StringComparison.Ordinal)
                  && options.Contains("_sharedTokenBytes = Encoding.UTF8.GetBytes(_sharedToken);", StringComparison.Ordinal)
                  && options.Contains("return FixedTimeEqualsUtf8(_sharedTokenBytes, providedToken);", StringComparison.Ordinal),
                "164-48C-1: shared-token expected UTF8 bytes are cached by the setter");
            Check(!sendRawHandshake.Contains("Thread.Sleep(50)", StringComparison.Ordinal)
                  && sendRawHandshake.Contains("ConnectWithRetry(client, \"127.0.0.1\", port, 2000)", StringComparison.Ordinal),
                "164-48C-2: Phase50 raw handshake relies on connect retry instead of fixed pre-sleep");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-48\"", StringComparison.Ordinal), "164-48D-1: validation registry exposes Phase164-48");
            Check(project.Contains("Phase164_48Validation.cs", StringComparison.Ordinal), "164-48D-2: runtime validation project compiles Phase164-48");
        }

        private static string SourceMethod(string source, string signature)
            => PhaseValidationSourceHelpers.SourceMethod(source, signature);

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
