// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 51 regression coverage for maintainability, lifecycle,
// diagnostics, and optimization follow-up fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates Phase 51 maintainability and optimization closure.
    /// </summary>
    public static partial class Phase51Validation
    {
        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repo root.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool ThrowsAny(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch
            {
                return true;
            }
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

        private sealed class Phase51FakeTransport : IFoxgloveTransport, IPrioritizedFoxgloveTransport
        {
            public readonly List<(uint ClientId, string Text)> SentText = new List<(uint, string)>();
            public readonly List<(uint ClientId, byte[] Data)> ControlBinary = new List<(uint, byte[])>();
            public readonly List<(uint ClientId, byte[] Data)> DataBinary = new List<(uint, byte[])>();
            public readonly List<byte[]> ControlBroadcastBinary = new List<byte[]>();
            public readonly List<byte[]> DataBroadcastBinary = new List<byte[]>();

            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) => SentText.Add((uint.MaxValue, json));
            public void BroadcastBinary(byte[] data) => ControlBroadcastBinary.Add(data);
            public void BroadcastDataBinary(byte[] data) => DataBroadcastBinary.Add(data);
            public void SendText(uint clientId, string json) => SentText.Add((clientId, json));
            public void SendBinary(uint clientId, byte[] data) => ControlBinary.Add((clientId, data));
            public void SendDataBinary(uint clientId, byte[] data) => DataBinary.Add((clientId, data));
            public void Dispose() => Stop();
            public void RaiseText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void RaiseBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
            public void RaiseConnected(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void RaiseDisconnected(uint clientId) => OnClientDisconnected?.Invoke(clientId);
        }

        private sealed class Phase51ThrowingSendTransport : IFoxgloveTransport
        {
            public bool ThrowOnSendText;
            public int SendTextThrowCount;
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json)
            {
                if (!ThrowOnSendText)
                    return;

                SendTextThrowCount++;
                throw new InvalidOperationException("phase51 send text failure");
            }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() => Stop();
            public void RaiseText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void RaiseBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
            public void RaiseConnected(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void RaiseDisconnected(uint clientId) => OnClientDisconnected?.Invoke(clientId);
        }

        private sealed class ThrowingStartTransport : IFoxgloveTransport
        {
            private int _handlerBalance;
            public bool ThrowOnStart = true;
            public int HandlerBalance => _handlerBalance;
            public bool IsRunning { get; private set; }

            private event Action<uint> ClientConnected;
            private event Action<uint> ClientDisconnected;
            private event Action<uint, string> TextReceived;
            private event Action<uint, byte[]> BinaryReceived;

            public event Action<uint> OnClientConnected
            {
                add { _handlerBalance++; ClientConnected += value; }
                remove { _handlerBalance--; ClientConnected -= value; }
            }

            public event Action<uint> OnClientDisconnected
            {
                add { _handlerBalance++; ClientDisconnected += value; }
                remove { _handlerBalance--; ClientDisconnected -= value; }
            }

            public event Action<uint, string> OnTextReceived
            {
                add { _handlerBalance++; TextReceived += value; }
                remove { _handlerBalance--; TextReceived -= value; }
            }

            public event Action<uint, byte[]> OnBinaryReceived
            {
                add { _handlerBalance++; BinaryReceived += value; }
                remove { _handlerBalance--; BinaryReceived -= value; }
            }

            public void Start(string host, int port)
            {
                if (ThrowOnStart)
                    throw new InvalidOperationException("phase51 start failure");
                IsRunning = true;
            }

            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() => Stop();
        }
    }
}
