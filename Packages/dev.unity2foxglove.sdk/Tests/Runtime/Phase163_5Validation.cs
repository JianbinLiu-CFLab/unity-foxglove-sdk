// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-5 review regression checks for transport, queues, TLS/auth, and backpressure.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase163_5Validation
    {
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-5: Transport, Queues, TLS/Auth, and Backpressure Review ===");

            var root = Phase16Validation.FindRepoRoot();
            var backend = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            var queue = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsSendQueue.cs");
            var handshake = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsHandshakeHandler.cs");
            var distributor = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Transport/Security/FoxgloveCertificateDistributor.cs");
            var unitTests = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Unit/Transport/TransportStatsSnapshotTests.cs");
            var phase28 = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase28Validation.cs");
            var registry = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(backend.Contains("private Task _acceptLoopTask;", StringComparison.Ordinal)
                  && backend.Contains("Interlocked.Exchange(ref _stopping, 1);", StringComparison.Ordinal)
                  && backend.Contains("WaitForShutdownTask(acceptLoopTask, StopAcceptLoopWaitMs, \"accept loop\");", StringComparison.Ordinal)
                  && backend.Contains("Task.Run(() => HandleClient(tcpClient, ct))", StringComparison.Ordinal),
                "163-5A: managed WebSocket stop closes the accept window before disconnect snapshots");

            Check(backend.Contains("TryRegisterClient(conn, out clientId, out var stopped)", StringComparison.Ordinal)
                  && backend.Contains("RemoveUnannouncedClient(clientId, conn)", StringComparison.Ordinal)
                  && backend.Contains("private bool IsStopping => Volatile.Read(ref _stopping) != 0;", StringComparison.Ordinal),
                "163-5B: late clients cannot register or fire connect events during Stop");

            Check(backend.Contains("Interlocked.Exchange(ref _nextClientId, 0);", StringComparison.Ordinal)
                  && backend.Contains("foreach (var (id, conn) in _clients.ToArray())", StringComparison.Ordinal)
                  && backend.Contains("long activeDropped = 0;", StringComparison.Ordinal)
                  && backend.Contains("Interlocked.Read(ref _totalDroppedDataFrames) + activeDropped", StringComparison.Ordinal),
                "163-5C: client ids, broadcast iteration, and dropped-frame stats are stable across lifecycle snapshots");

            Check(queue.Contains("frame.SizeBytes <= _maxQueuedBytes - _queuedBytes", StringComparison.Ordinal)
                  && unitTests.Contains("QueueByteCapacityCheckDoesNotOverflowNearIntMax", StringComparison.Ordinal),
                "163-5D: send-queue byte accounting avoids signed integer overflow");

            Check(handshake.Contains("string.Equals(origin, \"null\", StringComparison.OrdinalIgnoreCase)", StringComparison.Ordinal)
                  && phase28.Contains("TestOpaqueFileOriginAllowed", StringComparison.Ordinal),
                "163-5E: local file clients with opaque Origin null are accepted explicitly");

            Check(distributor.Contains("private const int MaxConcurrentClients = 10;", StringComparison.Ordinal)
                  && distributor.Contains("Interlocked.Increment(ref _activeClientHandlers)", StringComparison.Ordinal)
                  && distributor.Contains("Rejected certificate distributor client because active client limit", StringComparison.Ordinal)
                  && distributor.Contains("Interlocked.Decrement(ref _activeClientHandlers)", StringComparison.Ordinal),
                "163-5F: certificate distributor bounds concurrent local HTTP handlers");

            Check(registry.Contains("Ci(\"--phase163-5\", \"Phase 163-5\", Phase163_5Validation.Validate", StringComparison.Ordinal),
                "163-5G: PhaseValidationRegistry wires --phase163-5");

            Console.WriteLine("Phase 163-5: 7 checks passed.");
            Console.WriteLine();
        }

        private static string Read(string root, string relativePath)
            => File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + message);

            Console.WriteLine("[PASS] " + message);
        }
    }
}
