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

    }
}
