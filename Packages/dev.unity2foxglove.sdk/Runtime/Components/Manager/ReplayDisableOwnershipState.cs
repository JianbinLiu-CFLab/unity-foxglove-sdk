// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Replay disable ownership policy for publishers.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Tracks whether replay owns a publisher suppression/restore transition.</summary>
    internal sealed class ReplayDisableOwnershipState
    {
        private bool _owned;

        internal bool TryAcquire(Func<bool> isEnabled, Action acquire)
        {
            if (isEnabled == null) throw new ArgumentNullException(nameof(isEnabled));
            if (acquire == null) throw new ArgumentNullException(nameof(acquire));
            if (!isEnabled())
                return false;

            acquire();
            _owned = true;
            return true;
        }

        internal bool TryRestoreOwned(Action restore)
        {
            if (restore == null) throw new ArgumentNullException(nameof(restore));
            if (!_owned)
                return false;

            _owned = false;
            restore();
            return true;
        }
    }
}
