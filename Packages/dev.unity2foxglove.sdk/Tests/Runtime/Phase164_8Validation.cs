using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_8Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-8 Tests ---");
            _passed = 0;

            VerifyReplayEngineSnapshotAndHistoryReuse();
            VerifyCursorEndpointHotPostPathAvoidsTransientBuffers();
            VerifyUnsafeTimeFrameBufferReuseIsNotIntroduced();
            VerifyRegistry();

            Console.WriteLine("Phase 164-8: " + _passed + " checks passed.\n");
        }

        private static void VerifyReplayEngineSnapshotAndHistoryReuse()
        {
            var engine = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var snapshot = SourceMethod(engine, "public List<McapMessage> Snapshot");
            var history = SourceMethod(engine, "public List<McapMessage> History(ulong fromTimeNs, ulong toTimeNs, List<McapMessage> result, int maxMessages)");
            var addHistory = SourceMethod(engine, "private static void AddHistoryMessage");

            Check(engine.Contains("_snapshotLatestByChannel", StringComparison.Ordinal)
                  && snapshot.Contains("var latestByChannel = _snapshotLatestByChannel", StringComparison.Ordinal)
                  && snapshot.Contains("latestByChannel.Clear()", StringComparison.Ordinal)
                  && !snapshot.Contains("new Dictionary<ushort, McapMessage>", StringComparison.Ordinal),
                "164-8A-1: replay snapshots reuse the latest-by-channel dictionary");
            Check(history.Contains("var historyHeadIndex = 0", StringComparison.Ordinal)
                  && history.Contains("CompactHistory(result, ref historyHeadIndex)", StringComparison.Ordinal)
                  && addHistory.Contains("historyHeadIndex++", StringComparison.Ordinal)
                  && addHistory.Contains("CompactHistory(result, ref historyHeadIndex)", StringComparison.Ordinal)
                  && !addHistory.Contains("RemoveAt(0)", StringComparison.Ordinal),
                "164-8A-2: capped replay history trims through a head index instead of RemoveAt(0)");
        }

        private static void VerifyCursorEndpointHotPostPathAvoidsTransientBuffers()
        {
            var endpoint = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");
            var readBody = SourceMethod(endpoint, "private string ReadBody");
            var handle = SourceMethod(endpoint, "private void Handle");
            var tryWriteBytes = SourceMethod(endpoint, "private void TryWrite(HttpListenerContext context, int statusCode, byte[] bytes)");

            Check(endpoint.Contains("AcceptedCursorResponseBytes", StringComparison.Ordinal)
                  && handle.Contains("result.Success && string.Equals(result.Message, \"Cursor accepted.\"", StringComparison.Ordinal)
                  && handle.Contains("TryWrite(context, 202, AcceptedCursorResponseBytes)", StringComparison.Ordinal),
                "164-8B-1: common accepted cursor responses use pre-encoded bytes");
            Check(readBody.Contains("ArrayPool<byte>.Shared.Rent(_options.MaxBodyBytes + 1)", StringComparison.Ordinal)
                  && readBody.Contains("ArrayPool<byte>.Shared.Return(buffer)", StringComparison.Ordinal)
                  && readBody.Contains("encoding.GetString(buffer, 0, total)", StringComparison.Ordinal)
                  && !endpoint.Contains("private byte[] _readBodyBuffer", StringComparison.Ordinal)
                  && !readBody.Contains("new MemoryStream()", StringComparison.Ordinal)
                  && !readBody.Contains("memory.ToArray()", StringComparison.Ordinal),
                "164-8B-2: cursor request bodies rent a per-request buffer without MemoryStream.ToArray");
            Check(tryWriteBytes.Contains("bytes ??= Array.Empty<byte>()", StringComparison.Ordinal)
                  && tryWriteBytes.Contains("context.Response.OutputStream.Write(bytes, 0, bytes.Length)", StringComparison.Ordinal),
                "164-8B-3: cursor response writer can send pre-encoded bytes directly");
        }

        private static void VerifyUnsafeTimeFrameBufferReuseIsNotIntroduced()
        {
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var queue = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsSendQueue.cs");
            var replay = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");

            Check(queue.Contains("Payload = payload ?? Array.Empty<byte>()", StringComparison.Ordinal)
                  && session.Contains("BroadcastReplayBinary(byte[] data)", StringComparison.Ordinal)
                  && !replay.Contains("_timeFrameBuffer", StringComparison.Ordinal),
                "164-8C: replay time frames do not reuse a mutable buffer while transport queues retain payload references");
        }

        private static void VerifyRegistry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-8\"", StringComparison.Ordinal), "164-8D-1: validation registry exposes Phase164-8");
            Check(project.Contains("Phase164_8Validation.cs", StringComparison.Ordinal), "164-8D-2: runtime validation project compiles Phase164-8");
        }

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing method: " + signature);

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                throw new InvalidOperationException("Missing method body: " + signature);

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unterminated method: " + signature);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(
                        dir.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk",
                        "Tests",
                        "Runtime",
                        "FoxgloveSdk.Tests.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
