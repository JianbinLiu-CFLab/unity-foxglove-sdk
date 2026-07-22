// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Per-binding origin/sequence helpers for custom ROS2 envelopes.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Main-thread-only monotonic sequence allocator. A binding must be retired
    /// when this source is exhausted; it never wraps and reuses an origin pair.
    /// </summary>
    internal sealed class FoxRunRos2CustomSequenceSource
    {
        private ulong _next;
        private bool _exhausted;

        internal FoxRunRos2CustomSequenceSource()
            : this(0UL)
        {
        }

        internal FoxRunRos2CustomSequenceSource(ulong initialSequence)
        {
            _next = initialSequence;
        }

        internal bool TryAllocate(out ulong sequence)
        {
            if (_exhausted)
            {
                sequence = 0UL;
                return false;
            }

            sequence = _next;
            if (_next == ulong.MaxValue)
                _exhausted = true;
            else
                _next++;
            return true;
        }

        /// <summary>
        /// Returns the next sequence without reserving it. Publisher bindings
        /// use this to map first, then commit the exact value only after mapper
        /// preflight succeeds, so a mapper/budget failure cannot consume an
        /// origin-sequence pair.
        /// </summary>
        internal bool TryPeek(out ulong sequence)
        {
            if (_exhausted)
            {
                sequence = 0UL;
                return false;
            }

            sequence = _next;
            return true;
        }
    }

    internal static class FoxRunRos2CustomEnvelopeOrigin
    {
        internal static string Create()
            => Guid.NewGuid().ToString("N");

        internal static bool IsSameNonEmptyOrigin(string localOrigin, string remoteOrigin)
            => !String.IsNullOrEmpty(localOrigin)
               && !String.IsNullOrEmpty(remoteOrigin)
               && String.Equals(localOrigin, remoteOrigin, StringComparison.Ordinal);
    }
}
#endif
