// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Component publisher session lifecycle policy for FoxgloveManager.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Owns the active Component publisher snapshot and its session transition.</summary>
    internal sealed class ComponentPublisherSessionLifecycleState<TSession>
        where TSession : class
    {
        private TSession _activeSession;

        public TSession ActiveSession => _activeSession;

        public void Set(TSession session)
            => _activeSession = session;

        public void Clear()
            => _activeSession = null;

        public ulong AdvanceAndCapture(Func<ulong> advance, Func<ulong, TSession> capture)
        {
            if (advance == null) throw new ArgumentNullException(nameof(advance));
            if (capture == null) throw new ArgumentNullException(nameof(capture));

            var generation = advance();
            _activeSession = capture(generation);
            return generation;
        }

        public ulong AdvanceActivateAndCapture(
            Func<ulong> advance,
            Action<ulong> activate,
            Func<ulong, TSession> capture)
        {
            if (advance == null) throw new ArgumentNullException(nameof(advance));
            if (activate == null) throw new ArgumentNullException(nameof(activate));
            if (capture == null) throw new ArgumentNullException(nameof(capture));

            var generation = advance();
            activate(generation);
            _activeSession = capture(generation);
            return generation;
        }
    }
}
