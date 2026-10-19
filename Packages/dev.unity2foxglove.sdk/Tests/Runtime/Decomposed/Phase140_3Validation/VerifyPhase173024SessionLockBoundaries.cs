// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-3 recording/replay controller review fixes.

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for recording/replay controller defects found in Phase 140-3.
    /// </summary>
    public static partial class Phase140_3Validation
    {
        private static void VerifyPhase173_024SessionLockBoundaries()
        {
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var parameters = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Parameters.cs");
            var publish = ExtractMethodBody(session, "public void Publish(uint channelId, byte[] payload, ulong logTimeNs)");
            var copy = ExtractMethodBody(session, "private List<(uint clientId, uint subscriptionId)> CopySubscribersForPublish");
            var replay = ExtractMethodBody(session, "internal void PublishReplay");
            var dispose = ExtractMethodBody(session, "public void Dispose()");
            var broadcast = ExtractMethodBody(parameters, "public void BroadcastParameterValues");
            var advertise = ExtractMethodBody(session, "private string SerializeSingleAdvertise");
            var unadvertise = ExtractMethodBody(session, "private string SerializeSingleUnadvertise");

            Check(session.Contains("[ThreadStatic]", StringComparison.Ordinal)
                  && session.Contains("s_publishSubscriberScratch", StringComparison.Ordinal)
                  && publish.Contains("var subscribers = CopySubscribersForPublish(channelId);", StringComparison.Ordinal)
                  && replay.Contains("var subscribers = CopySubscribersForPublish(channelId);", StringComparison.Ordinal)
                  && copy.Contains("lock (_subscriberScratchLock)", StringComparison.Ordinal)
                  && copy.Contains("must not synchronously reenter Publish", StringComparison.Ordinal)
                  && !copy.Contains("SendBinary", StringComparison.Ordinal)
                  && !copy.Contains("SendDataBinary", StringComparison.Ordinal),
                "173-024A: FoxgloveSession only holds subscriber scratch lock while copying subscribers and documents scratch reentry limits");
            Check(session.Contains("[ThreadStatic]\n        private static List<AdvertiseChannel> s_singleAdvertiseChannels;", StringComparison.Ordinal)
                  && session.Contains("[ThreadStatic]\n        private static List<uint> s_singleUnadvertiseChannelIds;", StringComparison.Ordinal)
                  && advertise.Contains("s_singleAdvertiseChannels ??= new List<AdvertiseChannel>(1)", StringComparison.Ordinal)
                  && advertise.Contains("channels.Add(channel)", StringComparison.Ordinal)
                  && advertise.Contains("finally", StringComparison.Ordinal)
                  && advertise.LastIndexOf("channels.Clear()", StringComparison.Ordinal)
                     > advertise.IndexOf("JsonConvert.SerializeObject", StringComparison.Ordinal)
                  && unadvertise.Contains("s_singleUnadvertiseChannelIds ??= new List<uint>(1)", StringComparison.Ordinal)
                  && unadvertise.Contains("channelIds.Add(channelId)", StringComparison.Ordinal)
                  && unadvertise.Contains("finally", StringComparison.Ordinal)
                  && unadvertise.LastIndexOf("channelIds.Clear()", StringComparison.Ordinal)
                     > unadvertise.IndexOf("JsonConvert.SerializeObject", StringComparison.Ordinal),
                "173-024B: single advertise/unadvertise serialization reuses cleared thread-local lists without retained aliases");
            Check(dispose.Contains("Volatile.Write(ref _recorder, null)", StringComparison.Ordinal)
                  && dispose.Contains("Volatile.Write(ref _mirrorSink, null)", StringComparison.Ordinal),
                "173-024C: disposed sessions release recorder and mirror sink references");
              Check(broadcast.Contains("subscribedClientIds = GetParamSubscribersForChanged", StringComparison.Ordinal)
                    && broadcast.IndexOf("foreach (var cid in subscribedClientIds)", StringComparison.Ordinal)
                    > broadcast.LastIndexOf("finally", StringComparison.Ordinal),
                  "173-024D: parameter broadcasts release scratch locks before transport sends");
              Check(broadcast.Contains("names = new List<string>(_parameterBroadcastNames)", StringComparison.Ordinal)
                    && broadcast.IndexOf("JsonConvert.SerializeObject", StringComparison.Ordinal) > broadcast.LastIndexOf("finally", StringComparison.Ordinal)
                    && broadcast.IndexOf("GetParamSubscribersForChanged", StringComparison.Ordinal) > broadcast.LastIndexOf("finally", StringComparison.Ordinal),
                  "173-082A: parameter broadcasts copy scratch names before serialization and subscriber lookup");
              Check(session.Contains("s_jsonPublishStream", StringComparison.Ordinal)
                    && session.Contains("stream.SetLength(0);", StringComparison.Ordinal),
                  "173-024E: PublishJson reuses a thread-local payload stream");
        }

        private static ReplayMessageContext NewTestContext(string topic, string message = "phase140_3")
        {
            return new ReplayMessageContext(
                channelId: 10,
                topic: topic,
                messageEncoding: "json",
                schemaName: "phase140_3.Message",
                schemaEncoding: "json",
                logTimeNs: 100,
                replayStartTimeNs: 0,
                payload: System.Text.Encoding.UTF8.GetBytes(message),
                replaySessionId: 1);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private static string ExtractMethodBody(string source, string signature)
        {
            var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
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

        private static bool Ordered(string source, string first, string second)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }

        private static string NewTempRoot()
        {
            var path = Path.Combine(Path.GetTempPath(), "u2f-phase140-3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            try { Directory.Delete(path, recursive: true); }
            catch { }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class Phase140_3FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Connect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void Disconnect(uint clientId) => OnClientDisconnected?.Invoke(clientId);
            public void Text(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void Binary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
