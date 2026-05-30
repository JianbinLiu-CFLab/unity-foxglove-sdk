// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138C2 regression checks for shared-channel routing and subscription ids.

using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase138C2Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138C2: Shared channel subscription-routing regression ===");
            _passed = 0;

            VerifyZeroSubscriptionIdRoutesNormally();
            VerifyDuplicateSubscribeDeduplicates();
            VerifyUnsubscribeFullyRemovesSubscriber();

            Console.WriteLine($"Phase 138C2: {_passed} checks passed.");
            Console.WriteLine();
        }

        // id 0 is a valid Foxglove subscription id; it must route like any other.
        private static void VerifyZeroSubscriptionIdRoutesNormally()
        {
            var transport = new Phase138C2FakeTransport();
            using var session = new FoxgloveSession("phase138-c2-zero", transport);
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 77,
                Topic = "/phase138-c2/route",
                Encoding = "json",
                SchemaName = "",
                Schema = ""
            });

            transport.Text(11, JsonConvert.SerializeObject(new SubscribeMessage
            {
                Subscriptions = new List<Subscription>
                {
                    new Subscription { Id = 0, ChannelId = 77 }
                }
            }));

            session.Publish(77, Encoding.UTF8.GetBytes("{\"ok\":true}"), 1);
            var frames = transport.BinariesFor(11);
            Check(frames.Count == 1,
                "138C2-1: subscription id 0 is routed like any other id");

            if (frames.Count == 0)
                return;

            var decodeOk = BinaryEncoding.TryDecodeServerMessageData(frames[0], out var subscriptionId, out _, out _);
            Check(decodeOk,
                "138C2-2: routed frame is a valid server MessageData frame");
            Check(subscriptionId == 0,
                "138C2-3: routed frame carries the client's subscription id (0)");
        }

        // Re-subscribing the same (client, channel) twice must yield a single
        // subscriber, so a publish is delivered exactly once (no phantom dup).
        private static void VerifyDuplicateSubscribeDeduplicates()
        {
            var transport = new Phase138C2FakeTransport();
            using var session = new FoxgloveSession("phase138-c2-dup", transport);
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 78,
                Topic = "/phase138-c2/dup",
                Encoding = "json",
                SchemaName = "",
                Schema = ""
            });

            var subscribe = JsonConvert.SerializeObject(new SubscribeMessage
            {
                Subscriptions = new List<Subscription>
                {
                    new Subscription { Id = 5, ChannelId = 78 }
                }
            });
            transport.Text(22, subscribe);
            transport.Text(22, subscribe);

            session.Publish(78, Encoding.UTF8.GetBytes("{\"ok\":true}"), 2);
            Check(transport.BinariesFor(22).Count == 1,
                "138C2-4: duplicate subscribe routes the publish exactly once");
        }

        // After unsubscribe the subscriber is fully removed; later publishes
        // reach no one (no leftover phantom from the reverse index).
        private static void VerifyUnsubscribeFullyRemovesSubscriber()
        {
            var transport = new Phase138C2FakeTransport();
            using var session = new FoxgloveSession("phase138-c2-unsub", transport);
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 79,
                Topic = "/phase138-c2/unsub",
                Encoding = "json",
                SchemaName = "",
                Schema = ""
            });

            transport.Text(33, JsonConvert.SerializeObject(new SubscribeMessage
            {
                Subscriptions = new List<Subscription>
                {
                    new Subscription { Id = 0, ChannelId = 79 }
                }
            }));
            transport.Text(33, JsonConvert.SerializeObject(new UnsubscribeMessage
            {
                SubscriptionIds = new List<uint> { 0 }
            }));

            session.Publish(79, Encoding.UTF8.GetBytes("{\"ok\":true}"), 3);
            Check(transport.BinariesFor(33).Count == 0,
                "138C2-5: unsubscribe (id 0) fully removes the subscriber");
        }

        private static void Check(bool condition, string name)
        {
            _passed++;
            Console.WriteLine(condition ? $"[PASS] {name}" : $"[FAIL] {name}");
            if (!condition)
                throw new InvalidOperationException($"Phase 138C2 validation failed: {name}");
        }

        private sealed class Phase138C2FakeTransport : IFoxgloveTransport
        {
            private readonly Dictionary<uint, List<string>> _texts = new();
            private readonly Dictionary<uint, List<byte[]>> _binaries = new();

            public bool IsRunning { get; private set; }

            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }

            public void SendText(uint clientId, string json)
            {
                if (!_texts.TryGetValue(clientId, out var list))
                    _texts[clientId] = list = new List<string>();
                list.Add(json);
            }

            public void SendBinary(uint clientId, byte[] data)
            {
                if (!_binaries.TryGetValue(clientId, out var list))
                    _binaries[clientId] = list = new List<byte[]>();
                list.Add(data);
            }

            public IReadOnlyList<byte[]> BinariesFor(uint clientId)
                => _binaries.TryGetValue(clientId, out var list) ? list : Array.Empty<byte[]>();

            public IReadOnlyList<string> TextsFor(uint clientId)
                => _texts.TryGetValue(clientId, out var list) ? list : Array.Empty<string>();

            public void Text(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }
    }
}
