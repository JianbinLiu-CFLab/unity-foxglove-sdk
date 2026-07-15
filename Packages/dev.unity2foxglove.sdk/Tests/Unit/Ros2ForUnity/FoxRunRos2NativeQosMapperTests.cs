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
        [Fact]
        public void DefaultUsesR2fuDefaultWithKeepLastTen()
        {
            var factory = new RecordingFactory();

            var result = Ros2ForUnityNativeQosMapper.TryCreate(
                FoxRunRos2QosPreset.Default,
                factory,
                out var profile);

            Assert.True(result.Succeeded);
            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(ROS2.QosPresetProfile.DEFAULT, factory.Profile.Preset);
            Assert.Equal(ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST, factory.Profile.History);
            Assert.Equal(10, factory.Profile.Depth);
            Assert.Null(factory.Profile.Reliability);
            Assert.Null(factory.Profile.Durability);
            Assert.Equal(1, factory.Profile.SetHistoryCount);
            Assert.Equal(0, factory.Profile.SetPoliciesCount);
            profile.Dispose();
            Assert.Equal(1, factory.Profile.DisposeCount);
        }

        [Theory]
        [InlineData(
            FoxRunRos2QosPreset.Reliable,
            ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE,
            ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE,
            10)]
        [InlineData(
            FoxRunRos2QosPreset.SensorData,
            ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_BEST_EFFORT,
            ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE,
            5)]
        [InlineData(
            FoxRunRos2QosPreset.TransientLocal,
            ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE,
            ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_TRANSIENT_LOCAL,
            1)]
        public void ExplicitPresetsMapOneProfileWithExactPortablePolicies(
            FoxRunRos2QosPreset preset,
            ROS2.ReliabilityPolicy reliability,
            ROS2.DurabilityPolicy durability,
            int depth)
        {
            var factory = new RecordingFactory();

            var result = Ros2ForUnityNativeQosMapper.TryCreate(preset, factory, out var profile);

            Assert.True(result.Succeeded);
            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(ROS2.QosPresetProfile.DEFAULT, factory.Profile.Preset);
            Assert.Equal(ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST, factory.Profile.History);
            Assert.Equal(depth, factory.Profile.Depth);
            Assert.Equal(reliability, factory.Profile.Reliability);
            Assert.Equal(durability, factory.Profile.Durability);
            Assert.Equal(0, factory.Profile.SetHistoryCount);
            Assert.Equal(1, factory.Profile.SetPoliciesCount);
            profile.Dispose();
            Assert.Equal(1, factory.Profile.DisposeCount);
        }

        [Theory]
        [InlineData(FoxRunRos2QosPreset.Inherit)]
        [InlineData((FoxRunRos2QosPreset)99)]
        public void UnresolvedOrInvalidPresetFailsClosedWithoutCreatingAProfile(
            FoxRunRos2QosPreset preset)
        {
            var factory = new RecordingFactory();

            var result = Ros2ForUnityNativeQosMapper.TryCreate(preset, factory, out var profile);

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.UnsupportedQos, result.Error);
            Assert.Null(profile);
            Assert.Equal(0, factory.CreateCount);
        }

        [Fact]
        public void MissingRuntimePolicyFailsClosedAndDisposesPartialProfile()
        {
            var factory = new RecordingFactory
            {
                ConfigureException = new MissingMethodException("SetPolicies")
            };

            var result = Ros2ForUnityNativeQosMapper.TryCreate(
                FoxRunRos2QosPreset.SensorData,
                factory,
                out var profile);

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.UnsupportedQos, result.Error);
            Assert.Null(profile);
            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(1, factory.Profile.DisposeCount);
        }

        [Fact]
        public void BindingPassesOneConfiguredProfileToOneRegistrationThenDisposesIt()
        {
            var factory = new RecordingFactory();
            var backend = new RecordingBackend();
            var binding = CreateBinding(
                backend,
                FoxRunRos2QosPreset.SensorData,
                factory);

            var result = binding.TryRegister();

            Assert.True(result.Succeeded);
            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(1, backend.RegisterCount);
            Assert.Same(factory.Profile, backend.Profile);
            Assert.Equal(0, backend.DisposeCountAtRegistration);
            Assert.Equal(1, factory.Profile.DisposeCount);
            binding.Stop();
        }

        [Fact]
        public void ProfileDisposeFailureRollsBackTheSingleCreatedSubscription()
        {
            var factory = new RecordingFactory
            {
                DisposeException = new InvalidOperationException("dispose")
            };
            var backend = new RecordingBackend();
            var binding = CreateBinding(
                backend,
                FoxRunRos2QosPreset.Reliable,
                factory);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, result.Error);
            Assert.Equal(1, backend.RegisterCount);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, factory.Profile.DisposeCount);
            binding.Stop();
        }

        [Fact]
        public void UnsupportedQosCreatesNoCompatibilitySubscription()
        {
            var factory = new RecordingFactory();
            var backend = new RecordingBackend();
            var binding = CreateBinding(
                backend,
                FoxRunRos2QosPreset.Inherit,
                factory);

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
            FoxRunRos2QosPreset preset,
            RecordingFactory factory)
            => new FoxRunRos2SubscriptionBinding<QosMessage>(
                new FoxRunRos2GeneratedContract(
                    "qos-contract",
                    "/qos",
                    "Demo.QosReceiver",
                    "_message",
                    "std_msgs/msg/String",
                    "Ros2Native",
                    preset.ToString()),
                1,
                () => 1,
                1024,
                (source, _) => new QosMessage(),
                message => message.Dispose(),
                _ => { },
                _ => false,
                backend,
                preset,
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
                ThrowIfConfigured();
                History = history;
                Depth = depth;
            }

            public void SetPolicies(
                ROS2.HistoryPolicy history,
                int depth,
                ROS2.ReliabilityPolicy reliability,
                ROS2.DurabilityPolicy durability)
            {
                SetPoliciesCount++;
                ThrowIfConfigured();
                History = history;
                Depth = depth;
                Reliability = reliability;
                Durability = durability;
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
            public int RemoveCount { get; private set; }
            public int DisposeCountAtRegistration { get; private set; }
            public IFoxRunRos2NativeQosProfile Profile { get; private set; }

            public FoxRunRos2NativeBackendRegistration Register<T>(
                FoxRunRos2GeneratedContract contract,
                IFoxRunRos2NativeQosProfile qosProfile,
                Action<T> callback)
                where T : ROS2.Message, new()
            {
                RegisterCount++;
                Profile = qosProfile;
                DisposeCountAtRegistration = ((RecordingProfile)qosProfile).DisposeCount;
                return FoxRunRos2NativeBackendRegistration.Success(new RecordingToken());
            }

            public void RemoveSubscription(IFoxRunRos2NativeSubscriptionToken token)
                => RemoveCount++;
            public void ReleaseNodeOwnership() { }
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
