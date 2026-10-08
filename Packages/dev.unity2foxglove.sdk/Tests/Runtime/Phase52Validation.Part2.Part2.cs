// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validate Phase 52 Unity-native WSS/TLS transport extension,
// shared stream-based WebSocket core, token gating, certificate
// distribution, and secure/plain transport regressions.

using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Reflection;
using System.Threading;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase52Validation.
    /// </summary>
    public static partial class Phase52Validation
    {
        private static string SendRawHandshake(
            int port,
            string target,
            string origin,
            bool useTls,
            string serverName,
            string websocketVersion = "13")
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            client.ReceiveTimeout = TestTimeoutMs;
            client.SendTimeout = TestTimeoutMs;

            if (useTls)
            {
                using var ssl = CreateClientSslStream(client);
                ssl.AuthenticateAsClient(serverName ?? "localhost");
                ssl.ReadTimeout = TestTimeoutMs;
                ssl.WriteTimeout = TestTimeoutMs;
                WriteHandshake(ssl, target, origin, websocketVersion);
                return ReadHttpResponse(ssl);
            }

            using var stream = client.GetStream();
            stream.ReadTimeout = TestTimeoutMs;
            stream.WriteTimeout = TestTimeoutMs;
            WriteHandshake(stream, target, origin, websocketVersion);
            return ReadHttpResponse(stream);
        }

        private static string SendHttpGet(int port, string target)
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            using var stream = client.GetStream();
            stream.ReadTimeout = TestTimeoutMs;
            stream.WriteTimeout = TestTimeoutMs;
            var request = Encoding.ASCII.GetBytes(
                $"GET {target} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n");
            stream.Write(request, 0, request.Length);
            stream.Flush();
            return ReadToEnd(stream);
        }

        private static System.Net.Security.SslStream CreateClientSslStream(TcpClient client)
        {
            return new System.Net.Security.SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (_, _, _, _) => true);
        }

        private static void WriteHandshake(Stream stream, string target, string origin, string websocketVersion = "13")
        {
            var sb = new StringBuilder();
            sb.Append($"GET {target} HTTP/1.1\r\n");
            sb.Append("Host: 127.0.0.1\r\n");
            sb.Append("Upgrade: websocket\r\n");
            sb.Append("Connection: Upgrade\r\n");
            sb.Append("Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n");
            if (websocketVersion != null)
                sb.Append($"Sec-WebSocket-Version: {websocketVersion}\r\n");
            sb.Append("Sec-WebSocket-Protocol: foxglove.sdk.v1\r\n");
            if (!string.IsNullOrEmpty(origin))
                sb.Append($"Origin: {origin}\r\n");
            sb.Append("\r\n");

            var bytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string ReadHttpResponse(Stream stream)
        {
            var bytes = new MemoryStream();
            var tail = new byte[4];
            while (true)
            {
                var value = stream.ReadByte();
                if (value < 0)
                    break;

                bytes.WriteByte((byte)value);
                tail[0] = tail[1];
                tail[1] = tail[2];
                tail[2] = tail[3];
                tail[3] = (byte)value;
                if (tail[0] == '\r' && tail[1] == '\n' && tail[2] == '\r' && tail[3] == '\n')
                    break;
            }

            return GetString(bytes, Encoding.ASCII);
        }

        private static string ReadToEnd(Stream stream)
        {
            var buffer = new byte[1024];
            using var output = new MemoryStream();
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;
                output.Write(buffer, 0, read);
            }

            return GetString(output, Encoding.UTF8);
        }

        private static string GetString(MemoryStream stream, Encoding encoding)
        {
            if (!stream.TryGetBuffer(out var buffer))
                return encoding.GetString(stream.ToArray());

            return encoding.GetString(buffer.Array, buffer.Offset, buffer.Count);
        }

        private static string ReadServerTextFrame(Stream stream)
        {
            var first = stream.ReadByte();
            var second = stream.ReadByte();
            if (first < 0 || second < 0)
                return string.Empty;

            var length = second & 0x7f;
            if (length == 126)
            {
                var ext = ReadExact(stream, 2);
                length = (ext[0] << 8) | ext[1];
            }
            else if (length == 127)
            {
                var ext = ReadExact(stream, 8);
                var longLength = 0L;
                foreach (var b in ext)
                    longLength = (longLength << 8) | b;
                if (longLength > int.MaxValue)
                    throw new InvalidDataException("Test frame is too large.");
                length = (int)longLength;
            }

            var payload = ReadExact(stream, length);
            return Encoding.UTF8.GetString(payload);
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }

            return buffer;
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void ConnectAndCloseTcp(int port)
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
        }

        private static void WaitUntil(Func<bool> predicate, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (predicate())
                    return;
                Thread.Sleep(10);
            }
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                _passCount++;
                Console.WriteLine($"[PASS] {label}");
                return;
            }

            throw new Exception($"[FAIL] {label}");
        }

        private static void CheckThrows<TException>(Action action, string label)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                Check(true, label);
                return;
            }

            throw new Exception($"[FAIL] {label}");
        }

        private static WsFrame ReadFrameFromBytes(byte[] frameBytes)
        {
            using var stream = new MemoryStream(frameBytes);
            return WsFrameCodec.TryReadFrame(stream, out var frame) ? frame : null;
        }

        private static byte[] BuildClientFrame(byte opcode, byte[] payload, bool masked, bool fin, byte reservedBits = 0)
        {
            payload ??= Array.Empty<byte>();
            using var ms = new MemoryStream();
            ms.WriteByte((byte)((fin ? 0x80 : 0x00) | (reservedBits & 0x70) | opcode));

            var maskFlag = masked ? 0x80 : 0x00;
            if (payload.Length <= 125)
            {
                ms.WriteByte((byte)(maskFlag | payload.Length));
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                ms.WriteByte((byte)(maskFlag | 126));
                ms.WriteByte((byte)(payload.Length >> 8));
                ms.WriteByte((byte)payload.Length);
            }
            else
            {
                throw new InvalidDataException("Test frame payload is too large.");
            }

            if (masked)
            {
                var mask = new byte[] { 0x12, 0x34, 0x56, 0x78 };
                ms.Write(mask, 0, mask.Length);
                for (var i = 0; i < payload.Length; i++)
                    ms.WriteByte((byte)(payload[i] ^ mask[i % mask.Length]));
            }
            else
            {
                ms.Write(payload, 0, payload.Length);
            }

            return ms.ToArray();
        }

        private static byte[] BuildOversizedMaskedFrameHeader()
        {
            var length = (ulong)WsFrameCodec.MaxPayloadBytes + 1UL;
            return new byte[]
            {
                (byte)(0x80 | WsOpcode.Binary),
                (byte)(0x80 | 127),
                (byte)(length >> 56),
                (byte)(length >> 48),
                (byte)(length >> 40),
                (byte)(length >> 32),
                (byte)(length >> 24),
                (byte)(length >> 16),
                (byte)(length >> 8),
                (byte)length,
                0x12,
                0x34,
                0x56,
                0x78
            };
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ReadManagerSource()
        {
            var parts = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Certificate.cs"
            };
            var source = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                source.AppendLine(ReadRepoText(part));
            }

            return source.ToString();
        }

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }

        private sealed class Phase52OriginTransport : IFoxgloveTransport, IOriginGuardedFoxgloveTransport
        {
            public string AddedOrigin { get; private set; }
            public bool ClearCalled { get; private set; }
            public bool IsRunning => false;
            public System.Collections.Generic.IReadOnlyCollection<string> AllowedOrigins =>
                Array.Empty<string>();
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            /// <summary>
            /// Validation method for Start.
            /// </summary>
            /// <param name="host">Host address used by the validation client or listener.</param>
            /// <param name="port">TCP port used by the validation client or listener.</param>
            public void Start(string host, int port) { }
            /// <summary>
            /// Validation method for Stop.
            /// </summary>
            public void Stop() { }
            /// <summary>
            /// Validation method for BroadcastText.
            /// </summary>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void BroadcastText(string json) { }
            /// <summary>
            /// Validation method for BroadcastBinary.
            /// </summary>
            /// <param name="data">Binary payload used by the transport stub.</param>
            public void BroadcastBinary(byte[] data) { }
            /// <summary>
            /// Validation method for SendText.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void SendText(uint clientId, string json) { }
            /// <summary>
            /// Validation method for SendBinary.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="data">Binary payload used by the transport stub.</param>
            public void SendBinary(uint clientId, byte[] data) { }
            /// <summary>
            /// Validation method for AddAllowedOrigin.
            /// </summary>
            /// <param name="origin">Reference point for the seek operation.</param>
            public void AddAllowedOrigin(string origin) => AddedOrigin = origin;
            /// <summary>
            /// Validation method for ClearAllowedOrigins.
            /// </summary>
            public void ClearAllowedOrigins() => ClearCalled = true;
            /// <summary>
            /// Validation method for Dispose.
            /// </summary>
            public void Dispose() { }
        }
    }
}
