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
    public sealed partial class FoxRunRos2NativeSubscriptionAdapterTests
    {
        [Fact]
        public void ConcurrentRegisterStopAndSnapshotNeverExposeTornOutcome()
        {
            using var removeEntered = new ManualResetEventSlim();
            using var releaseRemove = new ManualResetEventSlim();
            var backend = new FakeBackend
            {
                RemoveEntered = removeEntered,
                ReleaseRemove = releaseRemove
            };
            var binding = CreateBinding(backend, 14, () => 14, _ => { }, _ => false);
            Assert.True(binding.TryRegister().Succeeded);
            var snapshots = new ConcurrentQueue<FoxRunRos2SubscriptionBindingSnapshot>();
            var stopThread = new Thread(binding.Stop);
            stopThread.Start();
            Assert.True(removeEntered.Wait(TimeSpan.FromSeconds(10)));
            var registerResult = default(FoxRunRos2RegistrationResult);
            var registerThread = new Thread(() => registerResult = binding.TryRegister());
            registerThread.Start();
            Exception snapshotFailure = null;
            var snapshotThread = new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < 100; i++)
                    {
                        if (binding.TryGetSnapshot(14, out var snapshot))
                            snapshots.Enqueue(snapshot);
                    }
                }
                catch (Exception exception)
                {
                    snapshotFailure = exception;
                }
            });
            snapshotThread.Start();
            releaseRemove.Set();
            Assert.True(stopThread.Join(TimeSpan.FromSeconds(10)));
            Assert.True(registerThread.Join(TimeSpan.FromSeconds(10)));
            Assert.True(snapshotThread.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(snapshotFailure);
            Assert.False(registerResult.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.Stopped, registerResult.Error);
            Assert.All(snapshots, snapshot =>
            {
                var coherentReady = (snapshot.State == FoxRunRos2SubscriptionBindingState.Ready
                                     || snapshot.State == FoxRunRos2SubscriptionBindingState.Receiving)
                                    && snapshot.Error == FoxRunRos2RegistrationError.None;
                var coherentStopped = snapshot.State == FoxRunRos2SubscriptionBindingState.Stopped
                                      && snapshot.Error == FoxRunRos2RegistrationError.Stopped;
                Assert.True(coherentReady || coherentStopped);
            });
        }

        [Fact]
        public void ApplyFailureBecomesTerminalAndPreservesPrimaryDiagnosticThroughTeardown()
        {
            FakeMessage owned = null;
            var copies = 0;
            var backend = new FakeBackend
            {
                RemoveException = new InvalidOperationException("secondary remove failure")
            };
            var binding = CreateBinding(
                backend,
                15,
                () => 15,
                _ => throw new InvalidOperationException("setter exploded"),
                _ => false,
                source =>
                {
                    copies++;
                    owned = Message(source.Data);
                    return owned;
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("first");
            backend.Invoke(borrowed);

            var failure = Assert.Throws<InvalidOperationException>(() => binding.TryApplyLatest(15));
            binding.RecordApplyFailure(failure);

            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);
            Assert.True(binding.TryGetSnapshot(15, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.ApplyFailure, snapshot.Error);
            Assert.Equal("The native ROS2 subscription could not apply the copied message.", snapshot.Diagnostic);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(0, backend.ReleaseCount);
            Assert.Equal(1, owned.DisposeCount);

            using var late = Message("late");
            backend.InvokeLate(late);
            Assert.Equal(1, copies);
            Assert.Equal(1, binding.RejectedAfterStopCount);
            Assert.False(binding.TryApplyLatest(15));
            backend.RemoveException = null;
            binding.Stop();
            Assert.Equal(2, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);
        }

        [Fact]
        public void NativeTransportAdmissionPreservesTheNewestEligibleSampleBeforeDeepCopy()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var copies = 0;
            var interval = (Stopwatch.Frequency + 1L) / 2L;
            var timestamps = new Queue<long>(new[]
            {
                100L,
                100L + Math.Max(1L, interval / 2L),
                100L + interval,
            });
            var binding = CreateBinding(
                backend,
                182,
                () => 182,
                value => applied = value,
                value => ReferenceEquals(applied, value),
                copy: value =>
                {
                    copies++;
                    return Message(value.Data);
                },
                transportAdmissionRateLimitHz: 2,
                admissionTimestamp: () => timestamps.Dequeue());
            binding.WaitForRuntime();
            Assert.True(binding.TryRegister().Succeeded);

            using var first = Message("first");
            using var rejected = Message("rejected-before-copy");
            using var newest = Message("newest");
            backend.Invoke(first);
            backend.Invoke(rejected);
            backend.Invoke(newest);

            Assert.Equal(2, copies);
            Assert.Equal(1, binding.TransportAdmissionDropCount);
            Assert.True(binding.TryApplyLatest(182, 0d));
            Assert.Equal("newest", applied.Data);
            binding.Stop();
        }

        [Fact]
        public void ChangePolicyDropsEqualNativeValuesAndAppliesChanges()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                183,
                () => 183,
                value => applied = value,
                value => ReferenceEquals(applied, value),
                contract: PolicyContract(Unity.FoxgloveSDK.Components.FoxRunPolicy.Change),
                valuesEqual: (left, right) => left.Data == right.Data);
            binding.WaitForRuntime();
            Assert.True(binding.TryRegister().Succeeded);

            backend.Invoke(Message("first"));
            Assert.True(binding.TryApplyLatest(183, 0d));
            backend.Invoke(Message("first"));
            Assert.False(binding.TryApplyLatest(183, 1d));
            backend.Invoke(Message("changed"));
            Assert.True(binding.TryApplyLatest(183, 2d));
            Assert.Equal("changed", applied.Data);
            binding.Stop();
        }

        [Fact]
        public void NativeOnlyIfDropsPendingAndInvalidatesSemanticHistoryUntilRecovery()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var condition = true;
            var binding = CreateBinding(
                backend,
                186,
                () => 186,
                value => applied = value,
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                },
                contract: PolicyContract(Unity.FoxgloveSDK.Components.FoxRunPolicy.Change),
                valuesEqual: (left, right) => left.Data == right.Data,
                canApply: () => condition);
            binding.WaitForRuntime();
            Assert.True(binding.TryRegister().Succeeded);

            backend.Invoke(Message("same"));
            Assert.True(binding.TryApplyLatest(186, 0d));
            Assert.Equal(1, binding.AppliedCount);
            Assert.Equal("same", applied.Data);

            condition = false;
            backend.Invoke(Message("same"));
            Assert.False(binding.TryApplyLatest(186, 1d));
            Assert.Equal(1, binding.AppliedCount);
            Assert.Equal("same", applied.Data);

            condition = true;
            backend.Invoke(Message("same"));
            Assert.True(binding.TryApplyLatest(186, 2d));
            Assert.Equal(2, binding.AppliedCount);
            Assert.Equal("same", applied.Data);
            binding.Stop();
        }

        [Fact]
        public void ChangeWithHeartbeatDefersFreshDuplicateUntilItsInterval()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                184,
                () => 184,
                value => applied = value,
                value => ReferenceEquals(applied, value),
                contract: PolicyContract(
                    Unity.FoxgloveSDK.Components.FoxRunPolicy.Change,
                    heartbeatIntervalSeconds: 2f),
                valuesEqual: (left, right) => left.Data == right.Data);
            binding.WaitForRuntime();
            Assert.True(binding.TryRegister().Succeeded);

            backend.Invoke(Message("same"));
            Assert.True(binding.TryApplyLatest(184, 0d));
            backend.Invoke(Message("same"));
            Assert.False(binding.TryApplyLatest(184, 1d));
            Assert.True(binding.TryApplyLatest(184, 2d));
            binding.Stop();
        }

        [Fact]
        public void TriggerPolicyKeepsOnlyNewestNativeValueUntilExplicitApply()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var trigger = false;
            var binding = CreateBinding(
                backend,
                185,
                () => 185,
                value => applied = value,
                value => ReferenceEquals(applied, value),
                contract: PolicyContract(Unity.FoxgloveSDK.Components.FoxRunPolicy.Trigger),
                valuesEqual: (left, right) => left.Data == right.Data,
                consumeTrigger: () =>
                {
                    var requested = trigger;
                    trigger = false;
                    return requested;
                });
            binding.WaitForRuntime();
            Assert.True(binding.TryRegister().Succeeded);

            backend.Invoke(Message("first"));
            Assert.False(binding.TryApplyLatest(185, 0d));
            backend.Invoke(Message("latest"));
            trigger = true;
            Assert.True(binding.TryApplyLatest(185, 1d));
            Assert.Equal("latest", applied.Data);
            binding.Stop();
        }

        private static FoxRunRos2SubscriptionBinding<FakeMessage> CreateBinding(
            FakeBackend backend,
            long generation,
            Func<long> activeGeneration,
            Action<FakeMessage> apply,
            Func<FakeMessage, bool> clearIfOwned,
            Func<FakeMessage, FakeMessage> copy = null,
            Action<FakeMessage> dispose = null,
            FoxRunRos2GeneratedContract contract = null,
            Func<FakeMessage, FakeMessage, bool> valuesEqual = null,
            Func<bool> consumeTrigger = null,
            Func<bool> canApply = null,
            int transportAdmissionRateLimitHz = int.MaxValue,
            Func<long> admissionTimestamp = null,
            Func<FakeMessage, bool> dropBeforeApply = null)
        {
            return new FoxRunRos2SubscriptionBinding<FakeMessage>(
                contract ?? Contract(),
                generation,
                activeGeneration,
                4L * 1024L * 1024L,
                (source, _) => (copy ?? (value => Message(value.Data)))(source),
                dispose ?? (value => value.Dispose()),
                apply,
                clearIfOwned,
                backend,
                FoxRunResolvedQos.Default,
                new ManagedQosFactory(),
                valuesEqual: valuesEqual,
                consumeTrigger: consumeTrigger,
                canApply: canApply,
                dropBeforeApply: dropBeforeApply,
                transportAdmissionRateLimitHz: transportAdmissionRateLimitHz,
                admissionTimestamp: admissionTimestamp);
        }

        private static FoxRunRos2GeneratedContract PolicyContract(
            Unity.FoxgloveSDK.Components.FoxRunPolicy policy,
            float heartbeatIntervalSeconds = 0f)
            => new FoxRunRos2GeneratedContract(
                "policy-contract-" + policy,
                "/native/policy",
                "Demo.Receiver",
                "_incoming",
                "std_msgs/msg/String",
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
                supportsRos2Native: true,
                policy: policy,
                hz: 0f,
                hasExplicitHz: false,
                heartbeatIntervalSeconds: heartbeatIntervalSeconds);

        private static FoxRunRos2GeneratedContract Contract()
            => new FoxRunRos2GeneratedContract(
                "contract-1",
                "/native/string",
                "Demo.Receiver",
                "_incoming",
                "std_msgs/msg/String",
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

        private static FakeMessage Message(string value)
            => new FakeMessage { Data = value };

    }
}
#endif
