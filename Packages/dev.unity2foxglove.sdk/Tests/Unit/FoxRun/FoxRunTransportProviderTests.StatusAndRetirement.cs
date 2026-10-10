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

    }
}
