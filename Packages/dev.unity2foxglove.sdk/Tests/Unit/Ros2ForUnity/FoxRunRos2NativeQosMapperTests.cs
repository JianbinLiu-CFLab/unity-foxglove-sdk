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
    public sealed class FoxRunRos2NativeQosMapperTests
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

        [Fact]
        public void MapperContainsNoDistroOrRmwSpecificBranch()
        {
            var source = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun",
                "Ros2ForUnityNativeQosMapper.cs"));

            Assert.DoesNotContain("FastDDS", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FastRTPS", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Zenoh", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Humble", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Jazzy", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Lyrical", source, StringComparison.OrdinalIgnoreCase);
        }

        private static FoxRunResolvedQos ResolvedProfile(FoxRunQosProfile profile)
        {
            switch (profile)
            {
                case FoxRunQosProfile.Default:
                    return FoxRunResolvedQos.Default;
                case FoxRunQosProfile.SensorData:
                    return FoxRunResolvedQos.SensorData;
                case FoxRunQosProfile.SystemDefault:
                    return FoxRunResolvedQos.SystemDefault;
                default:
                    throw new ArgumentOutOfRangeException(nameof(profile));
            }
        }

        private static void AssertMaps(
            FoxRunResolvedQos qos,
            ROS2.HistoryPolicy expectedHistory,
            int expectedDepth,
            ROS2.ReliabilityPolicy expectedReliability,
            ROS2.DurabilityPolicy expectedDurability,
            ROS2.QosPresetProfile expectedPreset = ROS2.QosPresetProfile.DEFAULT)
        {
            var factory = new RecordingFactory();

            var result = Ros2ForUnityNativeQosMapper.TryCreate(qos, factory, out var profile);

            Assert.True(result.Succeeded);
            Assert.NotNull(profile);
            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(expectedPreset, factory.Profile.Preset);
            Assert.Same(factory.Profile, profile);
            AssertPolicies(
                factory.Profile,
                expectedHistory,
                expectedDepth,
                expectedReliability,
                expectedDurability);
            Assert.Equal(0, factory.Profile.DisposeCount);

            profile.Dispose();

            Assert.Equal(1, factory.Profile.DisposeCount);
        }

        private static void AssertPolicies(
            RecordingProfile profile,
            ROS2.HistoryPolicy expectedHistory,
            int expectedDepth,
            ROS2.ReliabilityPolicy expectedReliability,
            ROS2.DurabilityPolicy expectedDurability)
        {
            Assert.Equal(0, profile.SetHistoryCount);
            Assert.Equal(1, profile.SetPoliciesCount);
            Assert.Equal(expectedHistory, profile.History);
            Assert.Equal(expectedDepth, profile.Depth);
            Assert.Equal(expectedReliability, profile.Reliability);
            Assert.Equal(expectedDurability, profile.Durability);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Packages")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }

        private static FoxRunRos2SubscriptionBinding<QosMessage> CreateBinding(
            RecordingBackend backend,
            FoxRunResolvedQos qos,
            RecordingFactory factory)
            => new FoxRunRos2SubscriptionBinding<QosMessage>(
                new FoxRunRos2GeneratedContract(
                    "qos-contract",
                    "/qos",
                    "Demo.QosReceiver",
                    "_message",
                    "std_msgs/msg/String",
                    FoxRunFlow.Subscribe,
                    FoxRunRos2RouteEndpoint.R2fu,
                    qos.Profile,
                    true,
                    qos.Reliability,
                    true,
                    qos.Durability,
                    true,
                    qos.History,
                    true,
                    qos.Depth,
                    true,
                    true),
                1,
                () => 1,
                1024,
                (source, _) => new QosMessage(),
                message => message.Dispose(),
                _ => { },
                _ => false,
                backend,
                qos,
                factory);

        private sealed class RecordingFactory : IFoxRunRos2NativeQosProfileFactory
        {
            public int CreateCount { get; private set; }
            public RecordingProfile Profile { get; private set; }
            public Exception ConfigureException { get; set; }
            public Exception DisposeException { get; set; }

            public IFoxRunRos2NativeQosProfile Create(ROS2.QosPresetProfile preset)
            {
                CreateCount++;
                Profile = new RecordingProfile(
                    preset,
                    () => ConfigureException,
                    () => DisposeException);
                return Profile;
            }
        }

        private sealed class RecordingProfile : IFoxRunRos2NativeQosProfile
        {
            private readonly Func<Exception> _configureException;
            private readonly Func<Exception> _disposeException;

            public RecordingProfile(
                ROS2.QosPresetProfile preset,
                Func<Exception> configureException,
                Func<Exception> disposeException)
            {
                Preset = preset;
                _configureException = configureException;
                _disposeException = disposeException;
            }

            public ROS2.QosPresetProfile Preset { get; }
            public ROS2.HistoryPolicy? History { get; private set; }
            public int Depth { get; private set; }
            public ROS2.ReliabilityPolicy? Reliability { get; private set; }
            public ROS2.DurabilityPolicy? Durability { get; private set; }
            public int SetHistoryCount { get; private set; }
            public int SetPoliciesCount { get; private set; }
            public int DisposeCount { get; private set; }
            public ROS2.QualityOfServiceProfile NativeProfile => null;

            public void SetHistory(ROS2.HistoryPolicy history, int depth)
            {
                SetHistoryCount++;
                History = history;
                Depth = depth;
                ThrowIfConfigured();
            }

            public void SetPolicies(
                ROS2.HistoryPolicy history,
                int depth,
                ROS2.ReliabilityPolicy reliability,
                ROS2.DurabilityPolicy durability)
            {
                SetPoliciesCount++;
                History = history;
                Depth = depth;
                Reliability = reliability;
                Durability = durability;
                ThrowIfConfigured();
            }

            public void Dispose()
            {
                DisposeCount++;
                var exception = _disposeException();
                if (exception != null)
                    throw exception;
            }

            private void ThrowIfConfigured()
            {
                var exception = _configureException();
                if (exception != null)
                    throw exception;
            }
        }

        private sealed class RecordingBackend : IFoxRunRos2NativeBackend
        {
            public int RegisterCount { get; private set; }
            public int NodeOwnershipAcquireCount { get; private set; }
            public int RemoveCount { get; private set; }
            public int NodeOwnershipReleaseCount { get; private set; }
            public int DisposeCountAtRegistration { get; private set; }
            public IFoxRunRos2NativeQosProfile Profile { get; private set; }

            public FoxRunRos2NativeBackendRegistration Register<T>(
                FoxRunRos2GeneratedContract contract,
                IFoxRunRos2NativeQosProfile qosProfile,
                Action<T> callback)
                where T : ROS2.Message, new()
            {
                RegisterCount++;
                NodeOwnershipAcquireCount++;
                Profile = qosProfile;
                DisposeCountAtRegistration = ((RecordingProfile)qosProfile).DisposeCount;
                return FoxRunRos2NativeBackendRegistration.Success(new RecordingToken());
            }

            public void RemoveSubscription(IFoxRunRos2NativeSubscriptionToken token)
                => RemoveCount++;

            public void ReleaseNodeOwnership()
                => NodeOwnershipReleaseCount++;
        }

        private sealed class RecordingToken : IFoxRunRos2NativeSubscriptionToken
        {
            public bool IsUsable => true;
        }

        private sealed class QosMessage : ROS2.Message, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }
    }
}
#endif
