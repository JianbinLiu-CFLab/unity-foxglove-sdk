// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Small lifecycle helper for generation-guarded background workers.

using System;
using System.Threading;

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>Tracks stop, generation, running, and idle state for one background worker.</summary>
    internal sealed class BackgroundWorkerLifecycle : IDisposable
    {
        public object Gate { get; } = new object();
        public ManualResetEventSlim Idle { get; } = new ManualResetEventSlim(true);
        public bool IsRunning { get; private set; }
        public bool StopRequested { get; private set; }
        public int Generation { get; private set; }

        public int StartOrReuseLocked(out bool startWorker)
        {
            if (!IsRunning)
            {
                StopRequested = false;
                Generation = unchecked(Generation + 1);
                IsRunning = true;
                Idle.Reset();
                startWorker = true;
                return Generation;
            }

            startWorker = false;
            return Generation;
        }

        public bool ShouldStopLocked(int generation)
            => StopRequested || generation != Generation;

        public void RequestStopLocked()
        {
            StopRequested = true;
        }

        public bool MarkStoppedIfCurrentLocked(int generation)
        {
            if (generation != Generation)
                return false;

            IsRunning = false;
            return true;
        }

        public void MarkStartFailedIfCurrentLocked(int generation)
        {
            if (MarkStoppedIfCurrentLocked(generation))
                Idle.Set();
        }

        public void InvalidateTimedOutWorkerLocked()
        {
            // Keep the worker logically running until its thread actually exits.
            // A timed-out stop must not open a second encode generation while the
            // previous delegate can still be executing.
            StopRequested = true;
        }

        public void Dispose()
        {
            Idle.Dispose();
        }
    }
}
