// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Coalesces optional external replay cursor requests for main-thread drain.

using System.Threading;

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
        private int _enabled;
        private int _hasPendingFast;
        private bool _hasPending;
        private bool _hasLastAccepted;
        private ulong _lastAcceptedNs;
        private ReplayCursorRequest _pending;

        /// <summary>Whether the optional cursor bridge should accept requests.</summary>
        public bool Enabled
        {
            get => Volatile.Read(ref _enabled) != 0;
            set => Volatile.Write(ref _enabled, value ? 1 : 0);
        }

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
                // An explicit seek is a restoration command, not a duplicate
                // advance. It must be accepted even when its timestamp equals
                // the last accepted cursor so a rebuilt scene can be replayed.
                if (_hasLastAccepted && _lastAcceptedNs == clampedTimeNs && !request.DidSeek)
                {
                    message = "Duplicate cursor ignored.";
                    return ExternalReplayCursorEnqueueResult.Duplicate;
                }

                _pending = request.WithTimeNs(clampedTimeNs);
                _hasPending = true;
                Volatile.Write(ref _hasPendingFast, 1);
                _hasLastAccepted = true;
                _lastAcceptedNs = clampedTimeNs;
                message = "Cursor accepted.";
                return ExternalReplayCursorEnqueueResult.Accepted;
            }
        }

        /// <summary>Drain the latest pending cursor request, dropping older coalesced values.</summary>
        public bool TryDrainLatest(out ReplayCursorRequest request)
        {
            if (Volatile.Read(ref _hasPendingFast) == 0)
            {
                request = default;
                return false;
            }

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
                Volatile.Write(ref _hasPendingFast, 0);
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
                Volatile.Write(ref _hasPendingFast, 0);
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
