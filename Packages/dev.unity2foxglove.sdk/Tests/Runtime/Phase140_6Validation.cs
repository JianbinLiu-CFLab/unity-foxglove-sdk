// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-6 transport, clock, and backpressure review fixes.

using System;
using System.IO;
using System.Reflection;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for transport, clock, and backpressure defects found in Phase 140-6.
    /// </summary>
    public static class Phase140_6Validation
    {
        private const string ManagedWsBackendPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs";

        private const string ManagedWssBackendPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWssBackend.cs";

        private const string WsSendQueuePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsSendQueue.cs";

        private const string WsFrameCodecPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsFrameCodec.cs";

        private const string CertificateDistributorPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/Security/FoxgloveCertificateDistributor.cs";

        private const string TlsOptionsPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/Security/FoxgloveTlsOptions.cs";

        private static int _passed;

        /// <summary>Runs all Phase 140-6 transport and playback-clock review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-6: Transport, clocks, and backpressure review fixes ===");
            _passed = 0;

            ManagedWebSocketStopAlwaysDisposesCancellationToken();
            ManagedWssBackendOwnsCertificateForStopAndStartFailures();
            SendQueueWaitUsesMonotonicElapsedTime();
            PlaybackClockHandlesInvalidRangesAndUnknownCommands();
            CertificateDistributorHonorsCancellationAndAvoidsSizeCheckRace();
            CertificateDistributorStopDrainsAcceptLoopAndCountsReadLineBytes();
            TlsOptionsDoesNotMarkLoadedPrivateKeysExportable();
            FrameDecodeUsesStackBuffers();
            LiveDataBroadcastAvoidsClientArraySnapshot();
            BackendSnapshotsOriginsAndAggregatesStatsInOnePass();
            ManagedWebSocketSnapshotsDocumentRacesAndReuseFragmentBuffers();

            Console.WriteLine($"Phase 140-6: {_passed} checks passed.");
        }

        private static void ManagedWebSocketStopAlwaysDisposesCancellationToken()
        {
            var source = ReadRepoText(ManagedWsBackendPath);
            var stop = ExtractMethodBody(source, "public virtual void Stop");

            Check(stop.Contains("finally", StringComparison.Ordinal)
                  && stop.Contains("cts?.Dispose();", StringComparison.Ordinal)
                  && !stop.Contains("deferring cancellation token disposal", StringComparison.Ordinal),
                "140-6A-1: ManagedWsBackend.Stop disposes its cancellation token even after forced-close timeout");
        }

        private static void ManagedWssBackendOwnsCertificateForStopAndStartFailures()
        {
            var source = ReadRepoText(ManagedWssBackendPath);
            var start = ExtractMethodBody(source, "public override void Start");
            var stop = ExtractMethodBody(source, "public override void Stop");

            Check(source.Contains("private void DisposeServerCertificate()", StringComparison.Ordinal)
                  && start.Contains("try", StringComparison.Ordinal)
                  && start.Contains("catch", StringComparison.Ordinal)
                  && start.Contains("DisposeServerCertificate();", StringComparison.Ordinal),
                "140-6B-1: ManagedWssBackend releases the loaded certificate when listener start fails");
            Check(stop.Contains("base.Stop();", StringComparison.Ordinal)
                  && stop.Contains("DisposeServerCertificate();", StringComparison.Ordinal),
                "140-6B-2: ManagedWssBackend.Stop releases the active server certificate");
        }

        private static void SendQueueWaitUsesMonotonicElapsedTime()
        {
            var source = ReadRepoText(WsSendQueuePath);
            var wait = ExtractMethodBody(source, "public bool WaitUntilEmpty");

            Check(source.Contains("using System.Diagnostics;", StringComparison.Ordinal)
                  && wait.Contains("Stopwatch.GetTimestamp()", StringComparison.Ordinal)
                  && !wait.Contains("DateTime.UtcNow", StringComparison.Ordinal),
                "140-6C-1: WsSendQueue.WaitUntilEmpty measures timeout with a monotonic clock");
        }

        private static void PlaybackClockHandlesInvalidRangesAndUnknownCommands()
        {
            var reversed = new Unity.FoxgloveSDK.Transport.PlaybackClock();
            reversed.EnableRange(100UL, 50UL);
            reversed.Apply(command: 0, speed: 1f, hasSeek: true, seekTimeNs: 75UL);
            var reversedState = reversed.ToState(didSeek: true, requestId: "phase140-6-range");
            Check(reversed.EndNs == 100UL && reversedState.CurrentTimeNs == 100UL,
                "140-6D-1: PlaybackClock normalizes reversed playback ranges before clamping seeks");

            var unknownCommand = new Unity.FoxgloveSDK.Transport.PlaybackClock();
            unknownCommand.EnableRange(0UL, 100UL);
            unknownCommand.Apply(command: 0, speed: 2f, hasSeek: false, seekTimeNs: 0UL);
            unknownCommand.Apply(command: 2, speed: float.NaN, hasSeek: false, seekTimeNs: 0UL);
            var unknownState = unknownCommand.ToState(didSeek: false, requestId: "phase140-6-unknown");
            Check(Math.Abs(unknownState.Speed - 2f) < 0.0001f,
                "140-6D-2: PlaybackClock ignores invalid speed on unknown playback commands");
        }

        private static void CertificateDistributorHonorsCancellationAndAvoidsSizeCheckRace()
        {
            var source = ReadRepoText(CertificateDistributorPath);
            var handleClient = ExtractMethodBody(source, "private void HandleClient");
            var writeFile = ExtractMethodBody(source, "private static void WriteFile");

            Check(handleClient.Contains("ct.ThrowIfCancellationRequested();", StringComparison.Ordinal)
                  && source.Contains("catch (OperationCanceledException)", StringComparison.Ordinal),
                "140-6E-1: certificate distributor client handling observes cancellation without error logging");
            Check(!writeFile.Contains("new FileInfo(path)", StringComparison.Ordinal)
                  && source.Contains("ReadFileWithinLimit", StringComparison.Ordinal),
                "140-6E-2: certificate distributor enforces response size while reading the file");
        }

        private static void CertificateDistributorStopDrainsAcceptLoopAndCountsReadLineBytes()
        {
            var source = ReadRepoText(CertificateDistributorPath);
            var start = ExtractMethodBody(source, "public void Start");
            var stop = ExtractMethodBody(source, "public void Stop");
            var stopNoLock = ExtractMethodBody(source, "private void StopNoLock");
            var readLine = ExtractMethodBody(source, "private static string ReadLine");

            Check(source.Contains("private Task _acceptLoopTask;", StringComparison.Ordinal)
                  && start.Contains("_acceptLoopTask = Task.Run", StringComparison.Ordinal)
                  && stop.Contains("StopNoLock();", StringComparison.Ordinal)
                  && stopNoLock.Contains("WaitForShutdownTask(_acceptLoopTask", StringComparison.Ordinal)
                  && stopNoLock.Contains("_clientHandlersIdle.Wait", StringComparison.Ordinal),
                "173-065A: certificate distributor Stop drains the accept loop and active handlers");
            Check(readLine.Contains("var next = stream.ReadByte();", StringComparison.Ordinal)
                  && readLine.Contains("bytesRead++;", StringComparison.Ordinal)
                  && readLine.Contains("if (bytesRead > maxBytes)", StringComparison.Ordinal),
                "173-065B: certificate distributor ReadLine counts CR lookahead bytes against the limit");

            var method = typeof(Unity.FoxgloveSDK.Transport.FoxgloveCertificateDistributor).GetMethod(
                "ReadLine",
                BindingFlags.NonPublic | BindingFlags.Static);
            Check(method != null, "173-065C-setup: certificate distributor exposes private ReadLine for boundary validation");
            using var stream = new MemoryStream(new byte[] { (byte)'a', (byte)'\r', (byte)'X' });
            CheckThrows<InvalidDataException>(
                () => method.Invoke(null, new object[] { stream, 2 }),
                "173-065D: certificate distributor ReadLine rejects CR lookahead bytes beyond the limit");
        }

        private static void TlsOptionsDoesNotMarkLoadedPrivateKeysExportable()
        {
            var source = ReadRepoText(TlsOptionsPath);

            Check(!source.Contains("X509KeyStorageFlags.Exportable", StringComparison.Ordinal)
                  && source.Contains("X509KeyStorageFlags.DefaultKeySet", StringComparison.Ordinal),
                "140-6F-1: TLS PFX loading avoids marking private keys exportable");
        }

        private static void FrameDecodeUsesStackBuffers()
        {
            var source = ReadRepoText(WsFrameCodecPath);
            var readFrame = ExtractMethodBody(source, "internal static bool TryReadFrame");

            Check(readFrame.Contains("stackalloc byte[2]", StringComparison.Ordinal)
                  && readFrame.Contains("stackalloc byte[4]", StringComparison.Ordinal)
                  && readFrame.Contains("stackalloc byte[8]", StringComparison.Ordinal)
                  && !readFrame.Contains("new byte[2]", StringComparison.Ordinal)
                  && !readFrame.Contains("new byte[4]", StringComparison.Ordinal)
                  && !readFrame.Contains("new byte[8]", StringComparison.Ordinal),
                "140-6G-1: inbound frame fixed-size headers use stack buffers");
        }

        private static void LiveDataBroadcastAvoidsClientArraySnapshot()
        {
            var source = ReadRepoText(ManagedWsBackendPath);
            var dataBroadcast = ExtractMethodBody(source, "public void BroadcastDataBinary");
            var controlBroadcast = ExtractMethodBody(source, "public void BroadcastBinary");

            Check(!dataBroadcast.Contains("_clients.ToArray()", StringComparison.Ordinal)
                  && !controlBroadcast.Contains("_clients.ToArray()", StringComparison.Ordinal)
                  && dataBroadcast.Contains("foreach", StringComparison.Ordinal)
                  && controlBroadcast.Contains("foreach", StringComparison.Ordinal),
                "140-6G-2: live-data and control broadcasts avoid client array snapshots");
        }

        private static void BackendSnapshotsOriginsAndAggregatesStatsInOnePass()
        {
            var source = ReadRepoText(ManagedWsBackendPath);
            var stats = ExtractMethodBody(source, "public TransportStatsSnapshot GetStatsSnapshot");

            Check(source.Contains("return _allowedOrigins.ToArray();", StringComparison.Ordinal)
                  && stats.Contains("activeDropped += cs.DroppedDataFrames;", StringComparison.Ordinal)
                  && !stats.Contains("foreach (var cs in clientList)", StringComparison.Ordinal),
                "140-6G-3: backend uses compact origin snapshots and one-pass stats aggregation");
        }

        private static void ManagedWebSocketSnapshotsDocumentRacesAndReuseFragmentBuffers()
        {
            var source = ReadRepoText(ManagedWsBackendPath);
            var stats = ExtractMethodBody(source, "public TransportStatsSnapshot GetStatsSnapshot");
            var receive = ExtractMethodBody(source, "private void ReceiveLoop");

            Check(source.Contains("public bool IsRunning => Volatile.Read(ref _listener) != null;", StringComparison.Ordinal),
                "173-025F: ManagedWsBackend IsRunning uses a volatile listener read");
            Check(source.Contains("Drop totals are best-effort under concurrent disconnects", StringComparison.Ordinal)
                  && stats.Contains("Interlocked.Read(ref _totalDroppedDataFrames) + activeDropped", StringComparison.Ordinal),
                "173-025G: ManagedWsBackend documents best-effort dropped-frame snapshots");
            Check(receive.Contains("var fragmentedPayload = new MemoryStream();", StringComparison.Ordinal)
                  && receive.Contains("fragmentedPayload.SetLength(0);", StringComparison.Ordinal)
                  && receive.Contains("var hasFragmentedPayload = false;", StringComparison.Ordinal),
                "173-025H: ReceiveLoop reuses one fragmented message stream");
        }

        private static string ExtractMethodBody(string source, string signaturePrefix)
        {
            var signatureIndex = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
            if (signatureIndex < 0)
                return string.Empty;
            var braceIndex = source.IndexOf('{', signatureIndex);
            if (braceIndex < 0)
                return string.Empty;

            var depth = 0;
            for (var i = braceIndex; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(braceIndex, i - braceIndex + 1);
                }
            }

            return string.Empty;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private static void CheckThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TargetInvocationException ex) when (ex.InnerException is TException)
            {
                _passed++;
                Console.WriteLine("[PASS] " + message);
                return;
            }
            catch (TException)
            {
                _passed++;
                Console.WriteLine("[PASS] " + message);
                return;
            }

            throw new Exception("[FAIL] " + message);
        }
    }
}
