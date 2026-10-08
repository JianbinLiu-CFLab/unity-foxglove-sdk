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
        public void TeardownFailureIsObservableAndLaterCleanupStillRuns()
        {
            var events = new List<string>();
            var backend = new FakeBackend(events)
            {
                RemoveException = new InvalidOperationException("remove exploded"),
                ReleaseException = new InvalidOperationException("release exploded")
            };
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                32,
                () => 32,
                value => applied = value,
                value =>
                {
                    events.Add("clear-applied");
                    applied = null;
                    return true;
                },
                dispose: value =>
                {
                    events.Add("dispose-owned");
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("owned");
            backend.Invoke(borrowed);
            Assert.True(binding.TryApplyLatest(32));

            binding.Stop();

            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
            Assert.True(binding.CleanupPending);
            Assert.Equal(0, backend.ReleaseCount);
            Assert.True(binding.TryGetSnapshot(32, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.TeardownFailure, snapshot.Error);
            Assert.Equal("The native ROS2 subscription did not complete teardown.", snapshot.Diagnostic);
            Assert.Equal(
                new[] { "remove-subscription", "clear-applied", "dispose-owned" },
                events);
            Assert.Equal(1, backend.RemoveCount);

            backend.RemoveException = null;
            backend.ReleaseException = null;
            binding.Stop();

            Assert.False(binding.CleanupPending);
            Assert.Equal(2, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal(
                new[]
                {
                    "remove-subscription",
                    "clear-applied",
                    "dispose-owned",
                    "remove-subscription",
                    "release-node"
                },
                events);
        }

        [Fact]
        public void DeferredNodeReleaseFailureIsRecordedAfterBlockedRegistrationCompletes()
        {
            using var registerEntered = new ManualResetEventSlim();
            using var releaseRegister = new ManualResetEventSlim();
            var backend = new FakeBackend
            {
                RegisterEntered = registerEntered,
                ReleaseRegister = releaseRegister,
                ReleaseException = new InvalidOperationException("deferred release")
            };
            var binding = CreateBinding(backend, 33, () => 33, _ => { }, _ => false);
            var registerThread = new Thread(() => binding.TryRegister()) { IsBackground = true };
            registerThread.Start();
            Assert.True(registerEntered.Wait(TimeSpan.FromSeconds(5)));
            binding.Stop();
            releaseRegister.Set();
            Assert.True(registerThread.Join(TimeSpan.FromSeconds(5)));

            Assert.True(binding.TryGetSnapshot(33, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.TeardownFailure, snapshot.Error);
            Assert.Equal("The native ROS2 subscription did not complete teardown.", snapshot.Diagnostic);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void NullOrNoOpTokenCanNeverReportRegistrationSuccess()
        {
            var backend = new FakeBackend
            {
                Next = FoxRunRos2NativeBackendRegistration.Success(null)
            };
            var binding = CreateBinding(backend, 1, () => 1, _ => { }, _ => false);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.InvalidSubscriptionToken, result.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);

            var events = new List<string>();
            var noOpBackend = new FakeBackend(events)
            {
                Next = FoxRunRos2NativeBackendRegistration.Success(new FakeToken(false))
            };
            var noOpBinding = CreateBinding(noOpBackend, 1, () => 1, _ => { }, _ => false);
            var noOpResult = noOpBinding.TryRegister();
            Assert.False(noOpResult.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.InvalidSubscriptionToken, noOpResult.Error);
            Assert.Equal(1, noOpBackend.RemoveCount);
            Assert.Equal(new[] { "remove-subscription" }, events);
        }

        [Fact]
        public void StaleGenerationCallbackAndDiagnosticsAreRejected()
        {
            var activeGeneration = 9L;
            var copied = 0;
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                9,
                () => activeGeneration,
                _ => { },
                _ => false,
                source =>
                {
                    copied++;
                    return Message(source.Data);
                });
            Assert.True(binding.TryRegister().Succeeded);
            Assert.True(binding.TryGetSnapshot(9, out var currentSnapshot));
            Assert.Equal(binding.ContractId, currentSnapshot.ContractId);
            activeGeneration = 10;

            using var borrowed = Message("stale");
            backend.Invoke(borrowed);

            Assert.Equal(0, copied);
            Assert.Equal(1, binding.StaleCallbackCount);
            Assert.False(binding.TryApplyLatest(9));
            Assert.False(binding.TryApplyLatest(activeGeneration));
            Assert.False(binding.TryGetSnapshot(9, out _));
            Assert.False(binding.TryGetSnapshot(activeGeneration, out _));
        }

        [Fact]
        public void GenerationProviderFailureRejectsDrainAndDiagnostics()
        {
            var throwProvider = false;
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                11,
                () => throwProvider ? throw new InvalidOperationException("generation") : 11,
                _ => { },
                _ => false);
            Assert.True(binding.TryRegister().Succeeded);
            throwProvider = true;

            Assert.False(binding.TryApplyLatest(11));
            Assert.False(binding.TryGetSnapshot(11, out _));
        }

        [Fact]
        public void StaleGenerationCannotRegisterANewEndpoint()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(backend, 4, () => 5, _ => { }, _ => false);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.StaleGeneration, result.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);
            Assert.Equal(0, backend.RegisterCount);
        }

        [Fact]
        public void CallbackCopyFailureAndLateCallbackNeverEscapeExecutor()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                3,
                () => 3,
                _ => { },
                _ => false,
                _ => throw new InvalidOperationException("copy failed"));
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("boom");

            var copyException = Record.Exception(() => backend.Invoke(borrowed));
            Assert.Null(copyException);
            Assert.Equal(1, binding.CopyFailedCount);

            binding.Stop();
            var lateException = Record.Exception(() => backend.InvokeLate(borrowed));
            Assert.Null(lateException);
            Assert.Equal(1, binding.RejectedAfterStopCount);
        }

        [Fact]
        public void FatalCallbackCopyFailurePassesThroughTheExecutorBoundary()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                3,
                () => 3,
                _ => { },
                _ => false,
                _ => throw new OutOfMemoryException("fatal"));
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("boom");

            Assert.Throws<OutOfMemoryException>(() => backend.Invoke(borrowed));
            Assert.Equal(1, binding.CopyFailedCount);

            binding.Stop();
        }

        [Fact]
        public void SelfOriginEnvelopeIsDroppedBeforeApplyWhileRemoteOriginStillApplies()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                3,
                () => 3,
                value => applied = value,
                _ => false,
                dropBeforeApply: value => value.Data == "self");
            Assert.True(binding.TryRegister().Succeeded);

            backend.Invoke(Message("self"));
            Assert.False(binding.TryApplyLatest(3));
            Assert.Null(applied);
            Assert.Equal(1, binding.SameOriginDropCount);

            backend.Invoke(Message("remote"));
            Assert.True(binding.TryApplyLatest(3));
            Assert.Equal("remote", applied.Data);

            binding.Stop();
        }

        [Fact]
        public void BorrowedCallbackReferenceCanNeverBecomeFrameworkOwned()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                3,
                () => 3,
                _ => { },
                _ => false,
                source => source);
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("borrowed");

            var exception = Record.Exception(() => backend.Invoke(borrowed));

            Assert.Null(exception);
            Assert.Equal(1, binding.CopyFailedCount);
            Assert.Equal(0, borrowed.DisposeCount);
            Assert.False(binding.TryApplyLatest(3));
            binding.Stop();
            Assert.Equal(0, borrowed.DisposeCount);
        }

        [Fact]
        public void StopIsIdempotentAndHonorsTransportOwnershipOrder()
        {
            var events = new List<string>();
            var backend = new FakeBackend(events);
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                5,
                () => 5,
                value => applied = value,
                value =>
                {
                    events.Add("clear-applied");
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                },
                source => Message(source.Data),
                value =>
                {
                    events.Add("dispose-" + value.Data);
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var first = Message("applied");
            using var second = Message("pending");
            backend.Invoke(first);
            Assert.True(binding.TryApplyLatest(5));
            backend.Invoke(second);

            binding.Stop();
            binding.Stop();

            Assert.Equal(
                new[]
                {
                    "remove-subscription",
                    "clear-applied",
                    "dispose-pending",
                    "dispose-applied",
                    "release-node"
                },
                events);
            Assert.Null(applied);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void RegistrationExceptionBecomesBoundedStableFailure()
        {
            var backend = new FakeBackend { RegistrationException = new InvalidOperationException(new string('z', 2048)) };
            var binding = CreateBinding(backend, 1, () => 1, _ => { }, _ => false);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, result.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);
            Assert.True(result.Diagnostic.Length <= FoxRunRos2RegistrationResult.MaximumDiagnosticLength);
        }
    }

}
#endif
