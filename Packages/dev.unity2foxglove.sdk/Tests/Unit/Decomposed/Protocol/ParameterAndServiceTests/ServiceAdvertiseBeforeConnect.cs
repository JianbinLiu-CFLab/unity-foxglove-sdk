// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: serverInfo capabilities, parameter store/subscriptions, service
//          advertise, binary service codec, and call timeout/sweep
//          (migrated from Phase6Validation; all checks are fake-transport pure logic).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// Server capabilities, parameter store and subscriptions, service advertise,
    /// binary service-call codec, and call timeout/sweep. Ported from Phase6Validation.
    /// </summary>


public partial class ParameterAndServiceTests
    {
        [Fact]
        public void ServiceAdvertiseBeforeConnect()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterService(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });
            fake.SimulateConnect(1);
            var texts = fake.SentTexts(1);
            var hasAdv = false;
            foreach (var t in texts)
                if (JObject.Parse(t)["op"]?.ToString() == "advertiseServices") hasAdv = true;
            Assert.True(hasAdv, "New client receives service advertise snapshot");
        }

        [Fact]
        public void ServiceSnapshotPrecedesTopicSnapshotForPanelReadiness()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/unity/status",
                Encoding = "json",
                SchemaName = "foxglove.Log"
            });
            session.RegisterService(new ServiceDescriptor
            {
                Name = "/foxrun/subscription-contracts",
                Type = "/foxrun/subscription-contracts",
                Request = new ServiceSchemaDescriptor { SchemaName = "FoxRunCatalogRequest" },
                Response = new ServiceSchemaDescriptor { SchemaName = "FoxRunCatalogResponse" }
            });

            fake.SimulateConnect(1);
            var operations = fake.SentTexts(1)
                .Select(text => JObject.Parse(text)["op"]?.ToString())
                .ToList();

            Assert.True(
                operations.IndexOf("advertiseServices") < operations.IndexOf("advertise"),
                "Service snapshot must precede topics so a panel topics render is a service-readiness barrier.");
        }

        [Fact]
        public void ServiceAdvertiseAfterConnect()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            fake.BroadcastTexts.Clear();

            session.RegisterService(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });

            var hasAdv = false;
            foreach (var t in fake.BroadcastTexts)
                if (JObject.Parse(t)["op"]?.ToString() == "advertiseServices") hasAdv = true;
            Assert.True(hasAdv, "RegisterService broadcasts advertiseServices to already-connected client");
        }

        [Fact]
        public void ServiceCallFailureUnknown()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            var request = EncodeClientServiceCallRequest(999, 1, "json", Encoding.UTF8.GetBytes("{}"));

            fake.SimulateBinary(1, request);
            var sent = fake.SentTexts(1);
            var failure = JObject.Parse(sent.Last());
            Assert.True(failure["op"]?.ToString() == "serviceCallFailure", "Unknown service → failure");
        }

        [Fact]
        public void ServiceCallFailureEncoding()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.Services.Register(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });
            fake.SimulateConnect(1);

            var req = EncodeClientServiceCallRequest(1, 1, "protobuf", new byte[] { 1 });
            fake.SimulateBinary(1, req);
            var sent = fake.SentTexts(1);
            Assert.True(sent.Last().Contains("Unsupported encoding"), "Wrong encoding → failure");
        }

        [Fact]
        public void InvalidUtf8ServicePayloadIsRejectedBeforeHandlerDispatch()
        {
            var fake = new Phase6FakeTransport();
            var calls = 0;
            var session = new FoxgloveSession("Test", fake);
            var serviceId = session.Services.Register(
                new ServiceDescriptor
                {
                    Name = "/strict", Type = "/strict",
                    Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                    Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
                },
                _ =>
                {
                    calls++;
                    return new JObject { ["ok"] = true };
                });
            fake.SimulateConnect(1);

            // A malformed UTF-8 byte inside a JSON string becomes U+FFFD under
            // replacement decoding and would otherwise be dispatched.
            var invalidUtf8Json = new byte[] { 0x22, 0xC3, 0x22 };
            fake.SimulateBinary(1, EncodeClientServiceCallRequest(serviceId, 7, "json", invalidUtf8Json));
            session.DrainServiceCalls();

            Assert.Equal(0, calls);
            Assert.Contains("Malformed JSON payload", fake.SentTexts(1).Last(), StringComparison.Ordinal);
        }

        [Fact]
        public void ServicePublicationFailureLeavesRegistryRetryable()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            var descriptor = new ServiceDescriptor
            {
                Name = "/atomic", Type = "/atomic",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            };

            fake.ThrowBroadcastCount = 1;
            Assert.Throws<InvalidOperationException>(() => session.RegisterService(descriptor));
            Assert.Empty(session.Services.GetAll());

            var serviceId = session.RegisterService(descriptor);
            Assert.NotNull(session.Services.GetById(serviceId));

            fake.ThrowBroadcastCount = 1;
            Assert.Throws<InvalidOperationException>(() => session.UnregisterService(serviceId));
            Assert.NotNull(session.Services.GetById(serviceId));

            Assert.True(session.UnregisterService(serviceId));
            Assert.Null(session.Services.GetById(serviceId));
        }

        [Fact]
        public void ChannelPublicationFailureLeavesRegistryRetryable()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            var channel = new AdvertiseChannel
            {
                Id = 7,
                Topic = "/atomic-channel",
                Encoding = "json",
                SchemaName = "std_msgs/String"
            };

            fake.ThrowBroadcastCount = 1;
            Assert.Throws<InvalidOperationException>(() => session.RegisterChannel(channel));
            Assert.Null(session.Channels.Get(channel.Id));

            session.RegisterChannel(channel);
            Assert.NotNull(session.Channels.Get(channel.Id));

            fake.ThrowBroadcastCount = 1;
            Assert.Throws<InvalidOperationException>(() => session.UnregisterChannel(channel.Id));
            Assert.NotNull(session.Channels.Get(channel.Id));

            session.UnregisterChannel(channel.Id);
            Assert.Null(session.Channels.Get(channel.Id));
        }

        [Fact]
        public void RecordingOnlyChannelCanSwitchDescriptorAndAdvertiseBeforeGraphUpdate()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterRecordingOnlyChannel(new AdvertiseChannel
            {
                Id = 11,
                Topic = "/recording-only",
                Encoding = "json",
                SchemaName = "old"
            });

            fake.SimulateConnect(1);
            fake.SimulateText(1, "{\"op\":\"subscribeConnectionGraph\"}");
            fake.OrderedTexts.Clear();
            fake.SentTexts(1).Clear();

            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 11,
                Topic = "/live-after-recording",
                Encoding = "json",
                SchemaName = "new"
            });

            Assert.Equal("/live-after-recording", session.Channels.Get(11).Topic);
            Assert.Equal(
                new[] { "advertise", "connectionGraphUpdate" },
                fake.OrderedTexts.Select(text => JObject.Parse(text)["op"]?.ToString()));
        }

        [Fact]
        public void LiveChannelCanSwitchToRecordingOnlyAndUnadvertiseBeforeGraphUpdate()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 12,
                Topic = "/live-before-recording",
                Encoding = "json",
                SchemaName = "old"
            });

            fake.SimulateConnect(1);
            fake.SimulateText(1,
                "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":3,\"channelId\":12}]}");
            fake.SimulateText(1, "{\"op\":\"subscribeConnectionGraph\"}");
            fake.OrderedTexts.Clear();
            fake.SentTexts(1).Clear();
            fake.SentBinaries(1).Clear();

            session.RegisterRecordingOnlyChannel(new AdvertiseChannel
            {
                Id = 12,
                Topic = "/recording-after-live",
                Encoding = "json",
                SchemaName = "new"
            });

            Assert.Equal("/recording-after-live", session.Channels.Get(12).Topic);
            Assert.Equal(
                new[] { "unadvertise", "connectionGraphUpdate" },
                fake.OrderedTexts.Select(text => JObject.Parse(text)["op"]?.ToString()));

            session.Publish(12, new byte[] { 1, 2, 3 }, 10);
            Assert.True(
                fake.SentBinaries(1).Count == 0,
                "recording-only switch removes the live subscription");
        }

        [Fact]
        public void ChannelPublicationFailureRetractsPartiallyBroadcastAdvertisement()
        {
            var fake = new Phase6FakeTransport
            {
                ThrowBroadcastCount = 1,
                RecordBroadcastBeforeThrow = true
            };
            var session = new FoxgloveSession("Test", fake);
            var channel = new AdvertiseChannel { Id = 8, Topic = "/partial-channel", Encoding = "json" };

            Assert.Throws<InvalidOperationException>(() => session.RegisterChannel(channel));

            var operations = fake.BroadcastTexts
                .Select(text => JObject.Parse(text)["op"]?.ToString())
                .ToList();
            Assert.Equal(new[] { "advertise", "unadvertise" }, operations);
            Assert.Null(session.Channels.Get(channel.Id));
        }

        [Fact]
        public void IdempotentLiveChannelFailureDoesNotRetractExistingAdvertisement()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            var channel = new AdvertiseChannel
            {
                Id = 13,
                Topic = "/idempotent-live-channel",
                Encoding = "json"
            };
            session.RegisterChannel(channel);
            fake.SimulateConnect(1);
            fake.BroadcastTexts.Clear();

            fake.ThrowBroadcastCount = 1;
            fake.RecordBroadcastBeforeThrow = true;
            Assert.Throws<InvalidOperationException>(() => session.RegisterChannel(channel));

            Assert.Equal(new[] { "advertise" },
                fake.BroadcastTexts.Select(text => JObject.Parse(text)["op"]?.ToString()));
            Assert.NotNull(session.Channels.Get(channel.Id));
        }

        [Fact]
        public void IdempotentLiveChannelFailureTreatsNullAndEmptySchemaAsEquivalent()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 14,
                Topic = "/null-schema",
                Encoding = "json",
                Schema = string.Empty
            });
            fake.SimulateConnect(1);
            fake.BroadcastTexts.Clear();

            var previous = session.Channels.Get(14);
            var schemaField = typeof(AdvertiseChannel).GetField(
                "_schema",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(previous);
            Assert.NotNull(schemaField);
            schemaField.SetValue(previous, null);

            fake.ThrowBroadcastCount = 1;
            fake.RecordBroadcastBeforeThrow = true;
            Assert.Throws<InvalidOperationException>(() => session.RegisterChannel(new AdvertiseChannel
            {
                Id = 14,
                Topic = "/null-schema",
                Encoding = "json",
                Schema = string.Empty
            }));

            Assert.Equal(
                new[] { "advertise" },
                fake.BroadcastTexts.Select(text => JObject.Parse(text)["op"]?.ToString()));
            Assert.NotNull(session.Channels.Get(14));
        }

    }
}
