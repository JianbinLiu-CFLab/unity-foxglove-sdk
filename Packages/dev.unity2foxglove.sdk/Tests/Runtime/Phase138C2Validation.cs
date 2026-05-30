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
    /// <summary>
    /// Regression checks for 138C2 subscription routing behavior on shared channels.
    /// </summary>
    public static class Phase138C2Validation
    {
        private static int _passed;

        /// <summary>
        /// Runs all Phase 138C2 validation checks and prints pass/fail results.
        /// </summary>
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

        /// <summary>
        /// Verifies subscription id 0 can be routed like any other subscription id.
        /// </summary>
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

        /// <summary>
        /// Verifies duplicate subscribe messages for the same channel/id are de-duplicated.
        /// </summary>
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

        /// <summary>
        /// Verifies an explicit unsubscribe removes subscription state and blocks further data.
        /// </summary>
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

        /// <summary>
        /// Tracks and throws if a validation check fails.
        /// </summary>
        /// <param name="condition">Whether the check passed.</param>
        /// <param name="name">Readable check name for log output.</param>
        private static void Check(bool condition, string name)
        {
            _passed++;
            Console.WriteLine(condition ? $"[PASS] {name}" : $"[FAIL] {name}");
            if (!condition)
                throw new InvalidOperationException($"Phase 138C2 validation failed: {name}");
        }

        /// <summary>
        /// Lightweight transport stub used by validation tests.
        /// </summary>
        private sealed class Phase138C2FakeTransport : IFoxgloveTransport
        {
            private readonly Dictionary<uint, List<string>> _texts = new();
            private readonly Dictionary<uint, List<byte[]>> _binaries = new();

            /// <summary>
            /// Gets whether the transport is running.
            /// </summary>
            public bool IsRunning { get; private set; }

            /// <summary>Client connected event.</summary>
            public event Action<uint> OnClientConnected;
            /// <summary>Client disconnected event.</summary>
            public event Action<uint> OnClientDisconnected;
            /// <summary>Text message received event.</summary>
            public event Action<uint, string> OnTextReceived;
            /// <summary>Binary message received event.</summary>
            public event Action<uint, byte[]> OnBinaryReceived;

            /// <summary>
            /// Starts the transport.
            /// </summary>
            public void Start(string host, int port) => IsRunning = true;
            /// <summary>
            /// Stops the transport.
            /// </summary>
            public void Stop() => IsRunning = false;
            /// <summary>
            /// Disposes transport resources.
            /// </summary>
            public void Dispose() { }
            /// <summary>
            /// Broadcasts a JSON text frame (not used in this fake transport).
            /// </summary>
            public void BroadcastText(string json) { }
            /// <summary>
            /// Broadcasts a binary frame (not used in this fake transport).
            /// </summary>
            public void BroadcastBinary(byte[] data) { }

            /// <summary>
            /// Records a text frame for a specific client.
            /// </summary>
            public void SendText(uint clientId, string json)
            {
                if (!_texts.TryGetValue(clientId, out var list))
                    _texts[clientId] = list = new List<string>();
                list.Add(json);
            }

            /// <summary>
            /// Records a binary frame for a specific client.
            /// </summary>
            public void SendBinary(uint clientId, byte[] data)
            {
                if (!_binaries.TryGetValue(clientId, out var list))
                    _binaries[clientId] = list = new List<byte[]>();
                list.Add(data);
            }

            /// <summary>
            /// Returns all recorded binary frames for a client.
            /// </summary>
            public IReadOnlyList<byte[]> BinariesFor(uint clientId)
                => _binaries.TryGetValue(clientId, out var list) ? list : Array.Empty<byte[]>();

            /// <summary>
            /// Returns all recorded text frames for a client.
            /// </summary>
            public IReadOnlyList<string> TextsFor(uint clientId)
                => _texts.TryGetValue(clientId, out var list) ? list : Array.Empty<string>();

            /// <summary>
            /// Injects a text message event for test transport interaction.
            /// </summary>
            public void Text(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }
    }
}
