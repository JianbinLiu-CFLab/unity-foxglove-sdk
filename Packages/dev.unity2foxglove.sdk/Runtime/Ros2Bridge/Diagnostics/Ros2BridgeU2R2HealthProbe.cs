// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge/Diagnostics
// Purpose: Live loopback U2R2 health probe for the ROS2 Bridge sidecar.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Loopback U2R2 health probe used by the ROS2 Bridge Inspector diagnostics.</summary>
    public sealed class Ros2BridgeU2R2HealthProbe : IRos2BridgeHealthProbe
    {
        public Ros2BridgeProbeResult Ping(string host, int port, int timeoutMs)
            => Ping(host, port, timeoutMs, CancellationToken.None);

        public Ros2BridgeProbeResult Ping(string host, int port, int timeoutMs, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Ros2BridgeTcpClient.ValidateLoopbackHost(host);
                if (port <= 0 || port > 65535)
                    throw new ArgumentOutOfRangeException(nameof(port), "ROS2 Bridge port must be in 1..65535.");

                using var client = new TcpClient();
                var connect = client.ConnectAsync(host, port);
                if (!WaitOrCancel(connect, Math.Max(1, timeoutMs), cancellationToken))
                    throw new TimeoutException("Timed out connecting to ROS2 Bridge sidecar.");
                connect.GetAwaiter().GetResult();

                cancellationToken.ThrowIfCancellationRequested();
                client.NoDelay = true;
                client.ReceiveTimeout = Math.Max(1, timeoutMs);
                client.SendTimeout = Math.Max(1, timeoutMs);

                var requestId = "phase97-" + Guid.NewGuid().ToString("N");
                var request = Ros2BridgeU2R2HealthCodec.WriteHealthPing(requestId);
                var stream = client.GetStream();
                cancellationToken.ThrowIfCancellationRequested();
                stream.Write(request, 0, request.Length);
                stream.Flush();

                var response = ReadU2R2Frame(stream, cancellationToken);
                var pong = Ros2BridgeU2R2HealthCodec.ParseHealthPong(response, requestId);
                stopwatch.Stop();

                if (pong.Status == "error")
                {
                    return new Ros2BridgeProbeResult(
                        false,
                        string.IsNullOrWhiteSpace(pong.Message)
                            ? "Sidecar returned health error: " + pong.ErrorCode
                            : pong.Message,
                        durationMs: stopwatch.ElapsedMilliseconds);
                }

                return new Ros2BridgeProbeResult(
                    true,
                    "Sidecar health pong received.",
                    pong.SidecarName,
                    pong.SidecarVersion,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new Ros2BridgeProbeResult(false, ex.Message, durationMs: stopwatch.ElapsedMilliseconds);
            }
        }

        private static bool WaitOrCancel(System.Threading.Tasks.Task task, int timeoutMs, CancellationToken cancellationToken)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < timeoutMs)
            {
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();
                if (task.Wait(50))
                    return true;
            }

            return task.IsCompleted;
        }

        private static byte[] ReadU2R2Frame(Stream stream, CancellationToken cancellationToken)
        {
            var fixedHeader = ReadExact(stream, 16, cancellationToken);
            if (fixedHeader[0] != 'U' || fixedHeader[1] != '2' || fixedHeader[2] != 'R' || fixedHeader[3] != '2')
                throw new FormatException("U2R2 response magic is invalid.");
            if (ReadUInt16LE(fixedHeader, 4) != 1)
                throw new FormatException("U2R2 response version is unsupported.");
            if (ReadUInt16LE(fixedHeader, 6) != 0)
                throw new FormatException("U2R2 response flags must be zero.");

            var headerLength = ReadUInt32LE(fixedHeader, 8);
            var payloadLength = ReadUInt32LE(fixedHeader, 12);
            if (headerLength == 0 || headerLength > Ros2BridgeFrameWriter.MaxHeaderBytes)
                throw new FormatException("U2R2 response JSON header length is invalid.");
            if (payloadLength > Ros2BridgeFrameWriter.MaxPayloadBytes)
                throw new FormatException("U2R2 response payload length exceeds maximum.");

            var header = ReadExact(stream, checked((int)headerLength), cancellationToken);
            var payload = payloadLength == 0
                ? Array.Empty<byte>()
                : ReadExact(stream, checked((int)payloadLength), cancellationToken);

            var frame = new byte[16 + header.Length + payload.Length];
            Buffer.BlockCopy(fixedHeader, 0, frame, 0, fixedHeader.Length);
            Buffer.BlockCopy(header, 0, frame, fixedHeader.Length, header.Length);
            if (payload.Length > 0)
                Buffer.BlockCopy(payload, 0, frame, fixedHeader.Length + header.Length, payload.Length);
            return frame;
        }

        private static byte[] ReadExact(Stream stream, int count, CancellationToken cancellationToken)
        {
            var bytes = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(bytes, offset, count - offset);
                if (read <= 0)
                    throw new IOException("ROS2 Bridge sidecar closed the connection.");
                offset += read;
            }
            return bytes;
        }

        private static uint ReadUInt32LE(byte[] data, int offset)
            => (uint)(data[offset]
                      | (data[offset + 1] << 8)
                      | (data[offset + 2] << 16)
                      | (data[offset + 3] << 24));

        private static ushort ReadUInt16LE(byte[] data, int offset)
            => (ushort)(data[offset] | (data[offset + 1] << 8));
    }
}
