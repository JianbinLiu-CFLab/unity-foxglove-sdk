// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Immutable finite bounds for one FoxRun input stream.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Immutable finite bounds captured by a <see cref="FoxRunStream{T}"/>.</summary>
    public sealed class FoxRunStreamOptions
    {
        public const int DefaultCapacity = 1024;
        public const double DefaultMaxInputHz = 1000d;
        public const int DefaultMaxBatch = 128;

        public FoxRunStreamOptions(
            int capacity = DefaultCapacity,
            double maxInputHz = DefaultMaxInputHz,
            int maxBatch = DefaultMaxBatch,
            FoxRunStreamOverflowPolicy overflow = FoxRunStreamOverflowPolicy.DropOldest)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            if (double.IsNaN(maxInputHz)
                || double.IsInfinity(maxInputHz)
                || maxInputHz <= 0d)
                throw new ArgumentOutOfRangeException(
                    nameof(maxInputHz),
                    "Maximum input rate must be positive and finite.");
            if (maxBatch <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBatch), "Maximum batch must be positive.");
            if (overflow != FoxRunStreamOverflowPolicy.DropOldest
                && overflow != FoxRunStreamOverflowPolicy.DropNewest)
                throw new ArgumentOutOfRangeException(
                    nameof(overflow),
                    "Only DropOldest and DropNewest are supported.");

            Capacity = capacity;
            MaxInputHz = maxInputHz;
            MaxBatch = maxBatch;
            Overflow = overflow;
        }

        public int Capacity { get; }
        public double MaxInputHz { get; }
        public int MaxBatch { get; }
        public FoxRunStreamOverflowPolicy Overflow { get; }
    }
}
