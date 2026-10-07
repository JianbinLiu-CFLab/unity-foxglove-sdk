// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Lock the portable FoxRun QoS mapping and temporary-profile lifetime.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity2Foxglove.Tests.Ros2ForUnity
{
    public sealed partial class FoxRunRos2NativeQosMapperTests
    {
        [Theory]
        [InlineData(
            FoxRunQosProfile.Default,
            ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
            10,
            ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE,
            ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE,
            ROS2.QosPresetProfile.DEFAULT)]
        [InlineData(
            FoxRunQosProfile.SensorData,
            ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
            5,
            ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT,
            ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE,
            ROS2.QosPresetProfile.SENSOR_DATA)]
        [InlineData(
            FoxRunQosProfile.SystemDefault,
            ROS2.HistoryPolicy.QOS_POLICY_HISTORY_SYSTEM_DEFAULT,
            0,
            ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT,
            ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_SYSTEM_DEFAULT,
            ROS2.QosPresetProfile.SYSTEM_DEFAULT)]
        public void OfficialProfilesMapEveryPortablePolicyExactly(
            FoxRunQosProfile profile,
            ROS2.HistoryPolicy expectedHistory,
            int expectedDepth,
            ROS2.ReliabilityPolicy expectedReliability,
            ROS2.DurabilityPolicy expectedDurability,
            ROS2.QosPresetProfile expectedPreset)
        {
            AssertMaps(
                ResolvedProfile(profile),
                expectedHistory,
                expectedDepth,
                expectedReliability,
                expectedDurability,
                expectedPreset);
        }

        [Theory]
        [InlineData(
            FoxRunQosReliability.SystemDefault,
            ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT)]
        [InlineData(
            FoxRunQosReliability.Reliable,
            ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE)]
        [InlineData(
            FoxRunQosReliability.BestEffort,
            ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT)]
        public void ReliabilityPoliciesMapWithoutDowngrade(
            FoxRunQosReliability reliability,
            ROS2.ReliabilityPolicy expected)
        {
            AssertMaps(
                new FoxRunResolvedQos(
                    FoxRunQosProfile.Default,
                    reliability,
                    FoxRunQosDurability.Volatile,
                    FoxRunQosHistory.KeepLast,
                    10),
                ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
                10,
                expected,
                ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE);
        }

        [Theory]
        [InlineData(
            FoxRunQosDurability.SystemDefault,
            ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_SYSTEM_DEFAULT)]
        [InlineData(
            FoxRunQosDurability.Volatile,
            ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE)]
        [InlineData(
            FoxRunQosDurability.TransientLocal,
            ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_TRANSIENT_LOCAL)]
        public void DurabilityPoliciesMapWithoutDowngrade(
            FoxRunQosDurability durability,
            ROS2.DurabilityPolicy expected)
        {
            AssertMaps(
                new FoxRunResolvedQos(
                    FoxRunQosProfile.Default,
                    FoxRunQosReliability.Reliable,
                    durability,
                    FoxRunQosHistory.KeepLast,
                    10),
                ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
                10,
                ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE,
                expected);
        }

        [Theory]
        [InlineData(
            FoxRunQosHistory.SystemDefault,
            0,
            ROS2.HistoryPolicy.QOS_POLICY_HISTORY_SYSTEM_DEFAULT)]
        [InlineData(
            FoxRunQosHistory.KeepLast,
            10,
            ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST)]
        [InlineData(
            FoxRunQosHistory.KeepAll,
            0,
            ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_ALL)]
        public void HistoryPoliciesMapWithoutDowngrade(
            FoxRunQosHistory history,
            int depth,
            ROS2.HistoryPolicy expected)
        {
            AssertMaps(
                new FoxRunResolvedQos(
                    FoxRunQosProfile.Default,
                    FoxRunQosReliability.Reliable,
                    FoxRunQosDurability.Volatile,
                    history,
                    depth),
                expected,
                depth,
                ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE,
                ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE);
        }

        [Fact]
        public void NonDefaultKeepLastDepthIsPassedExactly()
        {
            AssertMaps(
                new FoxRunResolvedQos(
                    FoxRunQosProfile.Default,
                    FoxRunQosReliability.BestEffort,
                    FoxRunQosDurability.TransientLocal,
                    FoxRunQosHistory.KeepLast,
                    37),
                ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
                37,
                ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT,
                ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_TRANSIENT_LOCAL);
        }

        [Fact]
        public void MixedSystemDefaultProfileHonorsEveryResolvedAxis()
        {
            AssertMaps(
                new FoxRunResolvedQos(
                    FoxRunQosProfile.SystemDefault,
                    FoxRunQosReliability.Reliable,
                    FoxRunQosDurability.TransientLocal,
                    FoxRunQosHistory.KeepLast,
                    37),
                ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
                37,
                ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE,
                ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_TRANSIENT_LOCAL,
                ROS2.QosPresetProfile.SYSTEM_DEFAULT);
        }

        [Fact]
        public void ZeroResolvedValueFailsClosedWithoutCreatingOrReturningAProfile()
        {
            var factory = new RecordingFactory();

            var result = Ros2ForUnityNativeQosMapper.TryCreate(
                default,
                factory,
                out var profile);

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.UnsupportedQos, result.Error);
            Assert.Null(profile);
            Assert.Equal(0, factory.CreateCount);
        }

        [Fact]
        public void MissingRuntimePolicySurfaceFailsClosedAndDisposesPartialProfileOnce()
        {
            var factory = new RecordingFactory
            {
                ConfigureException = new MissingMethodException("SetPolicies")
            };

            var result = Ros2ForUnityNativeQosMapper.TryCreate(
                FoxRunResolvedQos.SensorData,
                factory,
                out var profile);

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.UnsupportedQos, result.Error);
            Assert.Null(profile);
            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(1, factory.Profile.SetPoliciesCount);
            Assert.Equal(1, factory.Profile.DisposeCount);
        }

        [Fact]
        public void OtherRuntimeConfigurationFailureRemainsBackendFailureAndDisposesOnce()
        {
            var factory = new RecordingFactory
            {
                ConfigureException = new InvalidOperationException("configure")
            };

            var result = Ros2ForUnityNativeQosMapper.TryCreate(
                FoxRunResolvedQos.Default,
                factory,
                out var profile);

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, result.Error);
            Assert.Null(profile);
            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(1, factory.Profile.SetPoliciesCount);
            Assert.Equal(1, factory.Profile.DisposeCount);
        }

        [Fact]
        public void BindingPassesOneFullyConfiguredProfileToRegistrationThenDisposesIt()
        {
            var factory = new RecordingFactory();
            var backend = new RecordingBackend();
            var qos = FoxRunResolvedQos.SensorData;
            var binding = CreateBinding(backend, qos, factory);

            var result = binding.TryRegister();

            Assert.True(result.Succeeded);
            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(1, backend.RegisterCount);
            Assert.Equal(1, backend.NodeOwnershipAcquireCount);
            Assert.Equal(0, backend.RemoveCount);
            Assert.Equal(0, backend.NodeOwnershipReleaseCount);
            Assert.Same(factory.Profile, backend.Profile);
            AssertPolicies(
                factory.Profile,
                ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST,
                5,
                ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT,
                ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE);
            Assert.Equal(0, backend.DisposeCountAtRegistration);
            Assert.Equal(1, factory.Profile.DisposeCount);

            binding.Stop();

            Assert.Equal(1, backend.RegisterCount);
            Assert.Equal(1, backend.NodeOwnershipAcquireCount);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.NodeOwnershipReleaseCount);
            Assert.Equal(1, factory.Profile.DisposeCount);

            binding.Stop();

            Assert.Equal(1, backend.RegisterCount);
            Assert.Equal(1, backend.NodeOwnershipAcquireCount);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.NodeOwnershipReleaseCount);
            Assert.Equal(1, factory.Profile.DisposeCount);
        }

        [Fact]
        public void ProfileDisposeFailureRollsBackTheSingleCreatedSubscription()
        {
            var factory = new RecordingFactory
            {
                DisposeException = new InvalidOperationException("dispose")
            };
            var backend = new RecordingBackend();
            var binding = CreateBinding(backend, FoxRunResolvedQos.Default, factory);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, result.Error);
            Assert.Equal(1, backend.RegisterCount);
            Assert.Equal(1, backend.NodeOwnershipAcquireCount);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(0, backend.NodeOwnershipReleaseCount);
            Assert.Equal(1, factory.Profile.DisposeCount);

            binding.Stop();

            Assert.Equal(1, backend.NodeOwnershipAcquireCount);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.NodeOwnershipReleaseCount);
            Assert.Equal(1, factory.Profile.DisposeCount);

            binding.Stop();

            Assert.Equal(1, backend.NodeOwnershipAcquireCount);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.NodeOwnershipReleaseCount);
            Assert.Equal(1, factory.Profile.DisposeCount);
        }

        [Fact]
        public void UnsupportedQosCreatesNoCompatibilitySubscription()
        {
            var factory = new RecordingFactory();
            var backend = new RecordingBackend();
            var binding = CreateBinding(backend, default, factory);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.UnsupportedQos, result.Error);
            Assert.Equal(0, backend.RegisterCount);
            Assert.Equal(0, factory.CreateCount);
            binding.Stop();
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void PackagedEndpointCreationCopiesTemporaryQosBeforeReturning(string distro)
        {
            var root = FindRepositoryRoot();
            var runtime = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64",
                "Runtime",
                "Ros2ForUnity");
            var source = File.ReadAllText(Path.Combine(runtime, "Scripts", "ROS2Node.cs"));
            Assert.Contains(
                "liveNode.CreateSubscription<T>(topicName, callback, qos)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "using (QualityOfServiceProfile defaultQos = new QualityOfServiceProfile(QosPresetProfile.DEFAULT))",
                source,
                StringComparison.Ordinal);
            Assert.Contains("profile is copied during creation", source, StringComparison.Ordinal);

            var assembly = Assembly.LoadFile(Path.Combine(runtime, "Plugins", "ros2cs_core.dll"));
            var subscription = assembly.GetType("ROS2.Subscription`1", throwOnError: true);
            Assert.DoesNotContain(
                subscription.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                field => field.FieldType.FullName == "ROS2.QualityOfServiceProfile");
        }
    }
}
#endif
