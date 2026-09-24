// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Shares production component-session attach and stop wiring.

using System;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        internal ulong AdvanceComponentPublisherSession(
            Func<ulong> advanceGeneration,
            Action<ulong> activateAdmission)
        {
            if (advanceGeneration == null)
                throw new ArgumentNullException(nameof(advanceGeneration));
            if (activateAdmission == null)
                throw new ArgumentNullException(nameof(activateAdmission));

            return _componentPublisherSessionState.AdvanceActivateAndCapture(
                advanceGeneration,
                activateAdmission,
                CaptureComponentPublisherSession);
        }

        internal void ClearActiveComponentPublisherSessionAtStop(Action restoreLivePublishers)
        {
            ClearActiveComponentPublisherSession();
            restoreLivePublishers?.Invoke();
        }

        internal void RunComponentPublisherSessionStopTail(
            bool restoreLivePublishers,
            Action restoreLivePublishersAction)
        {
            ClearActiveComponentPublisherSessionAtStop(
                restoreLivePublishers ? restoreLivePublishersAction : null);
        }
    }
}
