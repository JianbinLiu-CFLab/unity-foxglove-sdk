using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_5Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-5 Tests ---");
            _passed = 0;

            VerifyBroadcastPathsAvoidPerClientEncodingAndSnapshots();
            VerifySendLoopBatchesFlushes();
            VerifyDataQueueClearUsesDataByteCounter();
            VerifyStatsAndMonotonicHotPaths();
            VerifyRegistry();

            Console.WriteLine("Phase 164-5: " + _passed + " checks passed.\n");
        }

        private static void VerifyBroadcastPathsAvoidPerClientEncodingAndSnapshots()
        {
            var backend = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            var connection = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsConnection.cs");
            var broadcastText = SourceMethod(backend, "public void BroadcastText(string json)");
            var broadcastBinary = SourceMethod(backend, "public void BroadcastBinary(byte[] data)");

            Check(connection.Contains("public EnqueueResult SendTextEncoded(byte[] utf8Json, FramePriority priority)", StringComparison.Ordinal)
                  && broadcastText.Contains("Encoding.UTF8.GetBytes(json ?? string.Empty)", StringComparison.Ordinal)
                  && broadcastText.Contains("conn.SendTextEncoded", StringComparison.Ordinal),
                "164-5A-1: text broadcasts encode once and enqueue pre-encoded frames");
            Check(!broadcastText.Contains("_clients.ToArray()", StringComparison.Ordinal)
                  && !broadcastBinary.Contains("_clients.ToArray()", StringComparison.Ordinal),
                "164-5A-2: broadcast text/binary paths avoid client snapshot arrays");
        }

        private static void VerifySendLoopBatchesFlushes()
        {
            var connection = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsConnection.cs");
            var codec = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsFrameCodec.cs");
            var sendLoop = SourceMethod(connection, "private void SendLoop(Action onSendFailed, CancellationToken ct)");
            var writeBatch = SourceMethod(connection, "private void WriteFrameBatch(List<QueuedFrame> frames)");
            var writeFrame = SourceMethod(codec, "internal static void WriteFrame(Stream stream, byte opcode, byte[] payload, bool flush)");

            Check(connection.Contains("private const int MaxSendBatchFrames", StringComparison.Ordinal)
                  && connection.Contains("private readonly List<QueuedFrame> _sendBatch", StringComparison.Ordinal)
                  && sendLoop.Contains("_sendQueue.TryDequeue(out var nextFrame)", StringComparison.Ordinal)
                  && sendLoop.Contains("WriteFrameBatch(_sendBatch)", StringComparison.Ordinal),
                "164-5B-1: send loop drains immediately available frames into a bounded batch");
            Check(writeBatch.Contains("_stream.Flush()", StringComparison.Ordinal)
                  && writeBatch.Contains("WsFrameCodec.WriteFrame(_stream, frame.Opcode, frame.Payload, flush: false)", StringComparison.Ordinal)
                  && writeFrame.Contains("if (flush)", StringComparison.Ordinal),
                "164-5B-2: send batches write frames without per-frame flush and flush once per batch");
        }

        private static void VerifyDataQueueClearUsesDataByteCounter()
        {
            var queue = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsSendQueue.cs");
            var clear = SourceMethod(queue, "public int ClearDataFrames()");

            Check(queue.Contains("private int _dataQueuedBytes;", StringComparison.Ordinal)
                  && clear.Contains("_queuedBytes -= _dataQueuedBytes;", StringComparison.Ordinal)
                  && clear.Contains("_dataQueuedBytes = 0;", StringComparison.Ordinal)
                  && clear.Contains("_dataFrames.Clear();", StringComparison.Ordinal)
                  && !clear.Contains("_dataFrames.Dequeue()", StringComparison.Ordinal),
                "164-5C: clearing data frames subtracts a maintained byte total and clears the queue directly");
        }

        private static void VerifyStatsAndMonotonicHotPaths()
        {
            var backend = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            var connection = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsConnection.cs");
            var stats = SourceMethod(backend, "public TransportStatsSnapshot GetStatsSnapshot()");
            var monotonic = SourceMethod(connection, "private static long MonotonicMilliseconds()");

            Check(!stats.Contains("_clients.ToArray()", StringComparison.Ordinal)
                  && stats.Contains("foreach (var kv in _clients)", StringComparison.Ordinal),
                "164-5D-1: stats snapshot iterates connected clients without array snapshots");
            Check(connection.Contains("StopwatchTicksPerMillisecond", StringComparison.Ordinal)
                  && !monotonic.Contains("Stopwatch.Frequency", StringComparison.Ordinal)
                  && !monotonic.Contains("%", StringComparison.Ordinal),
                "164-5D-2: monotonic millisecond conversion uses cached frequency scaling");
        }

        private static void VerifyRegistry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-5\"", StringComparison.Ordinal), "164-5E-1: validation registry exposes Phase164-5");
            Check(project.Contains("Phase164_5Validation.cs", StringComparison.Ordinal), "164-5E-2: runtime validation project compiles Phase164-5");
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
