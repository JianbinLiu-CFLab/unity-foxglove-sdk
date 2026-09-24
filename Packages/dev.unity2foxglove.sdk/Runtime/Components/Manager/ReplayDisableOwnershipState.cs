// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Replay disable ownership policy for publishers.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Tracks whether replay owns a publisher disable/restore transition.</summary>
    internal sealed class ReplayDisableOwnershipState
    {
        private bool _owned;
        private bool _transitionInProgress;

        internal bool HasOwnership => _owned;

        internal void NotifyExternalLifecycleTransition()
        {
            if (!_transitionInProgress)
                _owned = false;
        }

        internal bool TryAcquire(Func<bool> isEnabled, Action disable)
        {
            if (isEnabled == null) throw new ArgumentNullException(nameof(isEnabled));
            if (disable == null) throw new ArgumentNullException(nameof(disable));
            if (!isEnabled())
                return false;

            _transitionInProgress = true;
            try
            {
                disable();
                _owned = true;
                return true;
            }
            finally
            {
                _transitionInProgress = false;
            }
        }

        internal bool TryRestore(Func<bool> isEnabled, Action enable)
        {
            if (isEnabled == null) throw new ArgumentNullException(nameof(isEnabled));
            if (enable == null) throw new ArgumentNullException(nameof(enable));
            if (!_owned)
                return false;

            _owned = false;
            if (!isEnabled())
                enable();
            return true;
        }
    }
}
