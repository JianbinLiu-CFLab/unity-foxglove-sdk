// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Verify typed native subscription binding lifecycle and ownership.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    public sealed partial class FoxRunRos2SubscriptionHostTests
    {
        private static FoxRunRos2SubscriptionBindingSnapshot Snapshot(
            string id,
            FoxRunRos2SubscriptionBindingState state,
            FoxRunRos2RegistrationError error,
            string diagnostic,
            long received = 0)
            => new FoxRunRos2SubscriptionBindingSnapshot(
                id,
                1,
                state,
                error,
                diagnostic,
                received,
                0,
                0,
                0,
                0,
                0,
                0);

        private sealed class FakeHostMessage : ROS2.Message, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class FakeHostBinding : IFoxRunRos2HostBinding
        {
            private readonly Func<bool> _apply;
            private FoxRunRos2RegistrationResult _result = FoxRunRos2RegistrationResult.Success();

            internal FakeHostBinding(string id, Func<bool> apply)
            {
                Contract = new FoxRunRos2GeneratedContract(
                    id, "/" + id, "Demo.Host", "_message", "std_msgs/msg/String",
                    Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
                    FoxRunRos2RouteEndpoint.R2fu,
                    FoxRunQosProfile.Default,
                    hasExplicitQosProfile: true,
                    qosReliability: default,
                    hasExplicitQosReliability: false,
                    qosDurability: default,
                    hasExplicitQosDurability: false,
                    qosHistory: default,
                    hasExplicitQosHistory: false,
                    qosDepth: 0,
                    hasExplicitQosDepth: false,
                    supportsRos2Native: true);
                _apply = apply;
                State = FoxRunRos2SubscriptionBindingState.Ready;
            }

            public FoxRunRos2GeneratedContract Contract { get; }
            public string ContractId => Contract.Id;
            public long SessionGeneration => 1;
            public FoxRunRos2SubscriptionBindingState State { get; private set; }
            public bool CanRetryRegistration => false;
            public FoxRunRos2RegistrationResult TryRegister() => _result;
            public bool TryApplyLatest(long activeSessionGeneration) => _apply();
            public void RecordApplyFailure(Exception exception)
            {
                State = FoxRunRos2SubscriptionBindingState.Failed;
                _result = FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.ApplyFailure,
                    exception.GetType().Name + ": " + exception.Message);
            }
            public bool TryGetSnapshot(long activeSessionGeneration, out FoxRunRos2SubscriptionBindingSnapshot snapshot)
            {
                snapshot = Snapshot(ContractId, State, _result.Error, _result.Diagnostic);
                return activeSessionGeneration == SessionGeneration;
            }
            public FoxRunRos2AcceptanceArmStatus ArmAcceptanceAttempt(
                out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
            {
                snapshot = default;
                return FoxRunRos2AcceptanceArmStatus.EndpointUnavailable;
            }
            public bool TryGetAcceptanceAttempt(out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
            {
                snapshot = default;
                return false;
            }
            public bool EndAcceptanceAttempt(long epoch) => false;
            public bool TryCompleteAcceptanceAttempt(
                long epoch,
                out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
            {
                snapshot = default;
                return false;
            }
            public void Stop() => State = FoxRunRos2SubscriptionBindingState.Stopped;
        }

        private sealed class NativeOnlySource : UnityEngine.MonoBehaviour, IFoxRunRos2SubscriptionSource
        {
            public int FoxRunRos2SubscriptionCount => 1;

            public void FoxRunRos2RegisterSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar)
            {
            }
        }

        private sealed class CustomNativeOnlySource : UnityEngine.MonoBehaviour, IFoxRunRos2CustomSubscriptionSource
        {
            public int FoxRunRos2CustomSubscriptionCount => 1;

            public void FoxRunRos2RegisterCustomSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar)
            {
            }
        }

        private sealed class HostManagedQosProfile : IFoxRunRos2NativeQosProfile
        {
            public ROS2.QualityOfServiceProfile NativeProfile { get; } = null;
            public void SetHistory(ROS2.HistoryPolicy history, int depth) { }
            public void SetPolicies(ROS2.HistoryPolicy history, int depth, ROS2.ReliabilityPolicy reliability, ROS2.DurabilityPolicy durability) { }
            public void Dispose() { }
        }

        private sealed class FakeR2fuNodeDriver : IFoxRunRos2R2fuNodeDriver
        {
            public int CreateCount { get; private set; }
            public int RemoveCount { get; private set; }
            public int ReleaseNodeCount { get; private set; }
            public int ReleaseFailuresRemaining { get; set; }
            public List<ROS2.QualityOfServiceProfile> SeenQos { get; } = new List<ROS2.QualityOfServiceProfile>();
            public bool RemoveReturns { get; set; } = true;

            public object CreateSubscription<T>(string topic, Action<T> callback, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
            {
                CreateCount++;
                SeenQos.Add(qos);
                return new object();
            }

            public bool IsSubscriptionUsable(object subscription) => subscription != null;

            public bool RemoveSubscription(object subscription)
            {
                Assert.NotNull(subscription);
                RemoveCount++;
                return RemoveReturns;
            }

            public object CreatePublisher<T>(string topic, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
                => new object();

            public bool IsPublisherUsable<T>(object publisher)
                where T : ROS2.Message, new()
                => publisher != null;

            public bool Publish<T>(object publisher, T message)
                where T : ROS2.Message, new()
                => publisher != null && message != null;

            public bool RemovePublisher<T>(object publisher)
                where T : ROS2.Message, new()
                => publisher != null;

            public bool ReleaseNode()
            {
                ReleaseNodeCount++;
                if (ReleaseFailuresRemaining > 0)
                {
                    ReleaseFailuresRemaining--;
                    return false;
                }
                return true;
            }
        }
    }
}
#endif
