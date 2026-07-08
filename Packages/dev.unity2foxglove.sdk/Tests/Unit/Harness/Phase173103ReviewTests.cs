// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-103")]
    public sealed class Phase173103ReviewTests
    {
        [Fact]
        public void RemoteGatewayMirrorSinkUsesAtomicDisposeGuard()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.remotegateway.win64/Runtime/RemoteGatewayMirrorSink.cs");

            Assert.Contains("private int _disposed", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.CompareExchange(ref _disposed, 1, 0)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("private bool _disposed", source, StringComparison.Ordinal);
        }

        [Fact]
        public void FoxRunSchemaContractInfoNormalizesInvalidRates()
        {
            var contract = new FoxRunSchemaContractInfo(
                "Type",
                "/topic",
                "schema",
                "json",
                "contract",
                "binding",
                "policy",
                "FixedRate",
                float.NaN,
                0f,
                0f,
                Array.Empty<FoxRunSchemaFieldInfo>());

            Assert.Equal(0f, contract.RateHz);
        }

        [Fact]
        public void FullDemoTestLogUsesSerializedTrackedCubeWithWarningFallback()
        {
            var source = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/TestLog.cs");

            Assert.Contains("[SerializeField] private Transform _trackedCube;", source, StringComparison.Ordinal);
            Assert.Contains("publishing this transform instead", source, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraRawImageFrameBuilderChecksRgb24LengthWithLongArithmetic()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraRawImageFrameBuilder.cs");

            Assert.Contains("CheckedRgb24ByteLength", source, StringComparison.Ordinal);
            Assert.Contains("(long)width * height * 3L", source, StringComparison.Ordinal);
            Assert.Contains("byteLength > int.MaxValue", source, StringComparison.Ordinal);
        }

        [Fact]
        public void TlsOptionsDocumentsManagedPasswordLifetime()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Transport/Security/FoxgloveTlsOptions.cs");

            Assert.Contains("stores the", source, StringComparison.Ordinal);
            Assert.Contains("password as a managed string", source, StringComparison.Ordinal);
            Assert.Contains("remain in memory", source, StringComparison.Ordinal);
        }

        [Fact]
        public void BackgroundWorkerLifecycleOwnsDisposableWaitHandle()
        {
            var lifecycle = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundWorkerLifecycle.cs");
            var pipeline = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundEncodePipeline.cs");

            Assert.Contains("internal sealed class BackgroundWorkerLifecycle : IDisposable", lifecycle, StringComparison.Ordinal);
            Assert.Contains("Idle.Dispose();", lifecycle, StringComparison.Ordinal);
            Assert.Contains("_worker.Dispose();", pipeline, StringComparison.Ordinal);
        }
    }
}
