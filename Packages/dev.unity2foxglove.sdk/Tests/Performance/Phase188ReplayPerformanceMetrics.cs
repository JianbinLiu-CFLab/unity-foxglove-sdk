// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Performance
{
    /// <summary>Deterministic percentile calculation for paired replay samples.</summary>
    public static class Phase188ReplayPerformanceMetrics
    {
        public static double Percentile(IReadOnlyList<double> samples, double percentile)
        {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));
            if (samples.Count == 0)
                throw new ArgumentException("At least one sample is required.", nameof(samples));
            if (percentile < 0 || percentile > 100)
                throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "Percentile must be within [0, 100].");

            var sorted = new List<double>(samples.Count);
            for (var i = 0; i < samples.Count; i++)
                sorted.Add(samples[i]);
            sorted.Sort();

            var rank = (int)Math.Ceiling(percentile / 100d * sorted.Count);
            var index = Math.Max(0, Math.Min(sorted.Count - 1, rank - 1));
            return sorted[index];
        }
    }
}
