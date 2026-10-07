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
        public void GeneratedProviderAccessUsesTheManifestLogicalSchemaFallback()
        {
            var member = new FoxRunGenerationMember(
                ns: "Demo",
                className: "Source",
                memberName: "_log",
                memberKind: "field",
                rawObservedTypeName: "Foxglove.Log",
                emissionTypeName: "global::Foxglove.Log",
                isValueType: false,
                isArray: false,
                elementTypeName: string.Empty,
                topic: "/phase186/log",
                hz: 10f,
                schemaName: string.Empty,
                policy: (int)FoxRunPolicy.Change,
                tolerance: 0f,
                hostKind: "UnitTest",
                rawMemberOrder: 0,
                conditionalSymbols: string.Empty,
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                typeShape: FoxRunTypeShape.Object(
                    "Foxglove.Log",
                    Array.Empty<FoxRunTypeField>()),
                generatesWebSocketCodec: true,
                publishTransportIds: new[]
                {
                    FoxgloveWebSocketTransport.Id,
                    "unity2foxglove.ros2bridge"
                },
                subscribeTransportId: "unity2foxglove.ros2bridge");
            var model = FoxRunGenerationModel.FromMembers(new[] { member });
            var type = Assert.Single(model.Types);
            var manifest = FoxRunManifestBuilder.Build(
                new[]
                {
                    FoxRunManifestMember.FromGenerationMember(member)
                },
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var contracts = Assert.Single(
                manifest.Sections.FoxRun.Types).Contracts;

            Assert.All(
                contracts,
                contract => Assert.Equal(
                    "Foxglove.Log",
                    contract.LogicalSchemaName));
            var source = FoxgloveSourceEmitter.EmitClass(type);
            Assert.Matches(
                @"new FoxRunGeneratedMemberAccess<(?:global::)?Foxglove\.Log>\s*\(\s*""[^""]+"",\s*""/phase186/log"",\s*""Foxglove\.Log"",\s*\(FoxRunFlow\)3,",
                source);
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

        private sealed class ProbeTransportSession : IFoxRunTransportSession, IFoxRunTransportStatusSource
        {
            public ProbeTransportSession(FoxRunTransportId id, FoxRunTransportCapabilities capabilities, ulong generation)
            {
                Id = id;
                Capabilities = capabilities;
                Generation = generation;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities { get; }
            public ulong Generation { get; }
            public bool Disposed { get; private set; }
            public string StatusIdOverride { get; set; }

            public FoxRunTransportPublishResult Publish(in FoxRunTransportPublishRoute route)
                => FoxRunTransportPublishResult.Accepted();

            public FoxRunTransportSubscribeResult Subscribe(in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected("not used");

            public void Dispose() => Disposed = true;

            public FoxRunTransportStatusSnapshot CaptureStatus(FoxRunTransportCapabilities selected)
            {
                var publish = (selected & FoxRunTransportCapabilities.Publish) != 0;
                var subscribe = (selected & FoxRunTransportCapabilities.Subscribe) != 0;
                return new FoxRunTransportStatusSnapshot(
                    StatusIdOverride == null ? Id : new FoxRunTransportId(StatusIdOverride),
                    Generation,
                    new FoxRunTransportDirectionStatus(
                        FoxRunTransportDirection.Publish,
                        publish,
                        publish ? FoxRunTransportObservedState.Ready : FoxRunTransportObservedState.Stopped,
                        0,
                        0,
                        0),
                    new FoxRunTransportDirectionStatus(
                        FoxRunTransportDirection.Subscribe,
                        subscribe,
                        subscribe ? FoxRunTransportObservedState.Ready : FoxRunTransportObservedState.Stopped,
                        0,
                        0,
                        0));
            }
        }

    }
}
