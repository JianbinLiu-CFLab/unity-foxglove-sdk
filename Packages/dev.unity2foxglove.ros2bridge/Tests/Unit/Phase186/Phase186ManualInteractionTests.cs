// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity2Foxglove.ManualAcceptance;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests.Unit.Phase186
{
    public sealed class Phase186ManualInteractionTests
    {
        [Fact]
        public void ManualStepsAdvanceOnlyThroughExternalAThenBThenPeerEvidence()
        {
            var state = new Phase186ManualInteractionState();

            Assert.Equal(
                Phase186ManualStep.WaitingForProviderOrExternalA,
                Evaluate(state).Step);

            state.ObserveGeneratedTick(
                manual: true,
                appliedBeforeTick: 0,
                appliedAfterTick: 1,
                hasObservedExternalInput: true);
            var publish = Evaluate(state);
            Assert.Equal(Phase186ManualStep.ReadyToPublishLocalB, publish.Step);
            Assert.True(state.TryRequestLocalB(publish));

            Assert.Equal(
                Phase186ManualStep.WaitingForAutomatedBAndPeerEvidence,
                Evaluate(state).Step);

            var complete = Evaluate(state, canComplete: true);
            Assert.Equal(Phase186ManualStep.ReadyToComplete, complete.Step);
            Assert.True(state.TryRequestComplete(complete));

            Assert.Equal(
                Phase186ManualStep.CompletedKeepPlayRunning,
                Evaluate(state, canComplete: true).Step);
        }

        [Theory]
        [InlineData(false, true, false, true, true, true)]
        [InlineData(true, false, false, true, true, true)]
        [InlineData(true, true, true, true, true, true)]
        [InlineData(true, true, false, false, true, true)]
        [InlineData(true, true, false, true, false, true)]
        [InlineData(true, true, false, true, true, false)]
        public void LocalBRejectsEveryMissingPrerequisite(
            bool manual,
            bool contextValid,
            bool terminal,
            bool publishReady,
            bool subscribeReady,
            bool observeExternalA)
        {
            var state = new Phase186ManualInteractionState();
            state.ObserveGeneratedTick(
                manual: manual,
                appliedBeforeTick: 0,
                appliedAfterTick: observeExternalA ? 1 : 0,
                hasObservedExternalInput: observeExternalA);

            var interaction = state.Evaluate(
                manual,
                contextValid,
                terminal,
                publishReady,
                subscribeReady,
                canComplete: false);

            Assert.False(interaction.CanRequestLocalB);
            Assert.False(state.TryRequestLocalB(interaction));
            Assert.False(state.LocalBRequested);
        }

        [Fact]
        public void LocalBRequestIsUnchangedByEarlyAndDuplicateActions()
        {
            var state = new Phase186ManualInteractionState();

            Assert.False(state.TryRequestLocalB(Evaluate(state)));
            Assert.False(state.LocalBRequested);

            state.ObserveGeneratedTick(
                manual: true,
                appliedBeforeTick: 0,
                appliedAfterTick: 1,
                hasObservedExternalInput: true);
            var ready = Evaluate(state);
            Assert.True(state.TryRequestLocalB(ready));
            Assert.True(state.LocalBRequested);

            Assert.False(state.TryRequestLocalB(Evaluate(state)));
            Assert.True(state.LocalBRequested);
        }

        [Fact]
        public void CompleteUsesPostBEvidenceAndIsOneShot()
        {
            var state = new Phase186ManualInteractionState();
            state.ObserveGeneratedTick(
                manual: true,
                appliedBeforeTick: 0,
                appliedAfterTick: 1,
                hasObservedExternalInput: true);
            Assert.True(state.TryRequestLocalB(Evaluate(state)));

            Assert.False(state.TryRequestComplete(Evaluate(state)));
            Assert.False(state.CompleteRequested);

            var ready = Evaluate(state, canComplete: true);
            Assert.True(state.TryRequestComplete(ready));
            Assert.True(state.CompleteRequested);

            Assert.False(state.TryRequestComplete(Evaluate(state, canComplete: true)));
            Assert.True(state.CompleteRequested);
        }

        [Fact]
        public void ExternalAOnlyLatchesAfterAnObservedAppliedManualTickAndResets()
        {
            var state = new Phase186ManualInteractionState();

            state.ObserveGeneratedTick(
                manual: true,
                appliedBeforeTick: 0,
                appliedAfterTick: 0,
                hasObservedExternalInput: true);
            Assert.False(state.ExternalAObserved);

            state.ObserveGeneratedTick(
                manual: true,
                appliedBeforeTick: 0,
                appliedAfterTick: 1,
                hasObservedExternalInput: false);
            Assert.False(state.ExternalAObserved);

            state.ObserveGeneratedTick(
                manual: false,
                appliedBeforeTick: 0,
                appliedAfterTick: 1,
                hasObservedExternalInput: true);
            Assert.False(state.ExternalAObserved);

            state.ObserveGeneratedTick(
                manual: true,
                appliedBeforeTick: 1,
                appliedAfterTick: 1,
                hasObservedExternalInput: true);
            Assert.False(state.ExternalAObserved);

            state.ObserveGeneratedTick(
                manual: true,
                appliedBeforeTick: 1,
                appliedAfterTick: 2,
                hasObservedExternalInput: true);
            Assert.True(state.ExternalAObserved);

            state.ObserveGeneratedTick(
                manual: true,
                appliedBeforeTick: 2,
                appliedAfterTick: 2,
                hasObservedExternalInput: false);
            Assert.True(state.ExternalAObserved);

            state.ResetForRun();
            Assert.False(state.ExternalAObserved);
            Assert.False(state.LocalBRequested);
            Assert.False(state.CompleteRequested);
        }

        private static Phase186ManualInteraction Evaluate(
            Phase186ManualInteractionState state,
            bool canComplete = false)
            => state.Evaluate(
                manual: true,
                contextValid: true,
                terminal: false,
                publishReady: true,
                subscribeReady: true,
                canComplete: canComplete);
    }
}
