// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

namespace Unity2Foxglove.ManualAcceptance
{
    internal enum Phase186ManualStep
    {
        WaitingForProviderOrExternalA,
        ReadyToPublishLocalB,
        WaitingForAutomatedBAndPeerEvidence,
        ReadyToComplete,
        CompletedKeepPlayRunning,
    }

    internal readonly struct Phase186ManualInteraction
    {
        internal Phase186ManualInteraction(
            Phase186ManualStep step,
            bool canRequestLocalB,
            bool canRequestComplete)
        {
            Step = step;
            CanRequestLocalB = canRequestLocalB;
            CanRequestComplete = canRequestComplete;
        }

        internal Phase186ManualStep Step { get; }
        internal bool CanRequestLocalB { get; }
        internal bool CanRequestComplete { get; }
    }

    internal sealed class Phase186ManualInteractionState
    {
        internal bool ExternalAObserved { get; private set; }
        internal bool LocalBRequested { get; private set; }
        internal bool CompleteRequested { get; private set; }

        internal void ResetForRun()
        {
            ExternalAObserved = false;
            LocalBRequested = false;
            CompleteRequested = false;
        }

        internal void ObserveGeneratedTick(
            bool manual,
            long appliedBeforeTick,
            long appliedAfterTick,
            bool hasObservedExternalInput)
        {
            if (manual
                && appliedAfterTick > appliedBeforeTick
                && hasObservedExternalInput)
            {
                ExternalAObserved = true;
            }
        }

        internal Phase186ManualInteraction Evaluate(
            bool manual,
            bool contextValid,
            bool terminal,
            bool publishReady,
            bool subscribeReady,
            bool canComplete)
        {
            if (terminal || CompleteRequested)
            {
                return new Phase186ManualInteraction(
                    Phase186ManualStep.CompletedKeepPlayRunning,
                    canRequestLocalB: false,
                    canRequestComplete: false);
            }

            var readyForLocalB = manual
                                 && contextValid
                                 && publishReady
                                 && subscribeReady
                                 && ExternalAObserved
                                 && !LocalBRequested;
            if (readyForLocalB)
            {
                return new Phase186ManualInteraction(
                    Phase186ManualStep.ReadyToPublishLocalB,
                    canRequestLocalB: true,
                    canRequestComplete: false);
            }

            if (manual && contextValid && LocalBRequested && canComplete)
            {
                return new Phase186ManualInteraction(
                    Phase186ManualStep.ReadyToComplete,
                    canRequestLocalB: false,
                    canRequestComplete: true);
            }

            return new Phase186ManualInteraction(
                manual && contextValid && LocalBRequested
                    ? Phase186ManualStep.WaitingForAutomatedBAndPeerEvidence
                    : Phase186ManualStep.WaitingForProviderOrExternalA,
                canRequestLocalB: false,
                canRequestComplete: false);
        }

        internal bool TryRequestLocalB(Phase186ManualInteraction interaction)
        {
            if (!interaction.CanRequestLocalB || LocalBRequested)
                return false;
            LocalBRequested = true;
            return true;
        }

        internal bool TryRequestComplete(Phase186ManualInteraction interaction)
        {
            if (!interaction.CanRequestComplete || CompleteRequested)
                return false;
            CompleteRequested = true;
            return true;
        }
    }
}
