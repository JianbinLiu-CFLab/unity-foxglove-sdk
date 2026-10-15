// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 50 regression coverage for critical stability/security fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates Phase 50 P0/P1 stability and security fixes.
    /// </summary>
    public static partial class Phase50Validation
    {
        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static void ConnectWithRetry(TcpClient client, string host, int port, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            SocketException lastError = null;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    client.Connect(host, port);
                    return;
                }
                catch (SocketException ex)
                {
                    lastError = ex;
                    Thread.Sleep(10);
                }
            }

            throw lastError ?? new SocketException();
        }

        private static byte[] BuildClientMessageData(uint channelId, byte[] payload)
        {
            var frame = new byte[5 + (payload?.Length ?? 0)];
            frame[0] = ClientOpcode.MessageData;
            frame[1] = (byte)(channelId & 0xFF);
            frame[2] = (byte)((channelId >> 8) & 0xFF);
            frame[3] = (byte)((channelId >> 16) & 0xFF);
            frame[4] = (byte)((channelId >> 24) & 0xFF);
            if (payload != null)
                Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
            return frame;
        }

        private static byte[] BuildChunkRecordHeader(byte opcode, ulong length)
        {
            var bytes = new byte[9];
            bytes[0] = opcode;
            for (var i = 0; i < 8; i++)
                bytes[1 + i] = (byte)((length >> (8 * i)) & 0xFF);
            return bytes;
        }

        private static string ReadRepoText(string relativePath)
        {
            if (Phase16Validation.FindRepoRoot() == null)
                throw new InvalidOperationException("Could not find repo root.");
            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateProperty(object target, string name, object value)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
                throw new MissingMemberException(target.GetType().FullName, name);
            property.SetValue(target, value);
        }

        private static bool Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is TException)
            {
                return true;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }

        private sealed class FixedClock : IFoxgloveClock
        {
            public FixedClock(ulong nowNs) => NowNs = nowNs;
            public ulong NowNs { get; }
        }

        private sealed class Phase50FakeTransport : IFoxgloveTransport
        {
            public readonly List<string> SentText = new List<string>();
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) => SentText.Add(json);
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) => SentText.Add(json);
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() => Stop();
            public void RaiseConnected(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void RaiseDisconnected(uint clientId) => OnClientDisconnected?.Invoke(clientId);
            public void RaiseText(uint clientId, string text) => OnTextReceived?.Invoke(clientId, text);
            public void RaiseBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
