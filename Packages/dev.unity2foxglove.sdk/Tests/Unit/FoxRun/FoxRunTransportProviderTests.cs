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
    public sealed class FoxRunTransportProviderTests
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

        [Fact]
        public void OrdinaryFanoutContinuesAcrossThreeProvidersWhenMiddleProviderFails()
        {
            var calls = new System.Collections.Generic.List<string>();
            var registry = new FoxRunTransportProviderRegistry();
            var alpha = new OrdinaryProvider(
                "unity2foxglove.alpha",
                calls,
                failPublish: false);
            var bravo = new OrdinaryProvider(
                "unity2foxglove.bravo",
                calls,
                failPublish: true);
            var charlie = new OrdinaryProvider(
                "unity2foxglove.charlie",
                calls,
                failPublish: false);
            registry.Register(charlie);
            registry.Register(bravo);
            registry.Register(alpha);
            var selection = new FoxRunTransportSelection(
                new[]
                {
                    charlie.Id.Value,
                    alpha.Id.Value,
                    bravo.Id.Value
                },
                subscriptionsEnabled: false,
                subscribeTransportId: null);

            Assert.True(registry.TryCaptureSession(
                selection,
                generation: 186,
                out var snapshot,
                out _));
            var request = new FoxRunOrdinaryPayloadRequest(
                "ordinary-fixture",
                "/phase186/fanout",
                "Demo.Value",
                value: 42,
                logTimeNs: 186,
                sequence: 1,
                FoxRunDeliveryPolicy.ProviderDefault);

            var result = FoxRunOrdinaryTransportFanout.Publish(
                snapshot.PublishTransports,
                in request);

            Assert.Equal(
                new[]
                {
                    "unity2foxglove.alpha",
                    "unity2foxglove.bravo",
                    "unity2foxglove.charlie"
                },
                calls);
            Assert.Equal(3, result.Matched);
            Assert.Equal(2, result.Accepted);
            Assert.Equal(0, result.Rejected);
            Assert.Equal(0, result.Unavailable);
            Assert.Equal(1, result.Failed);
            Assert.True(result.AnyAccepted);
            Assert.False(result.AllAccepted);
            snapshot.Dispose();
        }

        [Fact]
        public void GeneratedFanoutUsesExplicitRoutesAndClassifiesEverySelectedProvider()
        {
            var calls = new System.Collections.Generic.List<string>();
            var sessions = new IFoxRunTransportSession[]
            {
                new GeneratedSession(
                    "unity2foxglove.alpha",
                    calls,
                    FoxRunTransportPublishResult.Accepted()),
                new GeneratedSession(
                    "unity2foxglove.bravo",
                    calls,
                    FoxRunTransportPublishResult.Failed("fixture")),
                new GeneratedSession(
                    "unity2foxglove.charlie",
                    calls,
                    FoxRunTransportPublishResult.Rejected("fixture"))
            };
            var source = new GeneratedSource();
            var request = new FoxRunGeneratedTransportPublishRequest(
                source,
                topicIndex: 0,
                "/phase186/generated",
                logTimeNs: 186);

            var result = FoxRunGeneratedTransportFanout.Publish(
                sessions,
                explicitTransportIds: new[]
                {
                    "unity2foxglove.charlie",
                    "unity2foxglove.alpha"
                },
                inheritedTransportIds: new[]
                {
                    new FoxRunTransportId("unity2foxglove.bravo")
                },
                in request);

            Assert.Equal(
                new[]
                {
                    "unity2foxglove.alpha",
                    "unity2foxglove.charlie"
                },
                calls);
            Assert.Equal(2, result.Matched);
            Assert.Equal(1, result.Accepted);
            Assert.Equal(1, result.Rejected);
            Assert.Equal(0, result.Unavailable);
            Assert.Equal(0, result.Failed);
            Assert.True(result.AnyAccepted);
            Assert.False(result.AllAccepted);
            Assert.Collection(
                result.TargetResults,
                target =>
                {
                    Assert.Equal(
                        new FoxRunTransportId("unity2foxglove.alpha"),
                        target.TransportId);
                    Assert.Equal(
                        FoxRunTransportRouteResultState.Accepted,
                        target.State);
                    Assert.Equal(string.Empty, target.Reason);
                },
                target =>
                {
                    Assert.Equal(
                        new FoxRunTransportId("unity2foxglove.charlie"),
                        target.TransportId);
                    Assert.Equal(
                        FoxRunTransportRouteResultState.Rejected,
                        target.State);
                    Assert.Equal("fixture", target.Reason);
                });
        }

        [Fact]
        public void GeneratedFanoutSuppressesOnlyExactRemoteProviderGeneration()
        {
            var calls = new System.Collections.Generic.List<string>();
            var sessions = new IFoxRunTransportSession[]
            {
                new GeneratedSession(
                    "unity2foxglove.ros2bridge",
                    calls,
                    FoxRunTransportPublishResult.Accepted(),
                    generation: 17),
                new GeneratedSession(
                    "unity2foxglove.r2fu",
                    calls,
                    FoxRunTransportPublishResult.Accepted(),
                    generation: 17),
            };
            var request = new FoxRunGeneratedTransportPublishRequest(
                new GeneratedSource(),
                topicIndex: 0,
                "/phase186/generated-origin",
                logTimeNs: 186);
            var selected = new[]
            {
                "unity2foxglove.ros2bridge",
                "unity2foxglove.r2fu",
            };

            var exact = FoxRunGeneratedTransportFanout.Publish(
                sessions,
                selected,
                inheritedTransportIds: null,
                in request,
                suppressedTransportId:
                    "unity2foxglove.ros2bridge",
                suppressedGeneration: 17);
            Assert.Equal(
                new[] { "unity2foxglove.r2fu" },
                calls);
            Assert.Equal(1, exact.Matched);
            Assert.Equal(1, exact.Accepted);

            calls.Clear();
            var staleGeneration =
                FoxRunGeneratedTransportFanout.Publish(
                    sessions,
                    selected,
                    inheritedTransportIds: null,
                    in request,
                    suppressedTransportId:
                        "unity2foxglove.ros2bridge",
                    suppressedGeneration: 16);
            Assert.Equal(selected, calls);
            Assert.Equal(2, staleGeneration.Matched);
            Assert.Equal(2, staleGeneration.Accepted);
        }

        [Fact]
        public void ObservedStatusSeparatesDirectionsAndBoundsDiagnostics()
        {
            var longMessage = new string('x', 700);
            var publish = new FoxRunTransportDirectionStatus(
                FoxRunTransportDirection.Publish,
                selected: true,
                FoxRunTransportObservedState.Ready,
                observedContractCount: 2,
                readyContractCount: 2,
                failedContractCount: 0,
                new FoxRunTransportDiagnostic(
                    "FOXTRANSPORT101",
                    longMessage));
            var subscribe = new FoxRunTransportDirectionStatus(
                FoxRunTransportDirection.Subscribe,
                selected: true,
                FoxRunTransportObservedState.Failed,
                observedContractCount: 1,
                readyContractCount: 0,
                failedContractCount: 1,
                new FoxRunTransportDiagnostic(
                    "FOXTRANSPORT102",
                    "decode failed"));
            var diagnostics = Enumerable.Range(
                    0,
                    FoxRunTransportStatusSnapshot.MaximumDiagnostics + 4)
                .Select(index => new FoxRunTransportDiagnostic(
                    index == 0
                        ? "FOXTRANSPORT101"
                        : "FOXTRANSPORT" + (200 + index),
                    "diagnostic-" + index))
                .ToArray();

            var snapshot = new FoxRunTransportStatusSnapshot(
                new FoxRunTransportId("unity2foxglove.status"),
                generation: 42,
                publish,
                subscribe,
                diagnostics);

            Assert.Equal(FoxRunTransportObservedState.Degraded, snapshot.State);
            Assert.True(snapshot.Publish.IsReady);
            Assert.False(snapshot.Subscribe.IsReady);
            Assert.Equal(
                FoxRunTransportStatusSnapshot.MaximumDiagnostics,
                snapshot.Diagnostics.Count);
            Assert.Equal(
                snapshot.Diagnostics.Count,
                snapshot.Diagnostics
                    .Select(diagnostic => diagnostic.Code)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.All(
                snapshot.Diagnostics,
                diagnostic => Assert.InRange(
                    diagnostic.Message.Length,
                    1,
                    FoxRunTransportDiagnostic.MaximumMessageChars));
        }

        [Fact]
        public void ObservedStatusRejectsOverflowedContractCounts()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new FoxRunTransportDirectionStatus(
                    FoxRunTransportDirection.Publish,
                    selected: true,
                    FoxRunTransportObservedState.Degraded,
                    observedContractCount: int.MaxValue,
                    readyContractCount: int.MaxValue,
                    failedContractCount: 1));
        }

        [Fact]
        public void FrozenSessionCapturesOneObservedStatusWithSelectedDirections()
        {
            var registry = new FoxRunTransportProviderRegistry();
            var provider = new FakeProvider(
                "unity2foxglove.observed",
                FoxRunTransportCapabilities.Publish
                | FoxRunTransportCapabilities.Subscribe);
            registry.Register(provider);
            var selection = new FoxRunTransportSelection(
                new[] { provider.Id.Value },
                subscriptionsEnabled: true,
                subscribeTransportId: provider.Id.Value);
            Assert.True(registry.TryCaptureSession(
                selection,
                generation: 43,
                out var frozen,
                out _));

            var status = Assert.Single(frozen.CaptureStatuses());

            Assert.Equal(provider.Id, status.ProviderId);
            Assert.Equal(43UL, status.Generation);
            Assert.Equal(FoxRunTransportObservedState.Ready, status.State);
            Assert.Equal(
                FoxRunTransportCapabilities.Publish
                | FoxRunTransportCapabilities.Subscribe,
                provider.LastCapturedSession.LastStatusDirections);
            frozen.Dispose();
        }

        [Fact]
        public void FrozenSessionFailsClosedWithoutObservedStatusSource()
        {
            var session = new GeneratedSession(
                "unity2foxglove.statusless",
                new System.Collections.Generic.List<string>(),
                FoxRunTransportPublishResult.Accepted(),
                generation: 44);
            using var frozen = new FoxRunTransportSessionSnapshot(
                generation: 44,
                new IFoxRunTransportSession[] { session },
                subscribeTransport: null,
                new IFoxRunTransportSession[] { session });

            var status = Assert.Single(frozen.CaptureStatuses());

            Assert.Equal(FoxRunTransportObservedState.Failed, status.State);
            Assert.Equal(FoxRunTransportObservedState.Failed, status.Publish.State);
            Assert.False(status.Subscribe.Selected);
            Assert.Equal("FOXTRANSPORT001", Assert.Single(status.Diagnostics).Code);
        }

        [Fact]
        public void RetirementCapacityIsPreReservedAndTimeoutConversionAllocatesNothing()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(capacity: 2);
            Assert.True(owner.TryReserve(
                new FoxRunTransportId("unity2foxglove.example"),
                FoxRunTransportDirection.Publish,
                generation: 9,
                workerCount: 2,
                out var reservation));
            Assert.False(owner.TryReserve(
                new FoxRunTransportId("unity2foxglove.other"),
                FoxRunTransportDirection.Subscribe,
                generation: 10,
                workerCount: 1,
                out _));

            var lease = new FakeDetachedLease();
            reservation.WarmUpTimeoutConversionForCurrentThread();
            var before = GC.GetAllocatedBytesForCurrentThread();
            Assert.True(reservation.TryConvertToRetired(
                workerIndex: 0,
                lease,
                workerIdentity: "worker-0",
                retainedBytes: 128,
                retainedResources: 3));
            var after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(before, after);

            Assert.Equal(2, owner.OccupiedCount);
            Assert.Equal(1, owner.RetiredCount);
            var retired = Assert.Single(owner.CaptureRetired());
            Assert.Equal("worker-0", retired.WorkerIdentity);
            Assert.True(retired.Age >= TimeSpan.Zero);
            Assert.True(reservation.TryReturn(workerIndex: 1));
            Assert.Equal(1, owner.OccupiedCount);
            Assert.True(reservation.TryCompleteRetired(workerIndex: 0));
            Assert.True(lease.Disposed);
            Assert.Equal(0, owner.OccupiedCount);
            var finalExit = Assert.Single(owner.CaptureFinalExits());
            Assert.True(finalExit.Succeeded);
            Assert.Equal("FOXTRANSPORTRETIRE001", finalExit.DiagnosticCode);
            Assert.Equal("worker-0", finalExit.WorkerIdentity);
            Assert.True(finalExit.Age >= TimeSpan.Zero);
        }

        [Fact]
        public void ExclusiveRetirementRemainsOccupiedUntilCleanupCompletes()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(capacity: 2);
            var providerId = new FoxRunTransportId("unity2foxglove.exclusive");
            Assert.True(owner.TryReserveExclusive(
                providerId,
                FoxRunTransportDirection.Publish,
                generation: 11,
                workerCount: 1,
                out var reservation));
            Assert.False(owner.TryReserveExclusive(
                providerId,
                FoxRunTransportDirection.Publish,
                generation: 12,
                workerCount: 1,
                out _));

            var lease = new BlockingDetachedLease();
            Assert.True(reservation.TryConvertToRetired(
                workerIndex: 0,
                lease,
                workerIdentity: "worker-0",
                retainedBytes: 64,
                retainedResources: 2));

            Exception completionFailure = null;
            var completion = new System.Threading.Thread(() =>
            {
                try
                {
                    Assert.True(reservation.TryCompleteRetired(workerIndex: 0));
                }
                catch (Exception ex)
                {
                    completionFailure = ex;
                }
            });
            completion.Start();

            Assert.True(lease.DisposeEntered.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(owner.TryReserveExclusive(
                providerId,
                FoxRunTransportDirection.Publish,
                generation: 12,
                workerCount: 1,
                out _));
            Assert.Equal(1, owner.OccupiedCount);
            Assert.Equal(1, owner.RetiredCount);

            lease.AllowDispose.Set();
            Assert.True(completion.Join(TimeSpan.FromSeconds(2)));
            Assert.Null(completionFailure);
            Assert.Equal(0, owner.OccupiedCount);

            Assert.True(owner.TryReserveExclusive(
                providerId,
                FoxRunTransportDirection.Publish,
                generation: 13,
                workerCount: 1,
                out var replacement));
            Assert.True(replacement.TryReturn(workerIndex: 0));
        }

        [Fact]
        public void ExclusiveRetirementCleanupFailureIsObservableAndFinallyReleasesSlot()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(capacity: 1);
            var providerId = new FoxRunTransportId("unity2foxglove.throwing-exclusive");
            Assert.True(owner.TryReserveExclusive(
                providerId,
                FoxRunTransportDirection.Publish,
                generation: 21,
                workerCount: 1,
                out var reservation));
            Assert.True(reservation.TryConvertToRetired(
                workerIndex: 0,
                new ThrowingDetachedLease(),
                workerIdentity: "worker-0",
                retainedBytes: 32,
                retainedResources: 1));

            var failure = Assert.Throws<InvalidOperationException>(
                () => reservation.TryCompleteRetired(workerIndex: 0));

            Assert.Equal("test cleanup failure", failure.Message);
            Assert.Equal(0, owner.OccupiedCount);
            Assert.Equal(0, owner.RetiredCount);
            var finalExit = Assert.Single(owner.CaptureFinalExits());
            Assert.False(finalExit.Succeeded);
            Assert.Equal("FOXTRANSPORTRETIRE002", finalExit.DiagnosticCode);
            Assert.Equal("test cleanup failure", finalExit.Failure);
            Assert.True(owner.TryReserveExclusive(
                providerId,
                FoxRunTransportDirection.Publish,
                generation: 22,
                workerCount: 1,
                out var replacement));
            Assert.True(replacement.TryReturn(workerIndex: 0));
        }

        [Fact]
        public void RetirementFinalExitHistoryIsBoundedToOwnerCapacity()
        {
            var owner = FoxRunTransportRetirementOwner.CreateForTests(capacity: 2);
            var providerId = new FoxRunTransportId("unity2foxglove.history");
            for (var index = 0; index < 3; index++)
            {
                Assert.True(owner.TryReserve(
                    providerId,
                    FoxRunTransportDirection.Subscribe,
                    generation: checked((ulong)(30 + index)),
                    workerCount: 1,
                    out var reservation));
                Assert.True(reservation.TryConvertToRetired(
                    workerIndex: 0,
                    new FakeDetachedLease(),
                    workerIdentity: "worker-" + index,
                    retainedBytes: index,
                    retainedResources: index));
                Assert.True(reservation.TryCompleteRetired(workerIndex: 0));
                reservation.Dispose();
            }

            var exits = owner.CaptureFinalExits();

            Assert.Equal(owner.Capacity, exits.Count);
            Assert.Equal("worker-1", exits[0].WorkerIdentity);
            Assert.Equal("worker-2", exits[1].WorkerIdentity);
        }

        [Fact]
        public void AttributeUsesDirectionSpecificProviderIds()
        {
            var publishProperty = typeof(FoxRunAttribute).GetProperty("PublishTransportIds");
            var subscribeProperty = typeof(FoxRunAttribute).GetProperty("SubscribeTransportId");

            Assert.NotNull(publishProperty);
            Assert.Equal(typeof(string[]), publishProperty.PropertyType);
            Assert.NotNull(subscribeProperty);
            Assert.Equal(typeof(string), subscribeProperty.PropertyType);
        }

        [Fact]
        public void DeclarationRoutingIsDirectionLegalAndHashesCanonicalIds()
        {
            Assert.Throws<ArgumentException>(() => new FoxRunTransportDeclaration(
                FoxRunFlow.Publish,
                publishTransportIds: null,
                subscribeTransportId: FoxgloveWebSocketTransport.Id));
            Assert.Throws<ArgumentException>(() => new FoxRunTransportDeclaration(
                FoxRunFlow.Subscribe,
                publishTransportIds: new[] { FoxgloveWebSocketTransport.Id },
                subscribeTransportId: null));
            Assert.Throws<ArgumentException>(() => new FoxRunTransportDeclaration(
                FoxRunFlow.Publish,
                publishTransportIds: Array.Empty<string>(),
                subscribeTransportId: null));

            var inherited = new FoxRunTransportSelection(
                new[]
                {
                    "unity2foxglove.zeta",
                    FoxgloveWebSocketTransport.Id
                },
                subscriptionsEnabled: true,
                subscribeTransportId: FoxgloveWebSocketTransport.Id);
            var first = new FoxRunTransportDeclaration(
                    FoxRunFlow.PublishAndSubscribe,
                    publishTransportIds: new[]
                    {
                        "unity2foxglove.zeta",
                        FoxgloveWebSocketTransport.Id
                    },
                    subscribeTransportId: FoxgloveWebSocketTransport.Id)
                .Resolve(
                    inherited,
                    FoxRunEncoding.MessagePack,
                    FoxRunEncoding.Protobuf);
            var second = new FoxRunTransportDeclaration(
                    FoxRunFlow.PublishAndSubscribe,
                    publishTransportIds: new[]
                    {
                        FoxgloveWebSocketTransport.Id,
                        "unity2foxglove.zeta"
                    },
                    subscribeTransportId: FoxgloveWebSocketTransport.Id)
                .Resolve(
                    inherited,
                    FoxRunEncoding.MessagePack,
                    FoxRunEncoding.Protobuf);

            Assert.Equal(FoxRunEncoding.MessagePack, first.PublishEncoding);
            Assert.Equal(FoxRunEncoding.Protobuf, first.SubscribeEncoding);
            Assert.Equal(first.DeterministicHash, second.DeterministicHash);
            Assert.Equal(first.DeterministicKey, second.DeterministicKey);
        }

        [Fact]
        public void RuntimeRegistryHasNoStaticProviderCollection()
        {
            var forbidden = typeof(FoxRunTransportProviderRegistry)
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field =>
                    typeof(IFoxRunTransportProvider).IsAssignableFrom(field.FieldType)
                    || field.FieldType.Name.Contains("Dictionary", StringComparison.Ordinal)
                    || field.FieldType.Name.Contains("List", StringComparison.Ordinal))
                .ToArray();

            Assert.Empty(forbidden);
        }

        [Fact]
        public void GenerationHostsPreserveCanonicalDirectionSpecificTransportIds()
        {
            var presence =
                FoxRunNamedArgumentPresence.PublishTransportIds
                | FoxRunNamedArgumentPresence.SubscribeTransportId;
            var publishIds = new[]
            {
                "unity2foxglove.zeta",
                FoxgloveWebSocketTransport.Id
            };
            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo",
                    "Source",
                    "Value",
                    "field",
                    "System.Int32",
                    "global::System.Int32",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/demo/value",
                    schemaName: "Demo.Value",
                    hz: 10f,
                    policy: (int)FoxRunPolicy.FixedRate,
                    tolerance: 0f,
                    rawMemberOrder: 1,
                    conditionalSymbols: "",
                    mode: (int)FoxRunFlow.PublishAndSubscribe,
                    publishTransportIds: publishIds,
                    subscribeTransportId: "unity2foxglove.alpha",
                    namedArgumentPresence: presence)
            });
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo",
                    "Source",
                    "Value",
                    "field",
                    "System.Int32",
                    "global::System.Int32",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/demo/value",
                    schemaName: "Demo.Value",
                    hz: 10f,
                    policy: (int)FoxRunPolicy.FixedRate,
                    tolerance: 0f,
                    rawMemberOrder: 1,
                    conditionalSymbols: "",
                    mode: (int)FoxRunFlow.PublishAndSubscribe,
                    publishTransportIds: publishIds.Reverse().ToArray(),
                    subscribeTransportId: "unity2foxglove.alpha",
                    namedArgumentPresence: presence)
            });

            var roslynMember = Assert.Single(Assert.Single(roslyn.Types).Members);
            var reflectionMember = Assert.Single(Assert.Single(reflection.Types).Members);
            Assert.Equal(
                new[]
                {
                    FoxgloveWebSocketTransport.Id,
                    "unity2foxglove.zeta"
                },
                roslynMember.PublishTransportIds);
            Assert.Equal(
                roslynMember.PublishTransportIds,
                reflectionMember.PublishTransportIds);
            Assert.Equal(
                "unity2foxglove.alpha",
                roslynMember.SubscribeTransportId);
            Assert.Equal(
                roslynMember.SubscribeTransportId,
                reflectionMember.SubscribeTransportId);
            var comparison =
                FoxRunGenerationDescriptorComparer.Compare(roslyn, reflection);
            Assert.True(
                comparison.IsSemanticEqual,
                string.Join(Environment.NewLine, comparison.SemanticDifferences));

            var json = FoxRunGenerationDescriptorJsonWriter.Write(roslyn);
            Assert.Contains("\"descriptorVersion\":6", json);
            Assert.Contains(
                "\"publishTransportIds\":[\"foxglove.websocket\",\"unity2foxglove.zeta\"]",
                json);
            Assert.Contains(
                "\"subscribeTransportId\":\"unity2foxglove.alpha\"",
                json);
            Assert.Contains(
                "\"explicitArguments\":\"PublishTransportIds,SubscribeTransportId\"",
                json);

            var roslynManifest = FoxRunManifestBuilder.Build(
                roslyn.Types.Single().Members
                    .Select(FoxRunManifestMember.FromGenerationMember)
                    .ToArray(),
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var reflectionManifest = FoxRunManifestBuilder.Build(
                reflection.Types.Single().Members
                    .Select(FoxRunManifestMember.FromGenerationMember)
                    .ToArray(),
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var contract = Assert.Single(
                Assert.Single(roslynManifest.Sections.FoxRun.Types).Contracts,
                candidate =>
                    candidate.Encoding
                    == FoxRunGenerationDescriptorConstants.JsonEncoding);
            Assert.True(contract.IncludesTransportSelection);
            Assert.Equal(
                new[]
                {
                    FoxgloveWebSocketTransport.Id,
                    "unity2foxglove.zeta"
                },
                contract.PublishTransportIds);
            Assert.Equal(
                "unity2foxglove.alpha",
                contract.SubscribeTransportId);
            Assert.Equal(
                roslynManifest.GlobalManifestHash,
                reflectionManifest.GlobalManifestHash);
            var manifestJson =
                FoxRunManifestJsonWriter.WriteCanonical(roslynManifest);
            Assert.Contains(
                "\"publishTransportIds\":[\"foxglove.websocket\",\"unity2foxglove.zeta\"]",
                manifestJson);
            Assert.Contains(
                "\"subscribeTransportId\":\"unity2foxglove.alpha\"",
                manifestJson);
        }

        [Fact]
        public void GeneratedCoreEmitsStableDirectProviderMemberAccess()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo",
                    "Source",
                    "_value",
                    "field",
                    "System.Int32",
                    "global::System.Int32",
                    true,
                    false,
                    "",
                    "/phase186/value",
                    10f,
                    "Demo.Value",
                    (int)FoxRunPolicy.FixedRate,
                    0f,
                    "UnitTest",
                    1,
                    "",
                    mode: (int)FoxRunFlow.PublishAndSubscribe)
            });
            var type = Assert.Single(model.Types);
            var member = Assert.Single(type.Members);
            var stableId = FoxRunGeneratedMemberIdentity.Build(
                type.DeclaringType,
                member.MemberKind,
                member.MemberName,
                member.Topic,
                member.Mode,
                member.JsonFieldName);
            var fingerprint =
                FoxRunGeneratedMemberIdentity.Fingerprint(stableId);

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("__FoxRunRead_value_" + fingerprint, source);
            Assert.Contains("__FoxRunWrite_value_" + fingerprint, source);
            Assert.Contains(
                "=> __foxRunCapture_0_0;",
                source);
            Assert.Contains("IFoxRunGeneratedTransportSource", source);
            Assert.Contains(
                "IFoxRunGeneratedTransportSource.FoxRunTransport_MemberCount => 1;",
                source);
            Assert.Contains(
                "IFoxRunGeneratedTransportSource.FoxRunTransport_GetMember(int index)",
                source);
            Assert.Contains(
                "new FoxRunGeneratedMemberAccess<int>",
                source);
            Assert.Contains(
                "\""
                + StringLiteralEmitter.CSharpStringLiteral(stableId)
                + "\"",
                source);
            Assert.Contains(
                "FoxRunTransport_GetCaptureSequence(int topicIndex)",
                source);
            Assert.DoesNotContain("System.Reflection", source);
            Assert.DoesNotContain("GetField(", source);
            Assert.DoesNotContain("GetProperty(", source);
        }

        [Fact]
        public void PhysicalProviderContributionUsesDeterministicIndependentFile()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo",
                    "Source",
                    "Value",
                    "field",
                    "System.Int32",
                    "global::System.Int32",
                    true,
                    false,
                    "",
                    "/phase186/value",
                    10f,
                    "Demo.Value",
                    (int)FoxRunPolicy.FixedRate,
                    0f,
                    "UnitTest",
                    1,
                    "")
            });
            var type = Assert.Single(model.Types);
            var contribution = new FakeEmitterContribution();

            var source =
                FoxRunTransportContributionSource.EmitSourceFile(
                    model,
                    type,
                    contribution);
            var name = FoxRunTransportContributionSource.SourceName(
                type.Namespace,
                type.ClassName,
                contribution);

            Assert.Equal(
                "Demo_Source_unity2foxglove_example_transport_FoxRun.g.cs",
                name);
            Assert.Contains("// <auto-generated/>", source);
            Assert.Contains(
                "// Optional transport contribution: unity2foxglove.example",
                source);
            Assert.Contains("#if !UNITY_EDITOR", source);
            Assert.Contains(
                "partial class Source { private const int ProviderMarker = 1; }",
                source);
        }

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

        private sealed class FakeProvider : IFoxRunTransportProvider
        {
            internal FakeProvider(
                string id,
                FoxRunTransportCapabilities capabilities,
                FoxRunTransportLifecycleState lifecycleState = FoxRunTransportLifecycleState.Available)
            {
                Id = new FoxRunTransportId(id);
                Capabilities = capabilities;
                LifecycleState = lifecycleState;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities { get; }
            public FoxRunTransportLifecycleState LifecycleState { get; }
            internal int CaptureCount { get; private set; }
            internal FakeSession LastCapturedSession { get; private set; }

            public bool TryCaptureSession(
                ulong generation,
                out IFoxRunTransportSession session,
                out string reason)
            {
                CaptureCount++;
                LastCapturedSession = new FakeSession(Id, Capabilities, generation);
                session = LastCapturedSession;
                reason = string.Empty;
                return true;
            }
        }

        private sealed class FakeEmitterContribution :
            IFoxRunTransportEmitterContribution
        {
            public string ProviderId => "unity2foxglove.example";
            public string HintNameSuffix => "transport";

            public void Emit(
                in FoxRunTransportEmitterContext context,
                StringBuilder output)
            {
                output.AppendLine(
                    "namespace Demo { partial class Source { private const int ProviderMarker = 1; } }");
            }
        }

        private sealed class OrdinaryProvider :
            IFoxRunTransportProvider
        {
            private readonly System.Collections.Generic.IList<string> _calls;
            private readonly bool _failPublish;

            internal OrdinaryProvider(
                string id,
                System.Collections.Generic.IList<string> calls,
                bool failPublish)
            {
                Id = new FoxRunTransportId(id);
                _calls = calls;
                _failPublish = failPublish;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish;
            public FoxRunTransportLifecycleState LifecycleState =>
                FoxRunTransportLifecycleState.Available;

            public bool TryCaptureSession(
                ulong generation,
                out IFoxRunTransportSession session,
                out string reason)
            {
                session = new OrdinarySession(
                    Id,
                    generation,
                    _calls,
                    _failPublish);
                reason = string.Empty;
                return true;
            }
        }

        private sealed class OrdinarySession :
            IFoxRunTransportSession,
            IFoxRunOrdinaryPayloadMapper
        {
            private readonly System.Collections.Generic.IList<string> _calls;
            private readonly bool _failPublish;

            internal OrdinarySession(
                FoxRunTransportId id,
                ulong generation,
                System.Collections.Generic.IList<string> calls,
                bool failPublish)
            {
                Id = id;
                Generation = generation;
                _calls = calls;
                _failPublish = failPublish;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish;
            public ulong Generation { get; }
            public string StableMapperId => Id.Value + ".ordinary";

            public bool TryMap(
                in FoxRunOrdinaryPayloadRequest request,
                out FoxRunOrdinaryPayloadContribution contribution,
                out string reason)
            {
                contribution = new FoxRunOrdinaryPayloadContribution(
                    request.LogicalSchemaName,
                    new byte[] { 1 },
                    "fixture",
                    "fixture");
                reason = string.Empty;
                return true;
            }

            public FoxRunTransportPublishResult Publish(
                in FoxRunTransportPublishRoute route)
            {
                _calls.Add(Id.Value);
                if (_failPublish)
                    throw new InvalidOperationException("fixture failure");
                return FoxRunTransportPublishResult.Accepted();
            }

            public FoxRunTransportSubscribeResult Subscribe(
                in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected("not used");

            public void Dispose()
            {
            }
        }

        private sealed class FakeSession :
            IFoxRunTransportSession,
            IFoxRunTransportStatusSource
        {
            internal FakeSession(
                FoxRunTransportId id,
                FoxRunTransportCapabilities capabilities,
                ulong generation)
            {
                Id = id;
                Capabilities = capabilities;
                Generation = generation;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities { get; }
            public ulong Generation { get; }
            internal bool Disposed { get; private set; }
            internal FoxRunTransportCapabilities LastStatusDirections
            {
                get;
                private set;
            }

            public FoxRunTransportStatusSnapshot CaptureStatus(
                FoxRunTransportCapabilities selectedDirections)
            {
                LastStatusDirections = selectedDirections;
                var publishSelected =
                    (selectedDirections
                     & FoxRunTransportCapabilities.Publish) != 0;
                var subscribeSelected =
                    (selectedDirections
                     & FoxRunTransportCapabilities.Subscribe) != 0;
                return new FoxRunTransportStatusSnapshot(
                    Id,
                    Generation,
                    new FoxRunTransportDirectionStatus(
                        FoxRunTransportDirection.Publish,
                        publishSelected,
                        publishSelected
                            ? FoxRunTransportObservedState.Ready
                            : FoxRunTransportObservedState.Stopped,
                        0,
                        0,
                        0),
                    new FoxRunTransportDirectionStatus(
                        FoxRunTransportDirection.Subscribe,
                        subscribeSelected,
                        subscribeSelected
                            ? FoxRunTransportObservedState.Ready
                            : FoxRunTransportObservedState.Stopped,
                        0,
                        0,
                        0));
            }

            public FoxRunTransportPublishResult Publish(in FoxRunTransportPublishRoute route)
                => FoxRunTransportPublishResult.Accepted();

            public FoxRunTransportSubscribeResult Subscribe(in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected("not used");

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class GeneratedSource :
            IFoxRunGeneratedTransportSource
        {
            public int FoxRunTransport_MemberCount => 0;

            public IFoxRunGeneratedMemberAccess FoxRunTransport_GetMember(
                int index)
                => throw new ArgumentOutOfRangeException(nameof(index));

            public ulong FoxRunTransport_GetCaptureSequence(int topicIndex)
                => 0;
        }

        private sealed class GeneratedSession :
            IFoxRunTransportSession,
            IFoxRunGeneratedTransportSession
        {
            private readonly System.Collections.Generic.IList<string> _calls;
            private readonly FoxRunTransportPublishResult _result;

            internal GeneratedSession(
                string id,
                System.Collections.Generic.IList<string> calls,
                FoxRunTransportPublishResult result,
                ulong generation = 186)
            {
                Id = new FoxRunTransportId(id);
                _calls = calls;
                _result = result;
                Generation = generation;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish;
            public ulong Generation { get; }

            public FoxRunTransportPublishResult PublishGenerated(
                in FoxRunGeneratedTransportPublishRequest request)
            {
                _calls.Add(Id.Value);
                return _result;
            }

            public FoxRunTransportPublishResult Publish(
                in FoxRunTransportPublishRoute route)
                => throw new NotSupportedException();

            public FoxRunTransportSubscribeResult Subscribe(
                in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected("not used");

            public void Dispose()
            {
            }
        }

        private sealed class FakeDetachedLease : IFoxRunDetachedRetirementLease
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class BlockingDetachedLease : IFoxRunDetachedRetirementLease
        {
            internal System.Threading.ManualResetEventSlim DisposeEntered { get; } =
                new System.Threading.ManualResetEventSlim(false);

            internal System.Threading.ManualResetEventSlim AllowDispose { get; } =
                new System.Threading.ManualResetEventSlim(false);

            public void Dispose()
            {
                DisposeEntered.Set();
                Assert.True(AllowDispose.Wait(TimeSpan.FromSeconds(2)));
            }
        }

        private sealed class ThrowingDetachedLease : IFoxRunDetachedRetirementLease
        {
            public void Dispose()
                => throw new InvalidOperationException("test cleanup failure");
        }
    }
}
