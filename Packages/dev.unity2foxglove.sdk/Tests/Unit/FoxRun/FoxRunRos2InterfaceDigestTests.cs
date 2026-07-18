// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.FoxRun
{
    public sealed class FoxRunRos2InterfaceDigestTests
    {
        [Fact]
        public void IdentityUsesTheOneStaticSourcePackageAndInitialRosRevision()
        {
            Assert.Equal(
                "dev.unity2foxglove.foxrun.ros2.interfaces",
                FoxRunRos2InterfaceIdentity.UnityPackageId);
            Assert.Equal(
                "unity2foxglove_foxrun_interfaces_v1",
                FoxRunRos2InterfaceIdentity.DefaultRosPackageName);
            Assert.Equal(
                FoxRunRos2InterfaceIdentity.DefaultRosPackageName,
                FoxRunRos2InterfaceIdentity.BuildRosPackageName(1));
            Assert.Equal(
                "ExamplePayloadEnvelope",
                FoxRunRos2InterfaceIdentity.BuildEnvelopeMessageName("ExamplePayload"));
            Assert.True(FoxRunRos2InterfaceIdentity.TryParseRosPackageRevision("project_interfaces_v1", out var initialRevision));
            Assert.Equal(1, initialRevision);
            Assert.Equal("project_interfaces_v2", FoxRunRos2InterfaceIdentity.BuildRosPackageName("project_interfaces_v1", 2));
        }

        [Fact]
        public void PayloadIdentityUsesTheRos2UpperCamelMessageGrammar()
        {
            var identity = FoxRunRos2CustomIdentity.BuildPayloadIdentity(
                "Example.Namespace.PayloadState",
                "canonical-shape");

            Assert.Matches("^[A-Z][A-Za-z0-9]*$", identity);
            Assert.DoesNotContain("_", identity, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("unity2foxglove_foxrun_interfaces_v1", true)]
        [InlineData("a", true)]
        [InlineData("a_1", true)]
        [InlineData("Unity2Foxglove", false)]
        [InlineData("1interfaces", false)]
        [InlineData("interfaces-1", false)]
        [InlineData("interfaces_", false)]
        [InlineData("", false)]
        public void RosPackageNameGrammarIsStable(string packageName, bool expected)
            => Assert.Equal(expected, FoxRunRos2InterfaceIdentity.IsValidRosPackageName(packageName));

        [Fact]
        public void DigestIsStableAcrossInputOrderAndTextLineEndings()
        {
            var first = FoxRunRos2InterfaceDigest.Compute(
                FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                new[]
                {
                    new FoxRunRos2InterfaceDigestInput("Ros2Package~/msg/Example.msg", "int32 count\r\n"),
                    new FoxRunRos2InterfaceDigestInput("package.json", "{\"name\":\"example\"}\n")
                });
            var second = FoxRunRos2InterfaceDigest.Compute(
                FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                new[]
                {
                    new FoxRunRos2InterfaceDigestInput("package.json", "{\"name\":\"example\"}\r\n"),
                    new FoxRunRos2InterfaceDigestInput("Ros2Package~\\msg\\Example.msg", "int32 count\n")
                });

            Assert.Equal(first, second);
            Assert.Equal("518773aa5ba89143600cc1111d371b19bfa54d11bd4874ff3e154e4048de9bdd", first);
            Assert.Matches("^[0-9a-f]{64}$", first);
        }

        [Fact]
        public void DigestChangesForOneByteDifference()
        {
            var baseline = FoxRunRos2InterfaceDigest.Compute(
                FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                new[] { new FoxRunRos2InterfaceDigestInput("msg/Example.msg", "int32 count\n") });
            var changed = FoxRunRos2InterfaceDigest.Compute(
                FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                new[] { new FoxRunRos2InterfaceDigestInput("msg/Example.msg", "int32 count\n#\n") });

            Assert.NotEqual(baseline, changed);
        }

        [Fact]
        public void DigestRejectsDuplicatePathsAndUnknownVersion()
        {
            Assert.Throws<ArgumentException>(() => FoxRunRos2InterfaceDigest.Compute(
                FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                new[]
                {
                    new FoxRunRos2InterfaceDigestInput("msg/Example.msg", "a\n"),
                    new FoxRunRos2InterfaceDigestInput("msg\\Example.msg", "b\n")
                }));
            Assert.Throws<ArgumentOutOfRangeException>(() => FoxRunRos2InterfaceDigest.Compute(
                99,
                new[] { new FoxRunRos2InterfaceDigestInput("msg/Example.msg", "a\n") }));
        }

        [Fact]
        public void LockSerializationIsDeterministicAndRejectsMalformedInput()
        {
            var contract = new FoxRunRos2InterfaceContractLock(
                "Example.Component",
                "State",
                "/example/state",
                "dto-canonical",
                "ExamplePayload",
                "ExamplePayloadEnvelope",
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789");
            var value = new FoxRunRos2InterfaceLock(
                FoxRunRos2InterfaceIdentity.LockSchemaVersion,
                FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion,
                FoxRunRos2InterfaceIdentity.UnityPackageId,
                FoxRunRos2InterfaceIdentity.DefaultRosPackageName,
                1,
                "2.0.0",
                FoxRunRos2InterfaceIdentity.NamingPolicyVersion,
                "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210",
                new[] { contract });

            var json = FoxRunRos2InterfaceJsonWriter.WriteLock(value);
            var reparsed = FoxRunRos2InterfaceLock.Parse(json);

            Assert.Equal(json, FoxRunRos2InterfaceJsonWriter.WriteLock(reparsed));
            Assert.Equal("ExamplePayloadEnvelope", reparsed.Contracts[0].EnvelopeMessageName);
            Assert.Throws<FormatException>(() => FoxRunRos2InterfaceLock.Parse("{\"lockSchemaVersion\":1}"));
        }
    }
}
