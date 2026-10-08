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

            public bool ReleaseNodeOwnership()
            {
                NodeOwnershipReleaseCount++;
                return true;
            }
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
