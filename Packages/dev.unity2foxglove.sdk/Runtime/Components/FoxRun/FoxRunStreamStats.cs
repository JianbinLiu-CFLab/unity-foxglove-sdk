// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Immutable diagnostic snapshot for a bounded FoxRun input stream.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Saturating lifetime counters for one stream instance.</summary>
    public readonly struct FoxRunStreamStats
    {
        internal FoxRunStreamStats(
            long received,
            long admitted,
            long drained,
            long taken,
            long droppedOldest,
            long droppedNewest,
            long rateDropped,
            long cleared,
            long highWater,
            long disposalFailures,
            string lastDisposalError)
        {
            Received = received;
            Admitted = admitted;
            Drained = drained;
            Taken = taken;
            DroppedOldest = droppedOldest;
            DroppedNewest = droppedNewest;
            RateDropped = rateDropped;
            Cleared = cleared;
            HighWater = highWater;
            DisposalFailures = disposalFailures;
            LastDisposalError = lastDisposalError ?? string.Empty;
        }

        public long Received { get; }
        public long Admitted { get; }
        public long Drained { get; }
        public long Taken { get; }
        public long DroppedOldest { get; }
        public long DroppedNewest { get; }
        public long RateDropped { get; }
        public long Cleared { get; }
        public long HighWater { get; }
        public long DisposalFailures { get; }
        public string LastDisposalError { get; }
    }
}
