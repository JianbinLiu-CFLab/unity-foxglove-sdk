// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    public sealed partial class Ros2BridgePublisherPreparationTests
    {
        [Fact]
        public void MalformedPreparationResponseRejectsWithoutReconnectLoop()
        {
            var transport = new PreparationTransport("ok")
            {
                MutateResponse = response =>
                    MutateHeader(response, header => header["status"] = true)
            };
            using var runtime = Runtime(
                () => transport,
                reconnectIntervalMs: 100);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/malformed_response",
                    "phase184_msgs/msg/MalformedResponse",
                    FoxRunResolvedQos.Default,
                    out _));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/malformed_response",
                          "phase184_msgs/msg/MalformedResponse",
                          FoxRunResolvedQos.Default,
                          out _)
                      == Ros2BridgePublisherReadiness.Rejected,
                TimeSpan.FromSeconds(3)));
            Thread.Sleep(250);

            Assert.Equal(1, transport.ConnectCount);
            Assert.Equal(1, transport.RequestCount);
        }

        [Fact]
        public void TransportPreparationFailureUsesReconnectBackoffAndBoundsRuntimeDiagnostic()
        {
            var transport = new PreparationTransport("ok")
            {
                PreparationFailure = new EndOfStreamException(new string('x', 4096))
            };
            using var runtime = Runtime(
                () => transport,
                reconnectIntervalMs: 100);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/closed_sidecar",
                    "phase184_msgs/msg/ClosedSidecar",
                    FoxRunResolvedQos.Default,
                    out _));

            string runtimeDiagnostic = null;
            Assert.True(SpinWait.SpinUntil(
                () =>
                {
                    var value = runtime.GetStatsSnapshot().LastError;
                    if (string.IsNullOrEmpty(value))
                        return false;
                    runtimeDiagnostic = value;
                    return true;
                },
                TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => transport.RequestCount >= 2,
                TimeSpan.FromSeconds(3)));
            var requestTimes = transport.RequestTimesSnapshot;

            Assert.True(
                requestTimes[1] - requestTimes[0] >= 75,
                "Preparation retries bypassed the configured reconnect backoff.");
            Assert.InRange(
                runtimeDiagnostic.Length,
                1,
                Ros2BridgeRuntime.MaxRuntimeDiagnosticChars);
        }

        [Fact]
        public void PreparationImplementationTypesStayInternalButTransportCapabilityIsPublic()
        {
            Assert.False(typeof(Ros2BridgePublisherReadiness).IsPublic);
            Assert.False(typeof(Ros2BridgePublisherPreparationRequest).IsPublic);
            Assert.False(typeof(Ros2BridgePublisherPreparationCodec).IsPublic);
            Assert.True(typeof(IRos2BridgePublisherPreparationTransport).IsPublic);
            Assert.True(typeof(Ros2BridgeRuntime)
                .GetMethod(
                    "PreparePublisher",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                ?.IsAssembly);
        }

        [Fact]
        public void FatalStopDuringDisposeStillDisposesOwnedSignalAndPreservesPrimary()
        {
            var sink = new FatalDisconnectSink();
            var runtime = Runtime(() => sink);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(SpinWait.SpinUntil(
                () => runtime.IsConnected,
                TimeSpan.FromSeconds(1)));

            var thrown = Assert.Throws<OutOfMemoryException>(() => runtime.Dispose());

            Assert.Equal("stop-primary", thrown.Message);
            Assert.Equal(1, sink.DisposeCount);
            Assert.Equal(
                Ros2BridgeRuntimeLifecycleState.Stopped,
                runtime.LifecycleState);
            Assert.Throws<ObjectDisposedException>(
                () => runtime.Start(enabled: true, autoConnect: true));
        }

        [Fact]
        public void DisposeIsIdempotentAfterAHealthyStop()
        {
            var runtime = Runtime(() => new TrackingLifecycleSink());

            runtime.Dispose();
            var secondDispose = Record.Exception(() => runtime.Dispose());
            var laterStop = Record.Exception(() => runtime.Stop());

            Assert.Null(secondDispose);
            Assert.Null(laterStop);
        }

        private static Ros2BridgeRuntime Runtime(
            Func<IRos2BridgeSink> factory,
            int queueCapacity = 8,
            int reconnectIntervalMs = 10)
            => new Ros2BridgeRuntime(
                "127.0.0.1",
                19484,
                queueCapacity,
                reconnectIntervalMs,
                sendTimeoutMs: 250,
                factory);
    }
}
