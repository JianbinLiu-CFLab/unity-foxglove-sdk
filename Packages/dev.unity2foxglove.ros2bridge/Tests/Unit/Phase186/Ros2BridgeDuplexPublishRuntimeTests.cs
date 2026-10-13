// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: Certify publish compatibility on the owned duplex writer lane.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed partial class Ros2BridgeDuplexPublishRuntimeTests
    {
        private const string Topic =
            "/phase186/duplex/publish";
        private const string Schema =
            "phase186_msgs/msg/DuplexPublish";

        [Fact]
        public void PublishUsesOneDuplexWriterWithoutInboundPipeline()
        {
            var transport = new DuplexTransport(
                DuplexMode.Happy);
            var owner =
                FoxRunTransportRetirementOwner.CreateForTests(3);
            using var runtime = CreateRuntime(
                transport,
                owner,
                FoxRunTransportDirection.Publish);

            runtime.Start(enabled: true, autoConnect: true);
            WaitForPublisherReady(runtime);
            Assert.False(runtime.HasInboundPipeline);
            Assert.True(
                runtime.TryEnqueuePrepared(
                    Frame(sequence: 1),
                    out var reason),
                reason);
            WaitUntil(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the duplex publish did not complete");

            Assert.Equal(
                new[]
                {
                    U2R2Operation.Hello,
                    U2R2Operation.PreparePublisher,
                    U2R2Operation.Publish,
                },
                transport.Requests
                    .Select(item => item.Message.Operation)
                    .ToArray());
            Assert.Equal(0, transport.LegacyV2ExchangeCount);
            Assert.Equal(0, transport.LegacyRawWriteCount);
        }

        [Fact]
        public void TransientContentionDoesNotHoldRequestAdmissionGate()
        {
            var transport = new DuplexTransport(DuplexMode.Happy);
            using var runtime = CreateRuntime(
                transport,
                FoxRunTransportRetirementOwner.CreateForTests(3),
                FoxRunTransportDirection.Publish);

            runtime.Start(enabled: true, autoConnect: true);
            WaitForPublisherReady(runtime);

            U2R2ByteLease blocker = null;
            try
            {
                lock (OutboundSchedulerGate(runtime))
                {
                    Assert.True(
                        runtime.TryEnqueuePrepared(
                            Frame(sequence: 9),
                            out var reason),
                        reason);
                    blocker = ReserveAllRuntimeTransient(runtime);
                }
                WaitUntil(
                    () => runtime.GetStatsSnapshot().InFlightBytes > 0,
                    "the publish worker did not acquire the accepted frame");
                Thread.Sleep(100);

                var admissionGate = RequestAdmissionGate(runtime);
                var acquired = Monitor.TryEnter(admissionGate, 250);
                if (acquired)
                    Monitor.Exit(admissionGate);
                Assert.True(
                    acquired,
                    "transient contention held the connection request-admission gate");
            }
            finally
            {
                blocker?.Dispose();
            }

            WaitUntil(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the bounded publish did not resume when transient capacity returned");
        }

        [Fact]
        public void ReadyDuplexStateOwnsLivenessWhenSocketProbeRacesReader()
        {
            var transport = new DuplexTransport(
                DuplexMode.FalseNegativeHealthProbeAfterHello);
            using var runtime = CreateRuntime(
                transport,
                FoxRunTransportRetirementOwner.CreateForTests(3),
                FoxRunTransportDirection.Publish,
                reconnectIntervalMs: 10);

            runtime.Start(enabled: true, autoConnect: true);
            WaitUntil(
                () => transport.Requests.Any(item =>
                    item.Message.Operation == U2R2Operation.Hello),
                "the duplex hello did not complete");
            WaitUntil(
                () => transport.HealthProbeUnavailable,
                "the scripted socket probe false negative was not published");
            Assert.False(
                transport.IsConnected,
                "the transport probe did not reproduce its concurrent-reader false negative");
            Assert.False(
                SpinWait.SpinUntil(
                    () => transport.ConnectCount > 1,
                    TimeSpan.FromMilliseconds(250)),
                "a ready duplex state was discarded because of a racy socket probe");
            Assert.True(runtime.IsConnected);

            WaitForPublisherReady(runtime);
            Assert.True(
                runtime.TryEnqueuePrepared(
                    Frame(sequence: 4),
                    out var reason),
                reason);
            WaitUntil(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the ready duplex state did not retain its writer lane");
            Assert.Equal(1, transport.ConnectCount);
        }

        [Fact]
        public void PublishOnlyIncompatibilityUsesFreshLegacySocket()
        {
            var transport = new DuplexTransport(
                DuplexMode.V2Incompatible);
            using var runtime = CreateRuntime(
                transport,
                FoxRunTransportRetirementOwner
                    .CreateForTests(3),
                FoxRunTransportDirection.Publish);

            runtime.Start(enabled: true, autoConnect: true);
            WaitForPublisherReady(runtime);
            Assert.True(
                runtime.TryEnqueuePrepared(
                    Frame(sequence: 2),
                    out var reason),
                reason);
            WaitUntil(
                () => transport.LegacyRawWriteCount == 1,
                "the legacy publish did not complete");

            Assert.Equal(2, transport.ConnectCount);
            var hello = Assert.Single(transport.Requests);
            Assert.Equal(U2R2Operation.Hello, hello.Message.Operation);
            Assert.Equal(1, hello.Connection);
            Assert.Equal(
                new[] { 2 },
                transport.LegacyPreparationConnections);
            Assert.Equal(
                new[] { 2 },
                transport.LegacyRawWriteConnections);
            Assert.Equal(0, transport.LegacyV2ExchangeCount);
            Assert.False(
                transport.IsDisposed,
                "v1 fallback reused a transport after disposing it");
        }

        [Fact]
        public async Task AbandonedStartedConnectionReturnsItsReservedWorkerSlots()
        {
            var providerId = new FoxRunTransportId(
                "unity2foxglove.ros2bridge.abandoned-duplex");
            var owner =
                FoxRunTransportRetirementOwner.CreateForTests(3);
            Assert.True(
                owner.TryReserveExclusive(
                    providerId,
                    FoxRunTransportDirection.Publish,
                    generation: 1,
                    workerCount: 3,
                    out var reservation));
            var transport = new DuplexTransport(
                DuplexMode.HoldHelloResponse);
            var worker = new Ros2BridgeWorkerLease(
                "127.0.0.1",
                port: 8765,
                queueCapacity: 8,
                reconnectIntervalMs: 20,
                sendTimeoutMs: 1000,
                transport,
                reservation,
                "abandoned-duplex",
                requiresSubscription: false,
                enableDuplexSession: true,
                sessionGeneration: 1,
                Ros2BridgeStatsSnapshot.Disabled);
            FoxRunTransportRetirementReservation replacement = null;
            try
            {
                SetPrivateField(worker, "_enabled", true);
                SetPrivateField(worker, "_workerGeneration", 1L);
                var ensureConnected =
                    typeof(Ros2BridgeWorkerLease).GetMethod(
                        "EnsureConnected",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException(
                        "The Bridge EnsureConnected boundary is missing.");
                var connect = Task.Factory.StartNew(
                    () => (bool)ensureConnected.Invoke(
                        worker,
                        new object[] { 1L }),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                Assert.True(
                    transport.HelloWritten.Wait(TimeSpan.FromSeconds(5)),
                    "the owned connection did not start its hello");

                var gate = RequiredPrivateField(
                    typeof(Ros2BridgeWorkerLease),
                    "_gate").GetValue(worker);
                lock (gate)
                    SetPrivateField(worker, "_stopRequested", true);
                transport.AllowHelloResponse.Set();

                Assert.False(await connect);
                Assert.True(reservation.TryReturn(workerIndex: 0));
                Assert.True(
                    owner.TryReserveExclusive(
                        providerId,
                        FoxRunTransportDirection.Publish,
                        generation: 2,
                        workerCount: 3,
                        out replacement),
                    "the abandoned connection retained reader/writer retirement slots");
            }
            finally
            {
                transport.AllowHelloResponse.Set();
                replacement?.Dispose();
                worker.Dispose();
                reservation.Dispose();
            }
        }

        [Fact]
        public void SubscriptionNeverFallsBackToV1()
        {
            var transport = new DuplexTransport(
                DuplexMode.V2Incompatible);
            using var runtime = CreateRuntime(
                transport,
                FoxRunTransportRetirementOwner
                    .CreateForTests(3),
                FoxRunTransportDirection.Subscribe,
                reconnectIntervalMs: 10);

            runtime.Start(enabled: true, autoConnect: true);
            WaitUntil(
                () => transport.ConnectCount >= 2,
                "the subscription runtime did not retry v2");
            runtime.Stop();

            Assert.All(
                transport.Requests,
                request => Assert.Equal(
                    U2R2Operation.Hello,
                    request.Message.Operation));
            Assert.Empty(
                transport.LegacyPreparationConnections);
            Assert.Empty(
                transport.LegacyRawWriteConnections);
            Assert.Equal(0, transport.LegacyV2ExchangeCount);
        }

        [Fact]
        public void ReconnectReusesReservedWorkersAndReplaysPreparation()
        {
            var providerId = new FoxRunTransportId(
                "unity2foxglove.ros2bridge.duplex-reconnect");
            var owner =
                FoxRunTransportRetirementOwner.CreateForTests(3);
            var transport = new DuplexTransport(
                DuplexMode.DisconnectAfterFirstPreparation);
            using var runtime = CreateRuntime(
                transport,
                owner,
                FoxRunTransportDirection.Publish,
                providerId: providerId,
                reconnectIntervalMs: 10);

            runtime.Start(enabled: true, autoConnect: true);
            _ = runtime.PreparePublisher(
                Topic,
                Schema,
                Qos(),
                out _);
            WaitUntil(
                () => transport.ConnectCount >= 2,
                "the duplex runtime did not reconnect");
            WaitUntil(
                () => runtime.PreparePublisher(
                          Topic,
                          Schema,
                          Qos(),
                          out _)
                      == Ros2BridgePublisherReadiness.Ready
                      && runtime.TryEnqueuePrepared(
                          Frame(sequence: 3),
                          out _),
                "the post-reconnect publisher did not become ready and enqueue on one connection generation");
            WaitUntil(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the post-reconnect publish did not complete");

            var requests = transport.Requests;
            Assert.Equal(
                new[] { 1, 2 },
                requests
                    .Where(item =>
                        item.Message.Operation
                        == U2R2Operation.Hello)
                    .Select(item => item.Connection)
                    .ToArray());
            Assert.Equal(
                new[] { 1, 2 },
                requests
                    .Where(item =>
                        item.Message.Operation
                        == U2R2Operation.PreparePublisher)
                    .Select(item => item.Connection)
                    .ToArray());
            Assert.Equal(
                2,
                Assert.Single(
                    requests,
                    item =>
                        item.Message.Operation
                        == U2R2Operation.Publish)
                    .Connection);

            runtime.Stop();
            Assert.True(
                owner.TryReserveExclusive(
                    providerId,
                    FoxRunTransportDirection.Publish,
                    generation: 2,
                    workerCount: 3,
                    out var replacement));
            replacement.Dispose();
        }

    }
}
