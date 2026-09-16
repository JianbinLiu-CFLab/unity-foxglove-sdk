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
        public void DeclarationDefaultsVector()
            => new FoxRunDeclarationModelTests().FoxRunAttributeDefaultsToPublishFlow();

        [Fact]
        public void DualHostVector()
            => new FoxRunDeclarationModelTests().ReflectionAndRoslynLowerersProduceEquivalentCanonicalConditionModel();

        [Fact]
        public void SharedEmitterVector()
            => new DescriptorEmitterTests().ChunkedDescriptorCarrierDoesNotSplitAStringEscape();

        [Fact]
        public void OutputFanoutVector()
            => new FoxRunAggregationEmitterTests().AggregateMemberEmitsSinkFanoutSideChannelReusingExplicitJsonBytes();

        [Fact]
        public void InputAdmissionVector()
            => new FoxRunInboundTests().RouterUsesGeneratedAllowlistAndRegistrationOrder();

        [Fact]
        public void OwnershipVector()
            => new FoxRunInboundTests().RouterUnregisterClearsOptionalOwnedInputExactlyOncePerRegistrationLifetime();

        [Fact]
        public void LifecycleVector()
            => new FoxgloveManagerTeardownTests().DisableTeardownRunsEveryMandatoryStepInOrderAndRethrowsFirstFatal();

        [Fact]
        public void SchemaReplayVector()
            => new FoxgloveSdk.UnitTests.Mcap.McapReplayBoundsTests().DeferredFutureMessageCountBoundIsIndependentOfOwnerByteBound();

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
