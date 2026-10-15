// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first real-stream checks for the bounded duplex connection.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed partial class Ros2BridgeConnectionTests
    {
        [Fact]
        public void GeneratedSubscribersShareOnePhysicalLeaseAndApplyOnPumpThread()
        {
            using var sendMessage = new ManualResetEventSlim(false);
            using var sendFault = new ManualResetEventSlim(false);
            using var registerSeen = new ManualResetEventSlim(false);
            using var allowRegistration = new ManualResetEventSlim(false);
            using var unregisterSeen = new ManualResetEventSlim(false);
            var connectionAttempt = 0;
            using var peer = LoopbackPeer.StartAcceptingReconnects(stream =>
            {
                if (Interlocked.Increment(ref connectionAttempt) == 1)
                {
                    ReadWireFrame(stream);
                    throw new IOException("intentional initial handshake disconnect");
                }

                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(
                    stream,
                    HelloAck(
                        hello.RequestId,
                        includeSubscribe: true));

                var register = Parse(ReadWireFrame(stream));
                Assert.Equal(
                    U2R2Operation.RegisterSubscription,
                    register.Operation);
                registerSeen.Set();
                Assert.True(
                    allowRegistration.Wait(TimeSpan.FromSeconds(3)));
                WriteFrame(
                    stream,
                    ContractResponse(
                        "subscription_ready",
                        register.RequestId,
                        register.ContractId));

                Assert.True(
                    sendMessage.Wait(TimeSpan.FromSeconds(3)));
                WriteFrame(
                    stream,
                    new JObject
                    {
                        ["connectionGeneration"] = 19,
                        ["contractId"] = register.ContractId,
                        ["encoding"] = "cdr",
                        ["messageId"] = 1,
                        ["op"] = "message",
                        ["protocolVersion"] = 2,
                        ["receiveTimeNs"] = 2,
                        ["representation"] = "xcdr1-le",
                        ["schemaName"] = register.SchemaName,
                        ["sequence"] = 1,
                        ["sessionId"] = "phase186-session",
                        ["topic"] = register.Topic,
                    },
                    new byte[] { 0x00, 0x01, 0x00, 0x00, 0x2a });
                Assert.True(
                    sendFault.Wait(TimeSpan.FromSeconds(3)));
                WriteFrame(
                    stream,
                    new JObject
                    {
                        ["connectionGeneration"] = 19,
                        ["contractId"] = register.ContractId,
                        ["encoding"] = "cdr",
                        ["messageId"] = 2,
                        ["op"] = "message",
                        ["protocolVersion"] = 2,
                        ["receiveTimeNs"] = 3,
                        ["representation"] = "xcdr1-le",
                        ["schemaName"] = register.SchemaName,
                        ["sequence"] = 2,
                        ["sessionId"] = "phase186-session",
                        ["topic"] = register.Topic,
                    },
                    new byte[] { 0x00, 0x01, 0x00, 0x00, 0x2b });

                var unregister = Parse(ReadWireFrame(stream));
                Assert.Equal(
                    U2R2Operation.UnregisterSubscription,
                    unregister.Operation);
                Assert.Equal(register.ContractId, unregister.ContractId);
                unregisterSeen.Set();
                WriteFrame(
                    stream,
                    ContractResponse(
                        "subscription_removed",
                        unregister.RequestId,
                        unregister.ContractId));
            });

            var providerId = new FoxRunTransportId(
                "unity2foxglove.ros2bridge.generated-duplex");
            using var runtime = new Ros2BridgeRuntime(
                "127.0.0.1",
                peer.Port,
                queueCapacity: 8,
                reconnectIntervalMs: 100,
                sendTimeoutMs: 5000,
                sinkFactory: null,
                retirementOwner:
                    FoxRunTransportRetirementOwner.CreateForTests(3),
                providerId: providerId,
                direction: FoxRunTransportDirection.Publish,
                generation: 7,
                joinTimeoutMs: 5000,
                requiresSubscription: true);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(
                SpinWait.SpinUntil(
                    () => runtime.IsConnected,
                    TimeSpan.FromSeconds(30)),
                "the generated duplex runtime did not complete hello; connections="
                + peer.Connections
                + "; lastError="
                + runtime.GetStatsSnapshot().LastError);
            Assert.True(peer.Connections >= 2);
            Assert.True(runtime.HasInboundPipeline);

            using var subscriptions =
                new Ros2BridgeGeneratedSubscriptionRuntime(
                    runtime,
                    providerId,
                    generation: 7);
            var callbackThread = Environment.CurrentManagedThreadId;
            var callbackCount = 0;
            var throwOnPayload = false;
            byte[] firstPayload = null;
            var route = new FoxRunTransportSubscribeRoute(
                "phase186/generated/shared-binding",
                "/phase186/generated/shared",
                "phase186_msgs/msg/GeneratedShared",
                maxPayloadBytes: 32,
                FoxRunDeliveryPolicy.ProviderDefault,
                (payload, receiveTimeNs, sequence) =>
                {
                    if (throwOnPayload)
                        throw new InvalidDataException("generated apply failed");
                    Assert.Equal(
                        callbackThread,
                        Environment.CurrentManagedThreadId);
                    Assert.Equal(2UL, receiveTimeNs);
                    Assert.Equal(1UL, sequence);
                    firstPayload ??= payload.ToArray();
                    callbackCount++;
                },
                messageEncoding: "cdr");

            var first = default(FoxRunTransportSubscribeResult);
            Exception subscribeFailure = null;
            var subscribeThread = new Thread(() =>
            {
                try
                {
                    first = subscriptions.Subscribe(in route);
                }
                catch (Exception exception)
                {
                    subscribeFailure = exception;
                }
            })
            {
                IsBackground = true,
                Name = "phase186-generated-subscribe",
            };
            subscribeThread.Start();
            Assert.True(
                registerSeen.Wait(TimeSpan.FromSeconds(3)));
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var pending));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Pending,
                pending.State);
            allowRegistration.Set();
            Assert.True(subscribeThread.Join(TimeSpan.FromSeconds(3)));
            Assert.Null(subscribeFailure);
            Assert.Equal(
                FoxRunTransportRouteResultState.Accepted,
                first.State);
            var second = subscriptions.Subscribe(in route);
            Assert.Equal(
                FoxRunTransportRouteResultState.Accepted,
                second.State);
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var active));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Active,
                active.State);
            Assert.Equal(2, active.Attempts);
            Assert.Equal(2, active.ActiveLeases);

            sendMessage.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () =>
                    {
                        subscriptions.Pump(maxFrames: 8);
                        return callbackCount == 2;
                    },
                    TimeSpan.FromSeconds(3)),
                "the generated subscriptions were not applied on the pump thread");
            Assert.Equal(
                new byte[] { 0x00, 0x01, 0x00, 0x00, 0x2a },
                firstPayload);
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var applied));
            Assert.Equal(1, applied.ReceivedFrames);
            Assert.Equal(1, applied.AppliedFrames);
            Assert.Equal(0, applied.FailedFrames);

            throwOnPayload = true;
            sendFault.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () =>
                    {
                        subscriptions.Pump(maxFrames: 8);
                        return subscriptions.TryGetContractSnapshot(
                                   in route,
                                   out var snapshot)
                               && snapshot.FailedFrames == 1;
                    },
                    TimeSpan.FromSeconds(3)),
                "the generated apply failure was not observed");
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var faulted));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Faulted,
                faulted.State);
            Assert.Equal(2, faulted.ReceivedFrames);
            Assert.Equal(1, faulted.AppliedFrames);
            Assert.Equal(1, faulted.FailedFrames);

            first.Lease.Dispose();
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var shared));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Faulted,
                shared.State);
            Assert.Equal(1, shared.ActiveLeases);
            Assert.False(
                unregisterSeen.Wait(TimeSpan.FromMilliseconds(100)),
                "releasing one shared subscriber removed the physical lease");
            second.Lease.Dispose();
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var stopped));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Stopped,
                stopped.State);
            Assert.Equal(0, stopped.ActiveLeases);
            var observed = subscriptions.GetObservationSnapshot();
            Assert.Equal(0, observed.ObservedContracts);
            Assert.Equal(0, observed.PendingContracts);
            Assert.True(
                unregisterSeen.Wait(TimeSpan.FromSeconds(3)));
            peer.AssertCompleted();
        }

        [Fact]
        public void GeneratedSubscriptionObservesRejectedAndUnavailableContracts()
        {
            var providerId = new FoxRunTransportId(
                "unity2foxglove.ros2bridge.generated-observation");
            using var runtime = new Ros2BridgeRuntime(
                "127.0.0.1",
                port: 1,
                queueCapacity: 2,
                reconnectIntervalMs: 10000,
                sendTimeoutMs: 1000,
                sinkFactory: null,
                retirementOwner:
                    FoxRunTransportRetirementOwner.CreateForTests(2),
                providerId: providerId,
                direction: FoxRunTransportDirection.Publish,
                generation: 11,
                joinTimeoutMs: 1000,
                requiresSubscription: true);
            using var subscriptions =
                new Ros2BridgeGeneratedSubscriptionRuntime(
                    runtime,
                    providerId,
                    generation: 11);
            var unavailableRoute = new FoxRunTransportSubscribeRoute(
                "phase186/generated/unavailable",
                "/phase186/generated/unavailable",
                "phase186_msgs/msg/Unavailable",
                maxPayloadBytes: 32,
                FoxRunDeliveryPolicy.ProviderDefault,
                (_, _, _) => { },
                messageEncoding: "cdr");
            var unavailable = subscriptions.Subscribe(in unavailableRoute);
            Assert.Equal(
                FoxRunTransportRouteResultState.Unavailable,
                unavailable.State);
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in unavailableRoute,
                    out var unavailableSnapshot));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Unavailable,
                unavailableSnapshot.State);
            Assert.Equal(1, unavailableSnapshot.Attempts);
            Assert.NotEmpty(unavailableSnapshot.LastReason);

            var rejectedRoute = new FoxRunTransportSubscribeRoute(
                "phase186/generated/rejected",
                "/phase186/generated/rejected",
                "phase186_msgs/msg/Rejected",
                maxPayloadBytes: 32,
                FoxRunDeliveryPolicy.ProviderDefault,
                (_, _, _) => { },
                messageEncoding: "json");
            var rejected = subscriptions.Subscribe(in rejectedRoute);
            Assert.Equal(
                FoxRunTransportRouteResultState.Rejected,
                rejected.State);
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in rejectedRoute,
                    out var rejectedSnapshot));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Rejected,
                rejectedSnapshot.State);
            Assert.Equal(1, rejectedSnapshot.RejectedAttempts);
            Assert.True(subscriptions.ObservedContractCount <= 64);
        }

    }
}
