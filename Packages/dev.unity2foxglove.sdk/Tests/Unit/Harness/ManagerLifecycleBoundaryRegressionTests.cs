// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Domain", "ManagerLifecycle")]
    public sealed class ManagerLifecycleBoundaryRegressionTests
    {
        [Fact]
        public void ComponentCaptureResolvesPublisherBeforePublisherEnableLifecycle()
        {
            var manager = new object();
            var publisher = new DeferredManagerPublisher(manager);

            var selected = ComponentPublisherSessionCaptureState
                .SelectOwned(
                    new[] { publisher },
                    candidate => ReferenceEquals(candidate.ResolveManagerForComponentSession(), manager),
                    candidate => candidate.HasValidTopic)
                .ToArray();

            Assert.Single(selected);
            Assert.Same(publisher, selected[0]);
            Assert.Equal(1, publisher.ResolveCalls);
            Assert.Same(manager, publisher.ConfiguredManager);
        }

        [Fact]
        public void ComponentSnapshotGenerationMatchesActiveRuntimeGenerationAcrossRestart()
        {
            var publisher = new object();
            var state = new ComponentPublisherSessionLifecycleState<ComponentPublisherSessionSnapshot>();
            var generation = 40UL;
            var activeGeneration = 0UL;

            var first = state.AdvanceActivateAndCapture(
                () => ++generation,
                value => activeGeneration = value,
                value => BuildSnapshot(value, publisher));

            Assert.Equal(activeGeneration, first);
            Assert.Equal(activeGeneration, state.ActiveSession.Generation);

            var second = state.AdvanceActivateAndCapture(
                () => ++generation,
                value => activeGeneration = value,
                value => BuildSnapshot(value, publisher));

            Assert.True(second > first);
            Assert.Equal(activeGeneration, second);
            Assert.Equal(activeGeneration, state.ActiveSession.Generation);
        }

        [Fact]
        public void ActiveComponentSessionIsClearedAfterRuntimeStopFailure()
        {
            var state = new ComponentPublisherSessionLifecycleState<ComponentPublisherSessionSnapshot>();
            state.Set(BuildSnapshot(7, new object()));

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
        public void ActiveComponentSessionIsClearedAfterStartupFailure()
        {
            var state = new ComponentPublisherSessionLifecycleState<ComponentPublisherSessionSnapshot>();
            state.Set(BuildSnapshot(8, new object()));

            var failure = FoxgloveManagerTeardownState.RunCleanupReturningFirstFailure(
                () => throw new InvalidOperationException("startup-failure"),
                state.Clear);

            Assert.NotNull(failure);
            Assert.Equal("startup-failure", failure.SourceException.Message);
            Assert.Null(state.ActiveSession);
        }

        [Fact]
        public void ManagerReplayMessageEventContinuesAfterFirstSubscriberThrows()
        {
            var boundary = new ReplayMessageBoundary();
            var received = new List<string>();
            var failures = new List<string>();
            boundary.OnReplayMessage += (_, _) => throw new InvalidOperationException("message-first");
            boundary.OnReplayMessage += (topic, _) => received.Add(topic);

            boundary.Dispatch("/replay", new byte[] { 1 }, exception => failures.Add(exception.Message));

            Assert.Equal(new[] { "/replay" }, received);
            Assert.Equal(new[] { "message-first" }, failures);
        }

        [Fact]
        public void ManagerReplayContextAndBatchEventsContinueAfterSubscriberThrows()
        {
            var context = new ReplayContextBoundary();
            var batch = new ReplayBatchBoundary();
            var received = new List<int>();
            var failures = new List<string>();
            context.OnReplayMessageContext += _ => throw new InvalidOperationException("context-first");
            context.OnReplayMessageContext += value => received.Add(value);
            batch.OnReplayBatchCompleted += _ => throw new InvalidOperationException("batch-first");
            batch.OnReplayBatchCompleted += value => received.Add(value);

            context.Dispatch(3, exception => failures.Add(exception.Message));
            batch.Dispatch(5, exception => failures.Add(exception.Message));

            Assert.Equal(new[] { 3, 5 }, received);
            Assert.Equal(new[] { "context-first", "batch-first" }, failures);
        }

        [Fact]
        public void PublisherReplayRestoreDoesNotOverrideExternalLifecycleOwnership()
        {
            var boundary = new ReplayPublisherBoundary(enabled: true);

            Assert.True(boundary.TryDisableForReplay());
            Assert.True(boundary.Enabled);
            Assert.True(boundary.ReplaySuppressed);
            boundary.ExternalDisable();
            Assert.False(boundary.RestoreAfterReplay());
            Assert.False(boundary.Enabled);

            boundary.ExternalEnable();
            Assert.True(boundary.Enabled);
            Assert.True(boundary.TryDisableForReplay());
            boundary.ExternalEnable();
            Assert.True(boundary.RestoreAfterReplay());
            Assert.True(boundary.Enabled);
        }

        [Fact]
        public void RemoteMcapManagerRetryClearsFailureAndRetriesAfterOneSecond()
        {
            AssertEndpointRetryBehavior();
        }

        [Fact]
        public void ReplayCursorManagerRetryClearsFailureAndRetriesAfterOneSecond()
        {
            AssertEndpointRetryBehavior();
        }

        private static void AssertEndpointRetryBehavior()
        {
            var boundary = new ManagerEndpointBoundary();

            Assert.Equal(RetryExecutionResult.Failed, boundary.Refresh(100d, fail: true));
            Assert.Equal(1, boundary.StartAttempts);
            Assert.False(boundary.ConfigurationKnown);
            Assert.Equal(RetryExecutionResult.Blocked, boundary.Refresh(100.5d, fail: false));
            Assert.Equal(1, boundary.StartAttempts);

            Assert.Equal(RetryExecutionResult.Succeeded, boundary.Refresh(101d, fail: false));
            Assert.Equal(2, boundary.StartAttempts);
            Assert.True(boundary.ConfigurationKnown);
        }

        private static ComponentPublisherSessionSnapshot BuildSnapshot(ulong generation, object publisher)
            => new ComponentPublisherSessionBuilder().Build(
                generation,
                new[]
                {
                    new ComponentPublisherContractDraft(
                        publisher,
                        "publisher",
                        publisher.GetType(),
                        "publisher",
                        "/replay",
                        "schema",
                        PublisherEffectiveEncoding.Json,
                        PublisherEffectiveEncoding.Json)
                });

        private sealed class DeferredManagerPublisher
        {
            private readonly object _manager;

            public DeferredManagerPublisher(object manager)
            {
                _manager = manager;
            }

            public object ConfiguredManager { get; private set; }
            public int ResolveCalls { get; private set; }
            public bool HasValidTopic => true;

            public object ResolveManagerForComponentSession()
            {
                ResolveCalls++;
                ConfiguredManager = _manager;
                return ConfiguredManager;
            }
        }

        private sealed class ReplayMessageBoundary
        {
            private readonly ReplaySubscriberFanoutState<Action<string, byte[]>> _state =
                new ReplaySubscriberFanoutState<Action<string, byte[]>>();

            public event Action<string, byte[]> OnReplayMessage
            {
                add => _state.Add(value);
                remove => _state.Remove(value);
            }

            public void Dispatch(string topic, byte[] data, Action<Exception> onError)
                => _state.Invoke(handler => handler(topic, data), onError);
        }

        private sealed class ReplayContextBoundary
        {
            private readonly ReplaySubscriberFanoutState<Action<int>> _state =
                new ReplaySubscriberFanoutState<Action<int>>();

            public event Action<int> OnReplayMessageContext
            {
                add => _state.Add(value);
                remove => _state.Remove(value);
            }

            public void Dispatch(int value, Action<Exception> onError)
                => _state.Invoke(handler => handler(value), onError);
        }

        private sealed class ReplayBatchBoundary
        {
            private readonly ReplaySubscriberFanoutState<Action<int>> _state =
                new ReplaySubscriberFanoutState<Action<int>>();

            public event Action<int> OnReplayBatchCompleted
            {
                add => _state.Add(value);
                remove => _state.Remove(value);
            }

            public void Dispatch(int value, Action<Exception> onError)
                => _state.Invoke(handler => handler(value), onError);
        }

        private sealed class ReplayPublisherBoundary
        {
            private readonly ReplayDisableOwnershipState _state = new ReplayDisableOwnershipState();

            public ReplayPublisherBoundary(bool enabled)
            {
                Enabled = enabled;
            }

            public bool Enabled { get; private set; }
            public bool ReplaySuppressed { get; private set; }

            public bool TryDisableForReplay()
                => _state.TryAcquire(() => Enabled, () => ReplaySuppressed = true);

            public bool RestoreAfterReplay()
                => _state.TryRestoreOwned(() => ReplaySuppressed = false);

            public void ExternalEnable()
            {
                SetEnabledLikeUnity(true);
            }

            public void ExternalDisable()
            {
                SetEnabledLikeUnity(false);
            }

            private void SetEnabledLikeUnity(bool value)
            {
                if (Enabled == value)
                    return;

                Enabled = value;
                if (_state.NotifyExternalLifecycleTransition())
                    ReplaySuppressed = false;
            }
        }

        private sealed class ManagerEndpointBoundary
        {
            private readonly RetryBackoffState _state = new RetryBackoffState();

            public int StartAttempts { get; private set; }
            public bool ConfigurationKnown { get; private set; }

            public RetryExecutionResult Refresh(double now, bool fail)
                => _state.TryExecute(
                    now,
                    1d,
                    () =>
                    {
                        StartAttempts++;
                        if (fail)
                            throw new InvalidOperationException("transient-bind-failure");
                        ConfigurationKnown = true;
                    },
                    _ => ConfigurationKnown = false);
        }
    }
}
