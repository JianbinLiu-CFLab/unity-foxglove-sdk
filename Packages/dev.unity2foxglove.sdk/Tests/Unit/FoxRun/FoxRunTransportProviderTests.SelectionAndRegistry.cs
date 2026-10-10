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
        [Fact]
        public void TransportIdIsValidatedImmutableAndOrdinal()
        {
            var id = new FoxRunTransportId("unity2foxglove.example-provider");

            Assert.Equal("unity2foxglove.example-provider", id.Value);
            Assert.Equal(id, new FoxRunTransportId("unity2foxglove.example-provider"));
            Assert.NotEqual(id, new FoxRunTransportId("unity2foxglove.other"));
            Assert.Equal(id.GetHashCode(), new FoxRunTransportId(id.Value).GetHashCode());

            foreach (var invalid in new[]
                     {
                         null,
                         string.Empty,
                         " ",
                         "single",
                         ".leading",
                         "trailing.",
                         "double..dot",
                         "Upper.case",
                         "white space.id",
                         "slash/id",
                         "segment.-bad",
                         "segment.bad-"
                     })
            {
                Assert.ThrowsAny<ArgumentException>(() => new FoxRunTransportId(invalid));
            }
        }

        [Fact]
        public void BuiltInIdAndCapabilityBitsAreStable()
        {
            Assert.Equal("foxglove.websocket", FoxgloveWebSocketTransport.Id);
            Assert.Equal(1, (int)FoxRunTransportCapabilities.Publish);
            Assert.Equal(2, (int)FoxRunTransportCapabilities.Subscribe);
            Assert.Equal(
                3,
                (int)(FoxRunTransportCapabilities.Publish
                      | FoxRunTransportCapabilities.Subscribe));
        }

        [Fact]
        public void SelectionCanonicalizesPublishIdsAndKeepsSubscribeScalar()
        {
            var selection = new FoxRunTransportSelection(
                new[]
                {
                    "unity2foxglove.zeta",
                    FoxgloveWebSocketTransport.Id,
                    "unity2foxglove.alpha"
                },
                subscriptionsEnabled: true,
                subscribeTransportId: "unity2foxglove.alpha");

            Assert.Equal(
                new[]
                {
                    FoxgloveWebSocketTransport.Id,
                    "unity2foxglove.alpha",
                    "unity2foxglove.zeta"
                },
                selection.PublishTransportIds.Select(id => id.Value));
            Assert.True(selection.SubscriptionsEnabled);
            Assert.Equal(
                "unity2foxglove.alpha",
                selection.SubscribeTransportId.Value.Value);

            Assert.Throws<ArgumentException>(() => new FoxRunTransportSelection(
                new[] { FoxgloveWebSocketTransport.Id, FoxgloveWebSocketTransport.Id },
                subscriptionsEnabled: false,
                subscribeTransportId: null));
            Assert.Throws<ArgumentException>(() => new FoxRunTransportSelection(
                Array.Empty<string>(),
                subscriptionsEnabled: true,
                subscribeTransportId: null));
        }

        [Fact]
        public void SerializedSelectionCanFailClosedWithoutCouplingPublishToSubscribe()
        {
            Assert.False(FoxRunTransportSelection.TryCreate(
                new[] { FoxgloveWebSocketTransport.Id },
                subscriptionsEnabled: true,
                subscribeTransportId: string.Empty,
                out var invalid,
                out var reason));
            Assert.Null(invalid);
            Assert.Contains("requires exactly one transport ID", reason);

            Assert.True(FoxRunTransportSelection.TryCreate(
                new[] { FoxgloveWebSocketTransport.Id },
                subscriptionsEnabled: false,
                subscribeTransportId: null,
                out var publishOnly,
                out reason));
            Assert.Empty(reason);
            Assert.Equal(
                FoxgloveWebSocketTransport.TransportId,
                Assert.Single(publishOnly.PublishTransportIds));
        }

        [Fact]
        public void RegistryIsManagerLocalIdempotentAndConflictOrderIndependent()
        {
            var registryA = new FoxRunTransportProviderRegistry();
            var registryB = new FoxRunTransportProviderRegistry();
            var registryC = new FoxRunTransportProviderRegistry();
            var first = new FakeProvider(
                "unity2foxglove.shared",
                FoxRunTransportCapabilities.Publish | FoxRunTransportCapabilities.Subscribe);
            var second = new FakeProvider(
                "unity2foxglove.shared",
                FoxRunTransportCapabilities.Publish | FoxRunTransportCapabilities.Subscribe);

            Assert.Equal(FoxRunTransportRegistrationResult.Added, registryA.Register(first));
            Assert.Equal(FoxRunTransportRegistrationResult.AlreadyRegistered, registryA.Register(first));
            Assert.Equal(FoxRunTransportRegistrationResult.Conflict, registryA.Register(second));
            Assert.Equal(FoxRunTransportProviderResolutionState.Conflicted,
                registryA.Resolve(first.Id, FoxRunTransportCapabilities.Publish).State);
            Assert.Equal(FoxRunTransportRegistrationResult.Added, registryB.Register(second));
            Assert.Equal(FoxRunTransportRegistrationResult.Conflict, registryB.Register(first));
            Assert.Equal(FoxRunTransportProviderResolutionState.Conflicted,
                registryB.Resolve(first.Id, FoxRunTransportCapabilities.Publish).State);
            Assert.Equal(FoxRunTransportProviderResolutionState.Absent,
                registryC.Resolve(first.Id, FoxRunTransportCapabilities.Publish).State);

            var conflictedSelection = new FoxRunTransportSelection(
                new[] { first.Id.Value },
                subscriptionsEnabled: false,
                subscribeTransportId: null);
            Assert.False(registryA.TryCaptureSession(
                conflictedSelection,
                generation: 1,
                out _,
                out var conflictFailure));
            Assert.Equal(FoxRunTransportSessionCaptureFailure.Conflict, conflictFailure.Code);

            Assert.True(registryA.Unregister(second));
            Assert.Equal(FoxRunTransportProviderResolutionState.Sole,
                registryA.Resolve(first.Id, FoxRunTransportCapabilities.Publish).State);
            Assert.True(registryA.TryCaptureSession(
                conflictedSelection,
                generation: 2,
                out var frozen,
                out _));
            Assert.Same(first.LastCapturedSession, frozen.PublishTransports.Single());
            Assert.True(frozen.TryGetPublishTransport(first.Id, out var selected));
            Assert.Same(first.LastCapturedSession, selected);
            Assert.False(frozen.TryGetPublishTransport(
                new FoxRunTransportId("unity2foxglove.missing"),
                out _));

            Assert.True(registryA.Unregister(first));
            Assert.Equal(FoxRunTransportProviderResolutionState.Absent,
                registryA.Resolve(first.Id, FoxRunTransportCapabilities.Publish).State);
            Assert.Same(first.LastCapturedSession, frozen.PublishTransports.Single());
            frozen.Dispose();
            Assert.True(first.LastCapturedSession.Disposed);

            Assert.True(registryB.Unregister(first));
            Assert.Equal(FoxRunTransportProviderResolutionState.Sole,
                registryB.Resolve(second.Id, FoxRunTransportCapabilities.Publish).State);
        }

        [Fact]
        public void CapturedPublishTransportIdsRemainFrozenWithSession()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var alpha = new FakeProvider(
                "unity2foxglove.alpha",
                FoxRunTransportCapabilities.Publish);
            var bravo = new FakeProvider(
                "unity2foxglove.bravo",
                FoxRunTransportCapabilities.Publish);
            registry.Register(alpha);
            registry.Register(bravo);
            var configuredIds = new[] { alpha.Id.Value };
            var selection = new FoxRunTransportSelection(
                configuredIds,
                subscriptionsEnabled: false,
                subscribeTransportId: null);

            Assert.True(registry.TryCaptureSession(
                selection,
                generation: 186,
                out var snapshot,
                out _));

            configuredIds[0] = bravo.Id.Value;
            registry.Unregister(alpha);
            Assert.Equal(
                new[] { alpha.Id.Value },
                snapshot.PublishTransportIds.Select(id => id.Value));
            Assert.Same(
                alpha.LastCapturedSession,
                snapshot.PublishTransports.Single());
            snapshot.Dispose();

            registry.Register(alpha);
            var recapturedSelection = new FoxRunTransportSelection(
                new[] { bravo.Id.Value },
                subscriptionsEnabled: false,
                subscribeTransportId: null);
            Assert.True(registry.TryCaptureSession(
                recapturedSelection,
                generation: 187,
                out var recaptured,
                out _));
            Assert.Equal(
                new[] { bravo.Id.Value },
                recaptured.PublishTransportIds.Select(id => id.Value));
            Assert.Same(bravo.LastCapturedSession, recaptured.PublishTransports.Single());
            Assert.NotSame(alpha.LastCapturedSession, recaptured.PublishTransports.Single());
            recaptured.Dispose();
        }

        [Fact]
        public void CaptureFailsClosedForMissingUnavailableOrCapabilityMismatch()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var publishOnly = new FakeProvider(
                "unity2foxglove.publish-only",
                FoxRunTransportCapabilities.Publish);
            var unavailable = new FakeProvider(
                "unity2foxglove.unavailable",
                FoxRunTransportCapabilities.Publish,
                FoxRunTransportLifecycleState.Unavailable);
            registry.Register(publishOnly);
            registry.Register(unavailable);

            AssertCaptureFailure(
                registry,
                new FoxRunTransportSelection(
                    new[] { "unity2foxglove.missing" },
                    false,
                    null),
                FoxRunTransportSessionCaptureFailure.Missing);
            AssertCaptureFailure(
                registry,
                new FoxRunTransportSelection(
                    new[] { unavailable.Id.Value },
                    false,
                    null),
                FoxRunTransportSessionCaptureFailure.Unavailable);
            AssertCaptureFailure(
                registry,
                new FoxRunTransportSelection(
                    Array.Empty<string>(),
                    true,
                    publishOnly.Id.Value),
                FoxRunTransportSessionCaptureFailure.CapabilityMismatch);

            Assert.Equal(0, publishOnly.CaptureCount);
            Assert.Equal(0, unavailable.CaptureCount);
        }

        [Fact]
        public void ResolveRevalidatesProviderAfterReentrantMetadataMutation()
        {
            var registry = new FoxRunTransportProviderRegistry();
            ReentrantMutationProvider provider = null;
            provider = new ReentrantMutationProvider(
                new FoxRunTransportId("phase187.h01.009"),
                () => registry.Unregister(provider));
            Assert.Equal(
                FoxRunTransportRegistrationResult.Added,
                registry.Register(provider));

            var resolution = registry.Resolve(
                provider.Id,
                FoxRunTransportCapabilities.Publish);

            Assert.Equal(
                FoxRunTransportProviderResolutionState.Conflicted,
                resolution.State);
        }

        [Fact]
        public void ManagerRejectsSuccessfulSchemaResolutionWithInvalidContribution()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs");
            Assert.Contains("try", source, StringComparison.Ordinal);
            Assert.Contains("string.IsNullOrWhiteSpace(contribution.StableSchemaId)", source, StringComparison.Ordinal);
            Assert.Contains("catch (Exception", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ZeroPublishRoutesAndIndependentSubscriptionAreSupported()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var provider = new FakeProvider(
                "unity2foxglove.subscribe",
                FoxRunTransportCapabilities.Subscribe);
            registry.Register(provider);

            var selection = new FoxRunTransportSelection(
                Array.Empty<string>(),
                subscriptionsEnabled: true,
                subscribeTransportId: provider.Id.Value);
            Assert.True(registry.TryCaptureSession(selection, 7, out var snapshot, out _));
            Assert.Empty(snapshot.PublishTransports);
            Assert.NotNull(snapshot.SubscribeTransport);
            Assert.Equal(7UL, snapshot.Generation);
            snapshot.Dispose();
        }

    }
}
