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
        public void ChannelGraphPublicationFailureLeavesRegistryRetryable()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(7);
            fake.SimulateText(7, "{\"op\":\"subscribeConnectionGraph\"}");
            fake.SentTexts(7).Clear();
            var channel = new AdvertiseChannel { Id = 9, Topic = "/graph-channel", Encoding = "json" };

            fake.ThrowSendTextCount = 1;
            Assert.Throws<InvalidOperationException>(() => session.RegisterChannel(channel));
            Assert.Null(session.Channels.Get(channel.Id));
            var restoredGraph = fake.SentTexts(7)
                .Select(text => JObject.Parse(text))
                .Last(obj => obj["op"]?.ToString() == "connectionGraphUpdate");
            Assert.DoesNotContain(
                (JArray)restoredGraph["publishedTopics"],
                topic => topic["name"]?.ToString() == channel.Topic);

            session.RegisterChannel(channel);
            Assert.NotNull(session.Channels.Get(channel.Id));
        }

        [Fact]
        public void ChannelUnregisterGraphFailureRestoresChannelForRetry()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            var channel = new AdvertiseChannel { Id = 10, Topic = "/unregister-graph-channel", Encoding = "json" };
            session.RegisterChannel(channel);

            fake.SimulateConnect(7);
            fake.SimulateText(7, "{\"op\":\"subscribeConnectionGraph\"}");
            fake.SentTexts(7).Clear();
            fake.ThrowSendTextCount = 1;

            Assert.Throws<InvalidOperationException>(() => session.UnregisterChannel(channel.Id));
            Assert.NotNull(session.Channels.Get(channel.Id));
            var restoredGraph = fake.SentTexts(7)
                .Select(text => JObject.Parse(text))
                .Last(obj => obj["op"]?.ToString() == "connectionGraphUpdate");
            Assert.Contains(
                (JArray)restoredGraph["publishedTopics"],
                topic => topic["name"]?.ToString() == channel.Topic);

            session.UnregisterChannel(channel.Id);
            Assert.Null(session.Channels.Get(channel.Id));
        }

        [Fact]
        public void InvalidServicePublicationDoesNotBroadcastBeforeGraphValidation()
        {
            var fake = new ConnectRaceTransport();
            var session = new FoxgloveSession("Test", fake);
            var descriptor = new ServiceDescriptor
            {
                Name = " ", Type = "/invalid",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            };

            fake.SimulateConnect(7);
            fake.ClearMessages();
            Assert.Throws<ArgumentException>(() => session.RegisterService(descriptor));
            Assert.Empty(
                fake.MessagesFor(7)
                    .Select(text => JObject.Parse(text)["op"]?.ToString())
                    .Where(op => op == "advertiseServices"));
            Assert.Empty(session.Services.GetAll());
        }

        [Fact]
        public void ServicePublicationDoesNotAdvertiseWhenGraphBroadcastFails()
        {
            var fake = new ConnectRaceTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(7);
            fake.SimulateText(7, "{\"op\":\"subscribeConnectionGraph\"}");
            fake.ClearMessages();
            // The pre-6928 order would consume the first send on advertise;
            // fail the subsequent graph update instead.
            fake.ThrowSendTextCount = 2;
            fake.RecordBeforeThrow = true;

            var descriptor = new ServiceDescriptor
            {
                Name = "/graph-broadcast-failure", Type = "/graph-broadcast-failure",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            };

            Assert.Throws<InvalidOperationException>(() => session.RegisterService(descriptor));
            var operations = fake.MessagesFor(7)
                .Select(text => JObject.Parse(text)["op"]?.ToString())
                .Where(op => op == "advertiseServices" || op == "unadvertiseServices")
                .ToList();
            Assert.DoesNotContain("advertiseServices", operations);
            Assert.Empty(session.Services.GetAll());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t\r\n")]
        public void ServiceRegistryRejectsMissingNamesBeforeAllocatingAnId(string name)
        {
            var registry = new FoxgloveServiceRegistry();
            var descriptor = new ServiceDescriptor
            {
                Name = name,
                Type = "/invalid",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            };

            Assert.Throws<ArgumentException>(() => registry.Register(descriptor));
            Assert.Empty(registry.GetAll());
            var validId = registry.Register(new ServiceDescriptor { Name = "/valid", Type = "/valid" });
            Assert.Equal(1u, validId);
        }

        [Fact]
        public void ServicePublicationFailureRetractsPartiallyBroadcastAdvertisement()
        {
            var fake = new Phase6FakeTransport
            {
                ThrowBroadcastCount = 1,
                RecordBroadcastBeforeThrow = true
            };
            var session = new FoxgloveSession("Test", fake);
            var descriptor = new ServiceDescriptor
            {
                Name = "/partial", Type = "/partial",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            };

            Assert.Throws<InvalidOperationException>(() => session.RegisterService(descriptor));

            var operations = fake.BroadcastTexts.Select(text => JObject.Parse(text)["op"]?.ToString()).ToList();
            Assert.Equal(new[] { "advertiseServices", "unadvertiseServices" }, operations);
            Assert.Empty(session.Services.GetAll());
        }

        [Fact]
        public void UnregisterGraphFailureDoesNotWithdrawClientAdvertisement()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            var serviceId = session.RegisterService(new ServiceDescriptor
            {
                Name = "/unregister-retry", Type = "/unregister-retry",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });

            fake.BroadcastTexts.Clear();
            fake.SimulateConnect(1);
            fake.SimulateText(1, "{\"op\":\"subscribeConnectionGraph\"}");
            fake.ThrowSendTextCount = 1;

            Assert.Throws<InvalidOperationException>(() => session.UnregisterService(serviceId));
            Assert.NotNull(session.Services.GetById(serviceId));

            var operations = fake.BroadcastTexts.Select(text => JObject.Parse(text)["op"]?.ToString()).ToList();
            Assert.Empty(operations);
            Assert.True(session.UnregisterService(serviceId));
            Assert.Null(session.Services.GetById(serviceId));
        }

        [Fact]
        public void UnregisterPartialBroadcastRestoresClientAdvertisement()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            var serviceId = session.RegisterService(new ServiceDescriptor
            {
                Name = "/unregister-partial", Type = "/unregister-partial",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });

            fake.BroadcastTexts.Clear();
            fake.ThrowBroadcastCount = 1;
            fake.RecordBroadcastBeforeThrow = true;

            Assert.Throws<InvalidOperationException>(() => session.UnregisterService(serviceId));
            Assert.NotNull(session.Services.GetById(serviceId));

            var operations = fake.BroadcastTexts.Select(text => JObject.Parse(text)["op"]?.ToString()).ToList();
            Assert.Equal(new[] { "unadvertiseServices", "advertiseServices" }, operations);
        }

        [Fact]
        public async Task ClientConnectSerializesServiceSnapshotWithUnregister()
        {
            var fake = new ConnectRaceTransport();
            var session = new FoxgloveSession("Test", fake);
            var serviceId = session.RegisterService(new ServiceDescriptor
            {
                Name = "/connect-race", Type = "/connect-race",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });

            fake.ClearMessages();
            var connectTask = Task.Run(() => fake.SimulateConnect(1));
            Assert.True(
                fake.ServerInfoEntered.Wait(TimeSpan.FromSeconds(5)),
                "connect did not enter the session snapshot");

            var unregisterStarted = new ManualResetEventSlim(false);
            var unregisterCompleted = new ManualResetEventSlim(false);
            Exception unregisterFailure = null;
            var unregisterTask = Task.Run(() =>
            {
                unregisterStarted.Set();
                try
                {
                    session.UnregisterService(serviceId);
                }
                catch (Exception ex)
                {
                    unregisterFailure = ex;
                }
                finally
                {
                    unregisterCompleted.Set();
                }
            });

            try
            {
                Assert.True(
                    unregisterStarted.Wait(TimeSpan.FromSeconds(5)),
                    "unregister did not start");
                Assert.False(
                    unregisterCompleted.Wait(TimeSpan.FromMilliseconds(500)),
                    "service mutation ran while the client snapshot was in progress");
            }
            finally
            {
                fake.ReleaseServerInfo.Set();
            }

            await Task.WhenAll(connectTask, unregisterTask);
            Assert.Null(unregisterFailure);

            var serviceOperations = fake.MessagesFor(1)
                .Select(text => JObject.Parse(text)["op"]?.ToString())
                .Where(op => op == "advertiseServices" || op == "unadvertiseServices")
                .ToList();
            Assert.Equal("unadvertiseServices", serviceOperations.Last());

            fake.SimulateConnect(2);
            fake.SimulateText(2, "{\"op\":\"subscribeConnectionGraph\"}");
            var graphSnapshot = fake.MessagesFor(2)
                .Select(text => JObject.Parse(text))
                .Last(obj => obj["op"]?.ToString() == "connectionGraphUpdate");
            Assert.DoesNotContain(
                (JArray)graphSnapshot["advertisedServices"],
                service => service["name"]?.ToString() == "/connect-race");
        }

        [Fact]
        public void ServiceCallEnqueueComplete()
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

            var payload = Encoding.UTF8.GetBytes("{}");
            var frame = EncodeClientServiceCallRequest(1, 1, "json", payload);
            fake.SimulateBinary(1, frame);

            session.Services.CompleteResponse(1, 1, "json", Encoding.UTF8.GetBytes("{\"ok\":true}"));

            session.DrainServiceCalls();
            var binaries = fake.SentBinaries(1);
            Assert.True(binaries.Count > 0, "Service response sent as binary after drain");
        }

        [Fact]
        public void ServiceCallResponseSendFailureDoesNotDropLaterResponses()
        {
            var fake = new Phase6FakeTransport { ThrowBinaryForClientId = 1 };
            var session = new FoxgloveSession("Test", fake);
            var serviceId = session.Services.Register(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });

            EnqueueCompletedCall(session, serviceId, clientId: 1, callId: 10);
            EnqueueCompletedCall(session, serviceId, clientId: 2, callId: 20);

            session.DrainServiceCalls();

            var response = Assert.Single(fake.SentBinaries(2));
            Assert.Equal(20u, BinaryEncoding.ReadU32LE(response, 5));
        }

        [Fact]
        public void ServiceCallRecordingFailureDoesNotDropLaterResponses()
        {
            var fake = new Phase6FakeTransport();
            var clock = new ThrowOnceClock();
            var session = new FoxgloveSession("Test", fake, clock);
            var serviceId = session.Services.Register(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            session.SetRecorder(recorder);

            EnqueueCompletedCall(session, serviceId, clientId: 1, callId: 10);
            EnqueueCompletedCall(session, serviceId, clientId: 2, callId: 20);
            clock.ThrowOnNextRead = true;

            session.DrainServiceCalls();

            Assert.Single(fake.SentBinaries(1));
            Assert.Single(fake.SentBinaries(2));
        }

    }
}
