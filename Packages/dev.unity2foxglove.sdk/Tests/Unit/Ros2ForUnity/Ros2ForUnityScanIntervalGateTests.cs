// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Executes the native discovery cadence at long Unity uptime.

using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace FoxgloveSdk.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "187")]
    [Trait("Domain", "LongUptime")]
    public sealed class Ros2ForUnityScanIntervalGateTests
    {
        [Fact]
        public void NativeScanGateRetainsHalfSecondCadenceAtFloatPrecisionBoundary()
        {
            const double start = 8_388_608D;
            var nextScanAt = 0D;

            Assert.True(Ros2ForUnityNativeScanGate.TryAdvance(start, ref nextScanAt));
            Assert.Equal(start + 0.5D, nextScanAt);
            Assert.False(Ros2ForUnityNativeScanGate.TryAdvance(start + 0.25D, ref nextScanAt));
            Assert.True(Ros2ForUnityNativeScanGate.TryAdvance(start + 0.5D, ref nextScanAt));
            Assert.Equal(start + 1D, nextScanAt);
            Assert.False(Ros2ForUnityNativeScanGate.TryAdvance(start + 0.75D, ref nextScanAt));
        }

        [Fact]
        public void NativePublisherRetryGateSuppressesHotPathAttemptsUntilCooldown()
        {
            var nextAttemptAt = 0D;
            const double failureAt = 8_388_608D;

            Assert.True(Ros2ForUnityNativePublisherRetryGate.CanAttempt(failureAt, nextAttemptAt));
            Ros2ForUnityNativePublisherRetryGate.RecordFailure(failureAt, ref nextAttemptAt);

            Assert.False(Ros2ForUnityNativePublisherRetryGate.CanAttempt(failureAt, nextAttemptAt));
            Assert.False(Ros2ForUnityNativePublisherRetryGate.CanAttempt(
                failureAt + Ros2ForUnityNativePublisherRetryGate.CooldownSeconds - 0.001D,
                nextAttemptAt));
            Assert.True(Ros2ForUnityNativePublisherRetryGate.CanAttempt(
                failureAt + Ros2ForUnityNativePublisherRetryGate.CooldownSeconds,
                nextAttemptAt));

            Ros2ForUnityNativePublisherRetryGate.Reset(ref nextAttemptAt);
            Assert.True(Ros2ForUnityNativePublisherRetryGate.CanAttempt(0D, nextAttemptAt));
        }

        [Fact]
        public void ImuBridgeRechecksRuntimeHealthOnItsBoundedScanCadence()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs");
            var syntax = CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(preprocessorSymbols: new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" }))
                .GetRoot();
            var update = syntax.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "Update");
            var invocations = update.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .ToArray();
            var scan = invocations.Single(invocation => invocation.Expression.ToString().EndsWith("TryAdvance"));
            var refresh = invocations.Single(invocation => invocation.Expression.ToString() == "RefreshBindings");

            Assert.Contains(
                invocations,
                invocation => invocation.Expression.ToString() == "EnsureRos2UnityReady"
                              && invocation.SpanStart > scan.SpanStart
                              && invocation.SpanStart < refresh.SpanStart);
        }
    }
}
