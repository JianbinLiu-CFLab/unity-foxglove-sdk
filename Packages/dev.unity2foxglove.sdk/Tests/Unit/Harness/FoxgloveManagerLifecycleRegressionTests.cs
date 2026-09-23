// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Manager lifecycle closure regression contracts.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class FoxgloveManagerLifecycleRegressionTests
    {
        [Fact]
        public void ComponentSessionAdvancesBeforeCaptureAndUsesActiveGeneration()
        {
            var state = new ComponentPublisherSessionLifecycleState<string>();
            var calls = new List<string>();
            var generation = 7UL;

            var returnedGeneration = state.AdvanceActivateAndCapture(
                () =>
                {
                    calls.Add("advance");
                    return ++generation;
                },
                activatedGeneration => calls.Add("activate:" + activatedGeneration),
                capturedGeneration =>
                {
                    calls.Add("capture:" + capturedGeneration);
                    return "snapshot:" + capturedGeneration;
                });

            Assert.Equal(8UL, returnedGeneration);
            Assert.Equal("snapshot:8", state.ActiveSession);
            Assert.Equal(new[] { "advance", "activate:8", "capture:8" }, calls);

            var restartedGeneration = state.AdvanceActivateAndCapture(
                () => ++generation,
                activatedGeneration => calls.Add("activate:" + activatedGeneration),
                capturedGeneration => "snapshot:" + capturedGeneration);

            Assert.Equal(9UL, restartedGeneration);
            Assert.Equal("snapshot:9", state.ActiveSession);
        }

        [Fact]
        public void ComponentSessionIsClearedAfterStopFailureTail()
        {
            var state = new ComponentPublisherSessionLifecycleState<object>();
            state.Set(new object());

            var failure = Assert.Throws<InvalidOperationException>(() =>
                FoxgloveManagerTeardownState.RunStopServer(
                    () => throw new InvalidOperationException("runtime-stop"),
                    () => { },
                    () => { },
                    () => { },
                    () => { },
                    () => { },
                    () => { },
                    () => { },
                    state.Clear));

            Assert.Equal("runtime-stop", failure.Message);
            Assert.Null(state.ActiveSession);
        }

        [Fact]
        public void ComponentSessionIsClearedAfterStartupFailureCleanup()
        {
            var state = new ComponentPublisherSessionLifecycleState<object>();
            state.Set(new object());

            var failure = FoxgloveManagerTeardownState.RunCleanupReturningFirstFailure(
                () => throw new InvalidOperationException("startup-failure"),
                state.Clear);

            Assert.NotNull(failure);
            Assert.Equal("startup-failure", failure.SourceException.Message);
            Assert.Null(state.ActiveSession);
        }

        [Fact]
        public void ReplayMessageFanoutContinuesAfterSubscriberFailure()
        {
            var fanout = new ReplaySubscriberFanoutState<Action<string, byte[]>>();
            var received = new List<string>();
            var failures = new List<string>();
            fanout.Add((_, _) => throw new InvalidOperationException("message-first"));
            fanout.Add((topic, _) => received.Add(topic));

            fanout.Invoke(
                handler => handler("/replay", new byte[] { 1 }),
                exception => failures.Add(exception.Message));

            Assert.Equal(new[] { "/replay" }, received);
            Assert.Equal(new[] { "message-first" }, failures);
        }

        [Fact]
        public void ReplayContextAndBatchFanoutsContinueAfterSubscriberFailure()
        {
            var contextFanout = new ReplaySubscriberFanoutState<Action<int>>();
            var batchFanout = new ReplaySubscriberFanoutState<Action<int>>();
            var received = new List<int>();
            var failures = new List<string>();
            contextFanout.Add(_ => throw new InvalidOperationException("context-first"));
            contextFanout.Add(value => received.Add(value));
            batchFanout.Add(_ => throw new InvalidOperationException("batch-first"));
            batchFanout.Add(value => received.Add(value));

            contextFanout.Invoke(handler => handler(3), exception => failures.Add(exception.Message));
            batchFanout.Invoke(handler => handler(5), exception => failures.Add(exception.Message));

            Assert.Equal(new[] { 3, 5 }, received);
            Assert.Equal(new[] { "context-first", "batch-first" }, failures);
        }

        [Fact]
        public void ReplayFanoutPreservesDelegateDuplicateAndRemoveSemantics()
        {
            var fanout = new ReplaySubscriberFanoutState<Action<int>>();
            var calls = 0;
            Action<int> subscriber = _ => calls++;
            fanout.Add(subscriber);
            fanout.Add(subscriber);
            fanout.Remove(subscriber);

            fanout.Invoke(handler => handler(1), _ => { });

            Assert.Equal(1, fanout.Count);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void ReplayFanoutFlattensCombinedDelegatesBeforeIsolation()
        {
            var fanout = new ReplaySubscriberFanoutState<Action<int>>();
            var received = new List<int>();
            var failures = new List<string>();
            Action<int> first = _ => throw new InvalidOperationException("combined-first");
            Action<int> second = value => received.Add(value);

            fanout.Add(first + second);
            fanout.Invoke(handler => handler(7), exception => failures.Add(exception.Message));

            Assert.Equal(new[] { 7 }, received);
            Assert.Equal(new[] { "combined-first" }, failures);
            Assert.Equal(2, fanout.Count);

            fanout.Remove(first + second);
            Assert.Equal(0, fanout.Count);
        }

        [Fact]
        public void ReplayDisableOwnershipDoesNotRestorePreDisabledPublisher()
        {
            var ownership = new ReplayDisableOwnershipState();
            var enabled = false;
            var disableCalls = 0;
            var enableCalls = 0;

            Assert.False(ownership.TryAcquire(() => enabled, () =>
            {
                disableCalls++;
                enabled = false;
            }));
            Assert.False(ownership.TryRestore(() => enabled, () =>
            {
                enableCalls++;
                enabled = true;
            }));
            Assert.Equal(0, disableCalls);
            Assert.Equal(0, enableCalls);
        }

        [Fact]
        public void ReplayDisableOwnershipRestoresOnlyWhenStillOwned()
        {
            var ownership = new ReplayDisableOwnershipState();
            var enabled = true;
            var disableCalls = 0;
            var enableCalls = 0;

            Assert.True(ownership.TryAcquire(() => enabled, () =>
            {
                disableCalls++;
                enabled = false;
            }));
            Assert.False(enabled);
            Assert.True(ownership.HasOwnership);
            Assert.True(ownership.TryRestore(() => enabled, () =>
            {
                enableCalls++;
                enabled = true;
            }));
            Assert.True(enabled);
            Assert.False(ownership.HasOwnership);
            Assert.Equal(1, disableCalls);
            Assert.Equal(1, enableCalls);

            enabled = true;
            Assert.True(ownership.TryAcquire(() => enabled, () => enabled = false));
            enabled = true;
            ownership.NotifyExternalLifecycleTransition();
            Assert.False(ownership.TryRestore(() => enabled, () => enableCalls++));
            Assert.Equal(1, enableCalls);

            enabled = true;
            Assert.True(ownership.TryAcquire(() => enabled, () => enabled = false));
            enabled = false;
            ownership.NotifyExternalLifecycleTransition();
            Assert.False(ownership.TryRestore(() => enabled, () =>
            {
                enableCalls++;
                enabled = true;
            }));
            Assert.False(enabled);
            Assert.Equal(1, enableCalls);
        }

        [Fact]
        public void RetryBackoffBlocksBusyLoopAndAllowsDeadlineRetry()
        {
            var remote = new RetryBackoffState();
            var cursor = new RetryBackoffState();

            remote.RecordFailure(100d, 1d);
            cursor.RecordFailure(100d, 1d);

            Assert.False(remote.HasSuccessfulConfiguration);
            Assert.False(cursor.HasSuccessfulConfiguration);
            Assert.True(remote.IsBlocked(100d));
            Assert.True(cursor.IsBlocked(100.999d));
            Assert.False(remote.IsBlocked(101d));
            Assert.False(cursor.IsBlocked(101d));

            remote.RecordSuccess();
            cursor.RecordSuccess();
            Assert.True(remote.HasSuccessfulConfiguration);
            Assert.True(cursor.HasSuccessfulConfiguration);
            Assert.False(remote.IsBlocked(0d));
            Assert.False(cursor.IsBlocked(0d));
        }
    }
}
