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
        public void ConnectedSocketIsNotSubscribeDecodeReadiness()
        {
            var stats = new Ros2BridgeStatsSnapshot(
                enabled: true,
                connected: true,
                connecting: false,
                queuedFrames: 0,
                sentFrames: 0,
                droppedFrames: 0,
                failedFrames: 0,
                lastError: string.Empty,
                lastConnectedUnixMs: 186,
                lastDisconnectedUnixMs: 0);

            var status = Ros2BridgeTransportStatusMapper.Create(
                generation: 186,
                FoxRunTransportCapabilities.Publish
                | FoxRunTransportCapabilities.Subscribe,
                Ros2BridgeRuntimeLifecycleState.Ready,
                stats,
                hasInboundPipeline: true,
                Ros2BridgePublisherObservationSnapshot.Empty,
                new Ros2BridgeSubscriptionObservationSnapshot(
                    observedContracts: 1,
                    activeContracts: 0,
                    pendingContracts: 1,
                    unavailableContracts: 0,
                    rejectedContracts: 0,
                    faultedContracts: 0,
                    lastReason: "awaiting decoded binding"));

            Assert.Equal(FoxRunTransportObservedState.Ready, status.Publish.State);
            Assert.Equal(FoxRunTransportObservedState.Starting, status.Subscribe.State);
            Assert.Equal(FoxRunTransportObservedState.Degraded, status.State);
            Assert.False(status.Subscribe.IsReady);
            Assert.Contains(
                status.Diagnostics,
                diagnostic => diagnostic.Code == "ROS2BRIDGE005");
        }

        [Fact]
        public void InboundDeliveryFailuresDegradeAnOtherwiseReadySubscription()
        {
            var stats = new Ros2BridgeStatsSnapshot(
                enabled: true,
                connected: true,
                connecting: false,
                queuedFrames: 0,
                sentFrames: 0,
                droppedFrames: 0,
                failedFrames: 0,
                lastError: string.Empty,
                lastConnectedUnixMs: 186,
                lastDisconnectedUnixMs: 0);
            var inbound = new Ros2BridgeInboundStatsSnapshot(
                received: 3,
                accepted: 3,
                replaced: 0,
                dropped: 0,
                applied: 0,
                rejectedAfterStop: 0,
                sequenceGaps: 0,
                staleSequences: 0,
                oversize: 0,
                decodeFailures: 3,
                disposalFailures: 0,
                queuedFrames: 0,
                queuedBytes: 0,
                transientBytes: 0,
                inFlightBytes: 0,
                lastDiagnostic: "CDR shape mismatch",
                resolutionRejections: 0,
                hasSessionDeliveryFailure: true);
            var subscription = new Ros2BridgeSubscriptionObservationSnapshot(
                observedContracts: 1,
                activeContracts: 1,
                pendingContracts: 0,
                unavailableContracts: 0,
                rejectedContracts: 0,
                faultedContracts: 0,
                lastReason: string.Empty);
            var status = Ros2BridgeTransportStatusMapper.Create(
                186UL,
                FoxRunTransportCapabilities.Subscribe,
                Ros2BridgeRuntimeLifecycleState.Ready,
                stats,
                true,
                Ros2BridgePublisherObservationSnapshot.Empty,
                subscription,
                inbound);

            Assert.Equal(
                FoxRunTransportObservedState.Degraded,
                status.Subscribe.State);
            var diagnostic = Assert.Single(status.Diagnostics);
            Assert.Equal("ROS2BRIDGE008", diagnostic.Code);
            Assert.Contains("CDR shape mismatch", diagnostic.Message);

            var replacementOnly = new Ros2BridgeInboundStatsSnapshot(
                received: 3,
                accepted: 3,
                replaced: 2,
                dropped: 0,
                applied: 1,
                rejectedAfterStop: 0,
                sequenceGaps: 0,
                staleSequences: 0,
                oversize: 0,
                decodeFailures: 0,
                disposalFailures: 0,
                queuedFrames: 0,
                queuedBytes: 0,
                transientBytes: 0,
                inFlightBytes: 0,
                lastDiagnostic: "latest-only coalescing",
                resolutionRejections: 0,
                hasSessionDeliveryFailure: false);
            var replacementStatus = Ros2BridgeTransportStatusMapper.Create(
                186UL,
                FoxRunTransportCapabilities.Subscribe,
                Ros2BridgeRuntimeLifecycleState.Ready,
                stats,
                true,
                Ros2BridgePublisherObservationSnapshot.Empty,
                subscription,
                replacementOnly);

            Assert.Equal(
                FoxRunTransportObservedState.Ready,
                replacementStatus.Subscribe.State);
            Assert.Empty(replacementStatus.Diagnostics);

            var recoveredSession = new Ros2BridgeInboundStatsSnapshot(
                received: 8,
                accepted: 5,
                replaced: 0,
                dropped: 1,
                applied: 4,
                rejectedAfterStop: 0,
                sequenceGaps: 2,
                staleSequences: 0,
                oversize: 0,
                decodeFailures: 1,
                disposalFailures: 0,
                queuedFrames: 0,
                queuedBytes: 0,
                transientBytes: 0,
                inFlightBytes: 0,
                lastDiagnostic: string.Empty,
                resolutionRejections: 0,
                hasSessionDeliveryFailure: false);
            var recoveredStatus = Ros2BridgeTransportStatusMapper.Create(
                186UL,
                FoxRunTransportCapabilities.Subscribe,
                Ros2BridgeRuntimeLifecycleState.Ready,
                stats,
                true,
                Ros2BridgePublisherObservationSnapshot.Empty,
                subscription,
                recoveredSession);

            Assert.Equal(
                FoxRunTransportObservedState.Ready,
                recoveredStatus.Subscribe.State);
            Assert.Empty(recoveredStatus.Diagnostics);
        }

        [Fact]
        public void ObservedDisconnectMapsToBoundedStableReconnectingStatus()
        {
            var stats = new Ros2BridgeStatsSnapshot(
                enabled: true,
                connected: false,
                connecting: false,
                queuedFrames: 0,
                sentFrames: 0,
                droppedFrames: 0,
                failedFrames: 1,
                lastError: new string('x', 700),
                lastConnectedUnixMs: 185,
                lastDisconnectedUnixMs: 186);

            var status = Ros2BridgeTransportStatusMapper.Create(
                generation: 187,
                FoxRunTransportCapabilities.Publish,
                Ros2BridgeRuntimeLifecycleState.Ready,
                stats,
                hasInboundPipeline: false,
                Ros2BridgePublisherObservationSnapshot.Empty,
                Ros2BridgeSubscriptionObservationSnapshot.Empty);

            Assert.Equal(
                FoxRunTransportObservedState.Reconnecting,
                status.Publish.State);
            Assert.Equal(
                FoxRunTransportObservedState.Reconnecting,
                status.State);
            var diagnostic = Assert.Single(status.Diagnostics);
            Assert.Equal("ROS2BRIDGE002", diagnostic.Code);
            Assert.Equal(
                FoxRunTransportDiagnostic.MaximumMessageChars,
                diagnostic.Message.Length);
        }

        [Fact]
        public void TerminalOutboundSchedulerCannotMapPublishReady()
        {
            var stats = new Ros2BridgeStatsSnapshot(
                enabled: true,
                connected: true,
                connecting: false,
                queuedFrames: 0,
                sentFrames: 0,
                droppedFrames: 1,
                failedFrames: 1,
                lastError: string.Empty,
                lastConnectedUnixMs: 187,
                lastDisconnectedUnixMs: 0);

            var status = Ros2BridgeTransportStatusMapper.Create(
                generation: 187,
                FoxRunTransportCapabilities.Publish,
                Ros2BridgeRuntimeLifecycleState.Ready,
                stats,
                hasInboundPipeline: false,
                new Ros2BridgePublisherObservationSnapshot(
                    observedContracts: 2,
                    readyContracts: 2,
                    pendingContracts: 0,
                    rejectedContracts: 0,
                    schedulerTerminal: true,
                    lastReason:
                        "ROS2 Bridge outbound scheduler faulted: encoder invariant"),
                Ros2BridgeSubscriptionObservationSnapshot.Empty);

            Assert.Equal(
                FoxRunTransportObservedState.Failed,
                status.Publish.State);
            Assert.False(status.Publish.IsReady);
            Assert.Equal(0, status.Publish.ReadyContractCount);
            Assert.Equal(2, status.Publish.FailedContractCount);
            var diagnostic = Assert.Single(status.Diagnostics);
            Assert.Equal("ROS2BRIDGE007", diagnostic.Code);
            Assert.Contains(
                "encoder invariant",
                diagnostic.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void CleanupAttemptsEveryActionAndPreservesFirstFailure()
        {
            var order = new System.Collections.Generic.List<int>();
            var first = new InvalidOperationException("first");
            var second = new InvalidDataException("second");

            var actual = Ros2BridgeCleanup.RunAll(
                count: 3,
                index =>
                {
                    order.Add(index);
                    if (index == 2)
                        throw first;
                    if (index == 0)
                        throw second;
                },
                reverse: true);

            Assert.Same(first, actual);
            Assert.Equal(new[] { 2, 1, 0 }, order);
        }

    }
}
