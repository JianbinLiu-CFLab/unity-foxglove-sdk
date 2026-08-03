// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Loopback TCP sink for the experimental Unity-to-ROS2 bridge.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.IO;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    /// <summary>Sends U2R2 bridge frames to a loopback-only ROS 2 sidecar.</summary>
    public sealed class Ros2BridgeTcpClient :
        IRos2BridgeSink,
        IRos2BridgePublisherPreparationTransport,
        IRos2BridgeRawWireSink,
        IRos2BridgeV2SessionTransport,
        IRos2BridgeSessionTransport
    {
        private readonly object _gate = new object();
        private readonly object _ioGate = new object();
        private TcpClient _client;
        private int _sendTimeoutMs;
        private U2R2Dialect _socketDialect;

        public bool IsConnected
        {
            get
            {
                TcpClient client;
                lock (_gate)
                    client = _client;
                if (client == null || !client.Connected)
                    return false;

                try
                {
                    var socket = client.Client;
                    return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }
        }

        public static void ValidateLoopbackHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("ROS 2 bridge host must be non-empty.", nameof(host));
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return;
            if (!IPAddress.TryParse(host, out var address)
                || address.AddressFamily != AddressFamily.InterNetwork
                || address.GetAddressBytes()[0] != 127)
            {
                throw new ArgumentException(
                    "ROS 2 bridge only accepts IPv4 127/8 loopback hosts.",
                    nameof(host));
            }
        }

        public void Connect(string host, int port, int timeoutMs)
        {
            ValidateLoopbackHost(host);
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "ROS 2 bridge port must be in 1..65535.");
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "ROS 2 bridge connect timeout must be positive.");

            lock (_ioGate)
            {
                DisposeClient();
                var client = new TcpClient();
                try
                {
                    lock (_gate)
                        _client = client;
                    var connectHost = string.Equals(
                        host,
                        "localhost",
                        StringComparison.OrdinalIgnoreCase)
                        ? "127.0.0.1"
                        : host;
                    var task = client.ConnectAsync(connectHost, port);
                    if (!task.Wait(timeoutMs))
                        throw new TimeoutException("Timed out connecting to ROS 2 bridge sidecar.");

                    client.NoDelay = true;
                    client.Client.SendTimeout = timeoutMs;
                    lock (_gate)
                        _sendTimeoutMs = timeoutMs;
                    lock (_gate)
                        _socketDialect = U2R2Dialect.None;
                    client = null;
                }
                catch
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_client, client))
                            _client = null;
                    }
                    client?.Dispose();
                    throw;
                }
            }
        }

        public void Send(Ros2BridgeFrame frame, int timeoutMs)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            lock (_ioGate)
            {
                var client = GetConnectedClient(timeoutMs);
                LatchDialect(U2R2Dialect.V1);
                var stream = client.GetStream();
                Ros2BridgeFrameWriter.Write(frame, stream);
                stream.Flush();
            }
        }

        void IRos2BridgeRawWireSink.SendWire(
            ReadOnlyMemory<byte> wireBytes,
            int timeoutMs)
        {
            if (wireBytes.IsEmpty)
            {
                throw new ArgumentException(
                    "ROS 2 bridge wire frame is empty.",
                    nameof(wireBytes));
            }
            if (!MemoryMarshal.TryGetArray(
                    wireBytes,
                    out ArraySegment<byte> segment)
                || segment.Array == null)
            {
                throw new InvalidOperationException(
                    "ROS 2 bridge owned wire memory is not array-backed.");
            }

            lock (_ioGate)
            {
                var client = GetConnectedClient(timeoutMs);
                LatchDialect(U2R2Dialect.V1);
                var stream = client.GetStream();
                stream.Write(
                    segment.Array,
                    segment.Offset,
                    segment.Count);
                stream.Flush();
            }
        }

        public byte[] ExchangePublisherPreparation(byte[] request, int timeoutMs)
        {
            if (request == null || request.Length == 0)
                throw new ArgumentException("Publisher preparation request is empty.", nameof(request));
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            lock (_ioGate)
            {
                var client = GetConnectedClient(timeoutMs);
                LatchDialect(U2R2Dialect.V1);
                var deadline = new ExchangeDeadline(timeoutMs);
                deadline.BeginPhase((ulong)timeoutMs);
                WriteMemory(client.Client, request, deadline);
                deadline.BeginPhase((ulong)timeoutMs);
                var stream = client.GetStream();
                try
                {
                    return Ros2BridgePublisherPreparationCodec.ReadFrame(
                        stream,
                        () => deadline.RemainingMilliseconds(
                            includePartialStall: true),
                        () => deadline.MarkReadProgress(
                            (ulong)timeoutMs));
                }
                catch (IOException exception)
                    when (IsTimeout(exception))
                {
                    throw Timeout(
                        "Timed out reading a U2R2 v1 publisher-preparation response.",
                        exception);
                }
            }
        }

        byte[] IRos2BridgeV2SessionTransport.ExchangeV2(
            ReadOnlyMemory<byte> request,
            U2R2ProtocolLimits limits,
            int timeoutMs)
        {
            if (request.IsEmpty)
                throw new ArgumentException(
                    "U2R2 v2 request is empty.",
                    nameof(request));
            if (limits == null)
                throw new ArgumentNullException(nameof(limits));

            lock (_ioGate)
            {
                var client = GetConnectedClient(timeoutMs);
                LatchDialect(U2R2Dialect.V2);
                var deadline = new ExchangeDeadline(timeoutMs);
                deadline.BeginPhase(limits.WriteTimeoutMs);
                WriteMemory(client.Client, request, deadline);
                deadline.BeginPhase(limits.ReadTimeoutMs);
                return ReadV2FrameCore(
                    client.GetStream(),
                    limits,
                    deadline,
                    requireEmptyPayload: true);
            }
        }

        void IRos2BridgeSessionTransport.BeginV2(
            U2R2ProtocolLimits limits,
            int timeoutMs)
        {
            if (limits == null)
                throw new ArgumentNullException(nameof(limits));
            GetConnectedClient(timeoutMs);
            LatchDialect(U2R2Dialect.V2);
        }

        void IRos2BridgeSessionTransport.WriteV2(
            ReadOnlyMemory<byte> wireBytes,
            U2R2ProtocolLimits limits,
            int timeoutMs)
        {
            if (wireBytes.IsEmpty)
            {
                throw new ArgumentException(
                    "A U2R2 v2 wire frame is required.",
                    nameof(wireBytes));
            }
            if (limits == null)
                throw new ArgumentNullException(nameof(limits));

            var client = GetConnectedClient(timeoutMs);
            LatchDialect(U2R2Dialect.V2);
            var deadline = new ExchangeDeadline(timeoutMs);
            deadline.BeginPhase(limits.WriteTimeoutMs);
            WriteMemory(client.Client, wireBytes, deadline);
        }

        byte[] IRos2BridgeSessionTransport.ReadV2(
            U2R2ProtocolLimits limits,
            int timeoutMs)
        {
            if (limits == null)
                throw new ArgumentNullException(nameof(limits));
            var client = GetConnectedClient(timeoutMs);
            LatchDialect(U2R2Dialect.V2);
            var deadline = new ExchangeDeadline(timeoutMs);
            deadline.BeginPhase(limits.ReadTimeoutMs);
            return ReadV2FrameCore(
                client.GetStream(),
                limits,
                deadline,
                requireEmptyPayload: false);
        }

        void IRos2BridgeSessionTransport.Close()
            => Disconnect();

        public void Disconnect()
        {
            TcpClient client;
            lock (_gate)
                client = _client;
            if (client == null)
                return;

            try
            {
                try
                {
                    client.Client.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException)
                {
                    // A failed or peer-closed socket is already waking I/O.
                }
                client.Client.Close();
            }
            catch (ObjectDisposedException)
            {
                // Repeated wake is idempotent; final wrapper disposal is separate.
            }
        }

        public void Dispose() => DisposeClient();

        private TcpClient GetConnectedClient(int timeoutMs)
        {
            TcpClient client;
            lock (_gate)
                client = _client;
            if (client == null || !client.Connected)
            {
                throw new InvalidOperationException(
                    "ROS 2 bridge TCP client is not connected.");
            }
            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeoutMs),
                    "ROS 2 bridge send timeout must be positive.");
            }

            var socket = client.Client;
            if (_sendTimeoutMs != timeoutMs)
            {
                socket.SendTimeout = timeoutMs;
                lock (_gate)
                    _sendTimeoutMs = timeoutMs;
            }
            return client;
        }

        private static void WriteMemory(
            Socket socket,
            ReadOnlyMemory<byte> memory,
            ExchangeDeadline deadline)
        {
            if (!MemoryMarshal.TryGetArray(
                    memory,
                    out ArraySegment<byte> segment)
                || segment.Array == null)
            {
                throw new InvalidOperationException(
                    "ROS 2 bridge owned wire memory is not array-backed.");
            }
            var offset = segment.Offset;
            var remaining = segment.Count;
            while (remaining > 0)
            {
                socket.SendTimeout = deadline.RemainingMilliseconds(
                    includePartialStall: false);
                int sent;
                try
                {
                    sent = socket.Send(
                        segment.Array,
                        offset,
                        remaining,
                        SocketFlags.None);
                }
                catch (SocketException exception)
                    when (exception.SocketErrorCode
                          == SocketError.TimedOut)
                {
                    throw Timeout(
                        "Timed out writing a U2R2 request.",
                        exception);
                }
                if (sent <= 0)
                {
                    throw new U2R2ProtocolException(
                        "peer_closed",
                        "The peer closed during a U2R2 request.",
                        terminal: true);
                }
                offset += sent;
                remaining -= sent;
            }
        }

        private static byte[] ReadV2Frame(
            Stream stream,
            U2R2ProtocolLimits limits)
        {
            var deadline = new ExchangeDeadline(
                LimitToInt(limits.ReadTimeoutMs));
            deadline.BeginPhase(limits.ReadTimeoutMs);
            return ReadV2FrameCore(
                stream,
                limits,
                deadline,
                requireEmptyPayload: false);
        }

        private static byte[] ReadV2FrameCore(
            Stream stream,
            U2R2ProtocolLimits limits,
            ExchangeDeadline deadline,
            bool requireEmptyPayload)
        {
            var fixedHeader = new byte[
                checked((int)limits.FixedFrameBytes)];
            ReadExactInto(
                stream,
                fixedHeader,
                offset: 0,
                count: fixedHeader.Length,
                deadline,
                limits.PartialFrameTimeoutMs,
                allowCleanEof: true);
            if (fixedHeader[0] != (byte)'U'
                || fixedHeader[1] != (byte)'2'
                || fixedHeader[2] != (byte)'R'
                || fixedHeader[3] != (byte)'2'
                || fixedHeader[4]
                != U2R2ProtocolCodec.EnvelopeVersion
                || fixedHeader[5] != 0
                || fixedHeader[6] != 0
                || fixedHeader[7] != 0)
            {
                throw new U2R2ProtocolException(
                    "invalid_frame",
                    "The U2R2 v2 response fixed header is invalid.");
            }
            var headerLength = ReadUInt32LE(fixedHeader, 8);
            var payloadLength = ReadUInt32LE(fixedHeader, 12);
            if (requireEmptyPayload && payloadLength != 0)
            {
                throw new U2R2ProtocolException(
                    "invalid_frame",
                    "A correlated U2R2 v2 response cannot carry a payload.",
                    terminal: true);
            }
            U2R2FrameSize size;
            try
            {
                size = U2R2FrameSize.Create(
                    limits,
                    headerLength,
                    payloadLength);
            }
            catch (U2R2ProtocolException exception)
                when (string.Equals(
                    exception.ErrorCode,
                    "capacity_exceeded",
                    StringComparison.Ordinal))
            {
                throw new U2R2ProtocolException(
                    "invalid_frame",
                    "The U2R2 v2 response declares lengths outside the negotiated frame limits.",
                    terminal: true,
                    innerException: exception);
            }
            var frame = new byte[checked((int)size.TotalBytes)];
            Buffer.BlockCopy(
                fixedHeader,
                0,
                frame,
                0,
                fixedHeader.Length);
            var remaining = checked(
                frame.Length - fixedHeader.Length);
            if (remaining != 0)
            {
                ReadExactInto(
                    stream,
                    frame,
                    fixedHeader.Length,
                    remaining,
                    deadline,
                    limits.PartialFrameTimeoutMs,
                    allowCleanEof: false);
            }
            return frame;
        }

        private static void ReadExactInto(
            Stream stream,
            byte[] bytes,
            int offset,
            int count,
            ExchangeDeadline deadline,
            ulong partialFrameTimeoutMs,
            bool allowCleanEof)
        {
            var end = checked(offset + count);
            while (offset < end)
            {
                var readTimeout = deadline.RemainingMilliseconds(
                    includePartialStall: true);
                if (stream.CanTimeout)
                    stream.ReadTimeout = readTimeout;
                int read;
                try
                {
                    read = stream.Read(
                        bytes,
                        offset,
                        end - offset);
                }
                catch (IOException exception)
                    when (IsTimeout(exception))
                {
                    throw Timeout(
                        "Timed out reading a U2R2 v2 response.",
                        exception);
                }
                if (read == 0)
                {
                    if (allowCleanEof && offset == 0)
                    {
                        throw new Ros2BridgeV2IncompatibilityException(
                            "The peer closed before returning any U2R2 v2 handshake bytes.");
                    }
                    throw new EndOfStreamException(
                        "The peer closed during a U2R2 v2 frame.");
                }
                offset += read;
                deadline.MarkReadProgress(partialFrameTimeoutMs);
            }
        }

        private void LatchDialect(U2R2Dialect requested)
        {
            lock (_gate)
            {
                if (_socketDialect == U2R2Dialect.None)
                {
                    _socketDialect = requested;
                    return;
                }
                if (_socketDialect != requested)
                {
                    throw new U2R2ProtocolException(
                        "dialect_downgrade",
                        "A ROS 2 Bridge TCP connection cannot change wire dialect.",
                        terminal: true);
                }
            }
        }

        private static bool IsTimeout(IOException exception)
            => exception.InnerException is SocketException socket
               && socket.SocketErrorCode == SocketError.TimedOut;

        private static U2R2ProtocolException Timeout(
            string message,
            Exception innerException = null)
            => new U2R2ProtocolException(
                "timeout",
                message,
                terminal: true,
                innerException);

        private static int LimitToInt(ulong milliseconds)
            => checked((int)Math.Min(milliseconds, int.MaxValue));

        private static ulong ReadUInt32LE(
            byte[] buffer,
            int offset)
            => (ulong)buffer[offset]
               | ((ulong)buffer[offset + 1] << 8)
               | ((ulong)buffer[offset + 2] << 16)
               | ((ulong)buffer[offset + 3] << 24);

        private void DisposeClient()
        {
            TcpClient client;
            lock (_gate)
            {
                client = _client;
                _client = null;
                _sendTimeoutMs = 0;
                _socketDialect = U2R2Dialect.None;
            }
            client?.Dispose();
        }

        private sealed class ExchangeDeadline
        {
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private readonly long _overallTimeoutMs;
            private long _phaseDeadlineMs;
            private long _partialDeadlineMs = long.MaxValue;

            internal ExchangeDeadline(int overallTimeoutMs)
            {
                if (overallTimeoutMs <= 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(overallTimeoutMs));
                _overallTimeoutMs = overallTimeoutMs;
            }

            internal void BeginPhase(ulong phaseTimeoutMs)
            {
                _phaseDeadlineMs = checked(
                    _clock.ElapsedMilliseconds
                    + LimitToInt(phaseTimeoutMs));
                _partialDeadlineMs = long.MaxValue;
            }

            internal void MarkReadProgress(ulong partialTimeoutMs)
            {
                _partialDeadlineMs = checked(
                    _clock.ElapsedMilliseconds
                    + LimitToInt(partialTimeoutMs));
            }

            internal int RemainingMilliseconds(
                bool includePartialStall)
            {
                var elapsed = _clock.ElapsedMilliseconds;
                var remaining = Math.Min(
                    _overallTimeoutMs - elapsed,
                    _phaseDeadlineMs - elapsed);
                if (includePartialStall
                    && _partialDeadlineMs != long.MaxValue)
                {
                    remaining = Math.Min(
                        remaining,
                        _partialDeadlineMs - elapsed);
                }
                if (remaining <= 0)
                {
                    throw Timeout(
                        "The U2R2 exchange exceeded its absolute deadline.");
                }
                return checked((int)Math.Min(remaining, int.MaxValue));
            }
        }
    }
}
