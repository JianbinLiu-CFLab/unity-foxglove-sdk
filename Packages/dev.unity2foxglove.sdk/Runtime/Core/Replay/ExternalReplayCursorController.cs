// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Coalesces optional external replay cursor requests for main-thread drain.

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>Result of accepting or rejecting an external replay cursor request.</summary>
    public enum ExternalReplayCursorEnqueueResult
    {
        /// <summary>The request was accepted and queued for the next runtime tick.</summary>
        Accepted,

        /// <summary>The cursor bridge is disabled.</summary>
        Disabled,

        /// <summary>Replay is not currently available.</summary>
        ReplayUnavailable,

        /// <summary>The request repeats the latest accepted cursor value.</summary>
        Duplicate
    }

    /// <summary>
    /// Thread-safe queue for Foxglove-extension cursor updates. Network code
    /// enqueues requests here; <see cref="TickCoordinator"/> drains only the
    /// latest request on the runtime owner thread.
    /// </summary>
    public sealed class ExternalReplayCursorController
    {
        private readonly object _gate = new object();
        private bool _hasPending;
        private bool _hasLastAccepted;
        private ulong _lastAcceptedNs;
        private ReplayCursorRequest _pending;

        /// <summary>Whether the optional cursor bridge should accept requests.</summary>
        public bool Enabled { get; set; }

        /// <summary>Queue a cursor request after disabled, replay, duplicate, and range checks.</summary>
        public ExternalReplayCursorEnqueueResult TryEnqueue(
            ReplayCursorRequest request,
            bool replayEnabled,
            ulong startNs,
            ulong endNs,
            out string message)
        {
            if (!Enabled)
            {
                message = "External replay cursor bridge is disabled.";
                return ExternalReplayCursorEnqueueResult.Disabled;
            }

            if (!replayEnabled || endNs < startNs)
            {
                message = "Replay is not available for external cursor control.";
                return ExternalReplayCursorEnqueueResult.ReplayUnavailable;
            }

            var clampedTimeNs = Clamp(request.TimeNs, startNs, endNs);
            lock (_gate)
            {
                if (_hasLastAccepted && _lastAcceptedNs == clampedTimeNs)
                {
                    message = "Duplicate cursor ignored.";
                    return ExternalReplayCursorEnqueueResult.Duplicate;
                }

                _pending = request.WithTimeNs(clampedTimeNs);
                _hasPending = true;
                _hasLastAccepted = true;
                _lastAcceptedNs = clampedTimeNs;
                message = "Cursor accepted.";
                return ExternalReplayCursorEnqueueResult.Accepted;
            }
        }

        /// <summary>Drain the latest pending cursor request, dropping older coalesced values.</summary>
        public bool TryDrainLatest(out ReplayCursorRequest request)
        {
            lock (_gate)
            {
                if (!_hasPending)
                {
                    request = default;
                    return false;
                }

                request = _pending;
                _pending = default;
                _hasPending = false;
                return true;
            }
        }

        /// <summary>Clear pending state when replay or the endpoint is disabled.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _pending = default;
                _hasPending = false;
                _hasLastAccepted = false;
                _lastAcceptedNs = 0;
            }
        }

        private static ulong Clamp(ulong value, ulong min, ulong max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
