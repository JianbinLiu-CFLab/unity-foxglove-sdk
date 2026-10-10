// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks the neutral, Manager-local FoxRun transport provider contract.

using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests
{
    public sealed partial class FoxRunTransportProviderTests
    {
        private static void AssertCaptureFailure(
            FoxRunTransportProviderRegistry registry,
            FoxRunTransportSelection selection,
            FoxRunTransportSessionCaptureFailure expected)
        {
            Assert.False(registry.TryCaptureSession(
                selection,
                generation: 1,
                out _,
                out var failure));
            Assert.Equal(expected, failure.Code);
        }

        [Fact]
        public void RegistryReRegistersTheSameProviderAfterUnregister()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var provider = new ProbeTransportProvider("probe.alpha", FoxRunTransportCapabilities.Publish);

            Assert.Equal(FoxRunTransportRegistrationResult.Added, registry.Register(provider));
            Assert.True(registry.Unregister(provider));
            Assert.Equal(FoxRunTransportRegistrationResult.Added, registry.Register(provider));
            Assert.Equal(
                FoxRunTransportProviderResolutionState.Sole,
                registry.Resolve(provider.Id, FoxRunTransportCapabilities.Publish).State);
        }

        [Fact]
        public void CaptureRejectsAProviderSwappedDuringMetadataRevalidation()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var replacement = new ProbeTransportProvider("probe.swap", FoxRunTransportCapabilities.Publish);
            ProbeTransportProvider original = null;
            original = new ProbeTransportProvider("probe.swap", FoxRunTransportCapabilities.Publish)
            {
                OnLifecycle = () =>
                {
                    registry.Unregister(original);
                    registry.Register(replacement);
                },
            };
            registry.Register(original);

            Assert.False(registry.TryCaptureSession(
                PublishSelection("probe.swap"),
                3,
                out var snapshot,
                out var failure));
            Assert.Null(snapshot);
            Assert.Equal(FoxRunTransportSessionCaptureFailure.Conflict, failure.Code);
            Assert.Equal(0, original.Captures);
            Assert.Equal(0, replacement.Captures);
        }

        [Fact]
        public void CaptureRejectsASessionWithForeignIdentityOrGeneration()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var provider = new ProbeTransportProvider("probe.stale", FoxRunTransportCapabilities.Publish)
            {
                SessionGenerationOverride = 1,
                SessionIdOverride = "probe.other",
            };
            registry.Register(provider);

            Assert.False(registry.TryCaptureSession(
                PublishSelection("probe.stale"),
                9,
                out var snapshot,
                out var failure));
            Assert.Null(snapshot);
            Assert.Equal(FoxRunTransportSessionCaptureFailure.ProviderFailed, failure.Code);
            Assert.True(provider.Last.Disposed);
        }

        [Fact]
        public void CaptureDisposesEarlierSessionsWhenALaterProviderRejects()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var first = new ProbeTransportProvider("probe.first", FoxRunTransportCapabilities.Publish);
            var second = new ProbeTransportProvider("probe.second", FoxRunTransportCapabilities.Publish) { Reject = true };
            registry.Register(first);
            registry.Register(second);

            Assert.False(registry.TryCaptureSession(
                PublishSelection("probe.first", "probe.second"),
                4,
                out var snapshot,
                out var failure));
            Assert.Null(snapshot);
            Assert.Equal(FoxRunTransportSessionCaptureFailure.ProviderRejected, failure.Code);
            Assert.True(first.Last.Disposed);
        }

        [Fact]
        public void CaptureContainsAThrowingProviderMetadataGetter()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var provider = new ProbeTransportProvider("probe.hostile", FoxRunTransportCapabilities.Publish)
            {
                ThrowLifecycle = true,
            };
            registry.Register(provider);

            var captured = true;
            var failure = default(FoxRunTransportSessionCaptureError);
            var thrown = Record.Exception(() => captured = registry.TryCaptureSession(
                PublishSelection("probe.hostile"),
                5,
                out _,
                out failure));

            Assert.Null(thrown);
            Assert.False(captured);
            Assert.Equal(FoxRunTransportSessionCaptureFailure.ProviderFailed, failure.Code);
        }

        [Fact]
        public void ObservedStatusRejectsAForeignProviderIdentity()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var provider = new ProbeTransportProvider("probe.status", FoxRunTransportCapabilities.Publish)
            {
                StatusIdOverride = "probe.impostor",
            };
            registry.Register(provider);
            Assert.True(registry.TryCaptureSession(
                PublishSelection("probe.status"),
                6,
                out var snapshot,
                out _));

            var status = snapshot.CaptureStatuses().Single();

            Assert.Equal("probe.status", status.ProviderId.Value);
            Assert.Equal(FoxRunTransportObservedState.Failed, status.State);
            Assert.Contains(status.Diagnostics, diagnostic => diagnostic.Code == "FOXTRANSPORT002");
        }

        [Fact]
        public void RegisteredCapabilitiesAreFrozenAtRegistration()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var provider = new ProbeTransportProvider("probe.mutable", FoxRunTransportCapabilities.Publish);
            registry.Register(provider);

            provider.CapabilitiesValue = FoxRunTransportCapabilities.Subscribe;

            Assert.Equal(
                FoxRunTransportProviderResolutionState.Sole,
                registry.Resolve(provider.Id, FoxRunTransportCapabilities.Publish).State);
            Assert.Equal(
                FoxRunTransportProviderResolutionState.CapabilityMismatch,
                registry.Resolve(provider.Id, FoxRunTransportCapabilities.Subscribe).State);
        }

        private static FoxRunTransportSelection PublishSelection(params string[] ids)
            => new FoxRunTransportSelection(ids, false, null);

    }
}
