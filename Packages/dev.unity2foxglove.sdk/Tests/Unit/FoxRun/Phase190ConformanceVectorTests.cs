// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Linq;
using Xunit;
using Unity.FoxgloveSDK.Tests.Unit.FoxRun;
using DescriptorEmitterTests = Unity.FoxgloveSDK.UnitTests.FoxRun.FoxRunDescriptorCarrierEmitterTests;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    /// <summary>
    /// Executes one deterministic vector per Phase190 claim against the same
    /// production-backed unit fixtures used by the focused owner suites.
    /// </summary>
    [Trait("Domain", "Phase190Conformance")]
    public sealed class Phase190ConformanceVectorTests
    {
        [Fact]
        public void VectorCatalogContainsEveryFrozenClaim()
        {
            Assert.Equal(11, Phase190ConformanceVectors.ByClaim.Count);
            Assert.Equal(
                new[]
                {
                    "FR-DECL-001", "FR-HOST-002", "FR-EMIT-003", "FR-OUT-004",
                    "FR-IN-005", "FR-OWN-006", "FR-LIFE-007", "FR-SCHEMA-008",
                    "FR-ROS-009", "FR-AOT-010", "FR-EVID-011"
                },
                Phase190ConformanceVectors.ByClaim.Keys);
        }

        [Fact]
        public void DeclarationDefaultsVector()
        {
            var tests = new FoxRunDeclarationModelTests();
            tests.FoxRunAttributeDefaultsToPublishFlow();
            tests.ReflectionAndRoslynLowerersProduceEquivalentCanonicalConditionModel();
        }

        [Fact]
        public void DualHostVector()
            => new FoxRunDeclarationModelTests().ReflectionAndRoslynLowerersProduceEquivalentCanonicalConditionModel();

        [Fact]
        public void SharedEmitterVector()
        {
            var descriptor = new DescriptorEmitterTests();
            descriptor.ChunkedDescriptorCarrierDoesNotSplitAStringEscape();
            var aggregate = new FoxRunAggregationEmitterTests();
            aggregate.AggregateMessagePackEmitsOneDirectStableMapBuilderWithoutJsonFallback();
        }

        [Fact]
        public void OutputFanoutVector()
        {
            var aggregate = new FoxRunAggregationEmitterTests();
            aggregate.AggregateMemberEmitsSinkFanoutSideChannelReusingExplicitJsonBytes();
            aggregate.AggregateMemberEmitsBusSideChannelReusingExplicitJsonBytes();
            aggregate.LegacySingleFieldTopicEmitsSinkFanoutSideChannel();
            aggregate.AggregateSchedulingUsesTheSameShortVocabulary();
            var providers = new Unity.FoxgloveSDK.Tests.FoxRunTransportProviderTests();
            providers.OrdinaryFanoutContinuesAcrossThreeProvidersWhenMiddleProviderFails();
            providers.GeneratedFanoutUsesExplicitRoutesAndClassifiesEverySelectedProvider();
        }

        [Fact]
        public void InputAdmissionVector()
        {
            var inbound = new FoxRunInboundTests();
            inbound.RouterUsesGeneratedAllowlistAndRegistrationOrder();
            inbound.RouterRejectsUnknownOversizedAndRateLimitedMessages();
            inbound.RouterRejectsWrongEncodingBeforeItConsumesTheTopicRateQuota();
            inbound.RouterConsumesOneQuotaAndAppliesOnlyMatchingSharedTopicRegistrations();
            inbound.RouterUnregisterStopsAssignment();
            inbound.InputHubSafelyRebindsSessionPolicyAndAppliesTheCurrentSnapshotImmediately();
            inbound.InputHubRefreshesSessionPolicyBeforeFirstMessageDispatch();
            var providers = new Unity.FoxgloveSDK.Tests.FoxRunTransportProviderTests();
            providers.CaptureFailsClosedForMissingUnavailableOrCapabilityMismatch();
            providers.ResolveRevalidatesProviderAfterReentrantMetadataMutation();
        }

        [Fact]
        public void OwnershipVector()
        {
            var inbound = new FoxRunInboundTests();
            inbound.RouterUnregisterClearsOptionalOwnedInputExactlyOncePerRegistrationLifetime();
            inbound.FailedSecondOwnershipAcquisitionClearsTheFirstMemberExactlyOnce();
            inbound.GeneratedWebSocketStreamFreezesRegisteredInstanceUntilOwnedClear();
        }

        [Fact]
        public void LifecycleVector()
        {
            new FoxgloveManagerTeardownTests().DisableTeardownRunsEveryMandatoryStepInOrderAndRethrowsFirstFatal();
            var runtime = new Unity.FoxgloveSDK.UnitTests.Harness.RuntimeStateReviewTests();
            runtime.RuntimeStopDetachesRecordingExactlyOnceBeforeSessionDispose();
            runtime.RuntimeStartFailureDisposesPartiallyStartedSessionBeforeRethrowing();
            runtime.RuntimeDisposePreservesCleanupAndReleasesTransportAfterStopFailure();
        }

        [Fact]
        public void SchemaReplayVector()
        {
            new FoxgloveSdk.UnitTests.Mcap.McapReplayBoundsTests().DeferredFutureMessageCountBoundIsIndependentOfOwnerByteBound();
            var replay = new Unity.FoxgloveSDK.Tests.Replay.ReplayFileValidatorTests();
            replay.ValidateReplayFileForLoadRejectsEmptyMissingAndUnfinalizedFiles();
            replay.ValidateReplayFileForLoadAcceptsFinalizedMcapEnvelope();
            var schema = new Unity.FoxgloveSDK.UnitTests.Harness.SchemaManifestJsonWriterTests();
            schema.WriteReportRejectsNullManifestWithParameterName();
        }

        [Fact]
        public void Ros2MappingSourceVector()
        {
            var path = FindRepoFile("Packages", "dev.unity2foxglove.ros2bridge", "Runtime", "Protocol", "U2R2ContractAuthority.cs");
            Assert.True(File.Exists(path), "ROS2 protocol authority source is missing.");
            var source = File.ReadAllText(path);
            Assert.Contains("U2R2ContractAuthority", source, StringComparison.Ordinal);
            Assert.Contains("ParseContract", source, StringComparison.Ordinal);
        }

        [Fact]
        public void AotGeneratedSourceVector()
        {
            var path = FindRepoFile("Unity2Foxglove", "Assets", "Scripts", "Generated", "TestLog_FoxRun.g.cs");
            Assert.True(File.Exists(path), "Generated FoxRun source is missing.");
            var source = File.ReadAllText(path);
            Assert.Contains("partial class TestLog", source, StringComparison.Ordinal);
        }

        [Fact]
        public void EvidenceToolingVector()
        {
            var path = FindRepoFile("Scripts", "phase190", "validate_adjudication.py");
            Assert.True(File.Exists(path), "Phase190 evidence validator is missing.");
            var source = File.ReadAllText(path);
            Assert.Contains("ADJUDICATION_VALID", source, StringComparison.Ordinal);
        }

        private static string FindRepoFile(params string[] parts)
        {
            var current = new DirectoryInfo(Environment.CurrentDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                    return candidate;
                current = current.Parent;
            }
            return Path.Combine(parts);
        }
    }
}
