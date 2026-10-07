// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Locks closed-generic custom native publisher backend ownership.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{


public sealed partial class FoxRunRos2CustomPublisherBackendTests
    {
        private static FoxRunRos2CustomPublisherContract Contract()
            => new FoxRunRos2CustomPublisherContract(
                "publisher-contract",
                "/phase181/outbound",
                "Phase181.Source",
                "State",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
                "dev.unity2foxglove.foxrun.ros2.interfaces",
                "unity2foxglove_foxrun_interfaces_v1",
                1,
                "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd",
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                FoxRunFlow.Publish,
                FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false);

        private sealed class TestEnvelope : ROS2.Message, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class FakeNodeDriver : IFoxRunRos2R2fuNodeDriver
        {
            public int CreatePublisherCount { get; private set; }
            public int CreateSubscriptionCount { get; private set; }
            public int RemovePublisherCount { get; private set; }
            public int PublishCount { get; private set; }
            public int ReleaseNodeCount { get; private set; }
            public int ReleaseThreadId { get; private set; }
            public bool PublisherUsable { get; set; } = true;
            public int PublisherRemovalExceptionsRemaining { get; set; }
            public bool PublisherRemovalReturnsFalse { get; set; }
            public int ReleaseFailuresRemaining { get; set; }
            public int ReleaseExceptionsRemaining { get; set; }
            public bool BlockRelease { get; set; }
            public ManualResetEventSlim ReleaseStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ContinueRelease { get; } = new ManualResetEventSlim(false);
            public Exception PublisherFailure { get; set; }
            public Exception PublisherUsabilityFailure { get; set; }
            public ROS2.QualityOfServiceProfile LastPublisherQos { get; private set; }

            public object CreateSubscription<T>(string topic, Action<T> callback, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
            {
                CreateSubscriptionCount++;
                return new object();
            }

            public bool IsSubscriptionUsable(object subscription) => subscription != null;
            public bool RemoveSubscription(object subscription) => true;

            public object CreatePublisher<T>(string topic, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
            {
                CreatePublisherCount++;
                LastPublisherQos = qos;
                if (PublisherFailure != null)
                    throw PublisherFailure;
                return new object();
            }

            public bool IsPublisherUsable<T>(object publisher)
                where T : ROS2.Message, new()
            {
                if (PublisherUsabilityFailure != null)
                    throw PublisherUsabilityFailure;
                return PublisherUsable && publisher != null;
            }

            public bool Publish<T>(object publisher, T message)
                where T : ROS2.Message, new()
            {
                if (publisher == null || message == null)
                    return false;
                PublishCount++;
                return true;
            }

            public bool RemovePublisher<T>(object publisher)
                where T : ROS2.Message, new()
            {
                if (publisher == null)
                    return false;
                RemovePublisherCount++;
                if (PublisherRemovalExceptionsRemaining > 0)
                {
                    PublisherRemovalExceptionsRemaining--;
                    throw new InvalidOperationException("publisher removal pending");
                }
                return !PublisherRemovalReturnsFalse;
            }

            public bool ReleaseNode()
            {
                ReleaseNodeCount++;
                ReleaseThreadId = Thread.CurrentThread.ManagedThreadId;
                if (BlockRelease)
                {
                    ReleaseStarted.Set();
                    if (!ContinueRelease.Wait(TimeSpan.FromSeconds(5)))
                        return false;
                    BlockRelease = false;
                }
                if (ReleaseFailuresRemaining > 0)
                {
                    ReleaseFailuresRemaining--;
                    return false;
                }
                if (ReleaseExceptionsRemaining > 0)
                {
                    ReleaseExceptionsRemaining--;
                    throw new InvalidOperationException("node release pending");
                }
                return true;
            }
        }

        private sealed class QueuedSynchronizationContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _pending =
                new ConcurrentQueue<(SendOrPostCallback Callback, object State)>();

            public int PendingCount => _pending.Count;

            public override void Post(SendOrPostCallback callback, object state)
                => _pending.Enqueue((callback, state));

            public void RunPending()
            {
                while (_pending.TryDequeue(out var work))
                    work.Callback(work.State);
            }
        }

        private sealed class ManagedQosFactory : IFoxRunRos2NativeQosProfileFactory
        {
            public List<ManagedQosProfile> Created { get; } = new List<ManagedQosProfile>();
            public Exception DisposeFailure { get; set; }

            public IFoxRunRos2NativeQosProfile Create(ROS2.QosPresetProfile preset)
            {
                var profile = new ManagedQosProfile(DisposeFailure);
                Created.Add(profile);
                return profile;
            }
        }

        private sealed class ManagedQosProfile : IFoxRunRos2NativeQosProfile
        {
            private readonly Exception _disposeFailure;

            internal ManagedQosProfile(Exception disposeFailure = null)
            {
                _disposeFailure = disposeFailure;
            }

            public ROS2.QualityOfServiceProfile NativeProfile => null;
            public ROS2.HistoryPolicy History { get; private set; }
            public int Depth { get; private set; }
            public ROS2.ReliabilityPolicy Reliability { get; private set; }
            public ROS2.DurabilityPolicy Durability { get; private set; }
            public bool IsDisposed { get; private set; }

            public void SetHistory(ROS2.HistoryPolicy history, int depth)
            {
                History = history;
                Depth = depth;
            }

            public void SetPolicies(
                ROS2.HistoryPolicy history,
                int depth,
                ROS2.ReliabilityPolicy reliability,
                ROS2.DurabilityPolicy durability)
            {
                History = history;
                Depth = depth;
                Reliability = reliability;
                Durability = durability;
            }

            public void Dispose()
            {
                IsDisposed = true;
                if (_disposeFailure != null)
                    throw _disposeFailure;
            }
        }
    }
}
#endif
