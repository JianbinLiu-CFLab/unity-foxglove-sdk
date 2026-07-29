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
        public void RegistryIsManagerLocalIdempotentAndConflictOrderIndependent()
        {
            var registryA = new FoxRunTransportProviderRegistry();
            var registryB = new FoxRunTransportProviderRegistry();
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
            Assert.Equal(FoxRunTransportProviderResolutionState.Absent,
                registryB.Resolve(first.Id, FoxRunTransportCapabilities.Publish).State);

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
            Assert.True(reservation.TryReturn(workerIndex: 1));
            Assert.Equal(1, owner.OccupiedCount);
            Assert.True(reservation.TryCompleteRetired(workerIndex: 0));
            Assert.True(lease.Disposed);
            Assert.Equal(0, owner.OccupiedCount);
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

            var source = FoxgloveSourceEmitter.EmitClass(
                type,
                emitRos2NativePartial: false);

            Assert.Contains("__FoxRunRead_value_" + fingerprint, source);
            Assert.Contains("__FoxRunWrite_value_" + fingerprint, source);
            Assert.Contains(
                "=> __foxRunCapture_0_0;",
                source);
            Assert.DoesNotContain("IFoxRunGeneratedTransportSource", source);
            Assert.DoesNotContain("new FoxRunGeneratedMemberAccess", source);
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

        private sealed class FakeSession : IFoxRunTransportSession
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

            public FoxRunTransportPublishResult Publish(in FoxRunTransportPublishRoute route)
                => FoxRunTransportPublishResult.Accepted();

            public FoxRunTransportSubscribeResult Subscribe(in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected("not used");

            public void Dispose()
            {
                Disposed = true;
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
    }
}
