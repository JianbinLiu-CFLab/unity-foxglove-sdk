// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Imu
// Purpose: Pure math helpers for IMU sub-step resampling between physics ticks.

using System;

namespace Unity.FoxgloveSDK.Sensors.Imu
{
    /// <summary>
    /// Unit-testable arithmetic used by <see cref="Unity.FoxgloveSDK.Components.VirtualImu"/>.
    /// </summary>
    internal static class ImuSubStep
    {
        internal const int MaxSupportedRateHz = 5_000;

        private const double TickBoundaryEpsilonSeconds = 1e-12;
        private const ulong NanosPerSecond = 1_000_000_000UL;

        public static int NormalizeRateHz(double requestedRateHz)
        {
            if (double.IsNaN(requestedRateHz) || requestedRateHz <= 0d)
                return 0;
            if (double.IsPositiveInfinity(requestedRateHz) || requestedRateHz >= MaxSupportedRateHz)
                return MaxSupportedRateHz;

            return Math.Clamp((int)Math.Round(requestedRateHz), 0, MaxSupportedRateHz);
        }

        public static long AlignSampleIndexToTickStart(double tickStartSeconds, int targetRateHz, long nextSampleIndex)
        {
            targetRateHz = NormalizeRateHz(targetRateHz);
            if (targetRateHz == 0)
                return nextSampleIndex;

            var wanted = Math.Ceiling((tickStartSeconds * targetRateHz) - TickBoundaryEpsilonSeconds);
            var aligned = ClampNonNegativeWholeNumber(wanted);

            return aligned > nextSampleIndex ? aligned : nextSampleIndex;
        }

        public static bool TryGetSampleTime(int targetRateHz, long sampleIndex, out double sampleTimeSeconds)
        {
            targetRateHz = NormalizeRateHz(targetRateHz);
            if (targetRateHz == 0 || sampleIndex < 0)
            {
                sampleTimeSeconds = 0.0;
                return false;
            }

            sampleTimeSeconds = (double)sampleIndex / targetRateHz;
            return true;
        }

        public static ImuTickSamplePlan PlanTickSamples(
            double tickStartSeconds,
            double tickEndSeconds,
            int targetRateHz,
            long nextSampleIndex,
            int maxSamples)
        {
            targetRateHz = NormalizeRateHz(targetRateHz);
            if (targetRateHz == 0
                || double.IsNaN(tickStartSeconds)
                || double.IsNaN(tickEndSeconds)
                || tickEndSeconds < tickStartSeconds)
            {
                return new ImuTickSamplePlan(nextSampleIndex, 0, nextSampleIndex, 0);
            }

            var alignedIndex = AlignSampleIndexToTickStart(
                tickStartSeconds,
                targetRateHz,
                nextSampleIndex);
            var lastDueIndex = ClampNonNegativeWholeNumber(Math.Floor(
                (tickEndSeconds + TickBoundaryEpsilonSeconds) * targetRateHz));
            if (lastDueIndex < alignedIndex)
                return new ImuTickSamplePlan(alignedIndex, 0, alignedIndex, 0);

            var indexSpan = lastDueIndex - alignedIndex;
            var dueCount = indexSpan == long.MaxValue ? long.MaxValue : indexSpan + 1;
            var boundedMaxSamples = Math.Max(maxSamples, 0);
            var sampleCount = (int)Math.Min(dueCount, boundedMaxSamples);
            var skippedSampleCount = dueCount - sampleCount;
            var firstSampleIndex = alignedIndex + skippedSampleCount;
            var followingSampleIndex = lastDueIndex == long.MaxValue
                ? long.MaxValue
                : lastDueIndex + 1;

            return new ImuTickSamplePlan(
                firstSampleIndex,
                sampleCount,
                followingSampleIndex,
                skippedSampleCount);
        }

        public static ulong SampleTimestampNs(ulong epochUnixNs, long sampleIndex, int targetRateHz)
        {
            targetRateHz = NormalizeRateHz(targetRateHz);
            if (targetRateHz == 0)
                return epochUnixNs;

            if (sampleIndex < 0)
                sampleIndex = 0;

            var nanosFromEpoch = Math.Round(
                (double)sampleIndex * NanosPerSecond / targetRateHz,
                MidpointRounding.AwayFromZero);
            if (nanosFromEpoch <= 0d)
                return epochUnixNs;

            var maxDelta = ulong.MaxValue - epochUnixNs;
            // ulong.MaxValue is not exactly representable as double; keep the
            // double comparison as an early saturating guard, then validate the
            // converted delta against the exact integer limit before adding.
            if (nanosFromEpoch >= (double)maxDelta)
                return ulong.MaxValue;

            var delta = (ulong)nanosFromEpoch;
            if (delta > maxDelta)
                return ulong.MaxValue;

            return epochUnixNs + delta;
        }

        public static int ComputeQueueCapacity(int targetRateHz, int minSamples, int maxSamples)
        {
            var target = Math.Max(NormalizeRateHz(targetRateHz), 1);
            var desired = (int)Math.Ceiling(target / 10.0) * 2;
            return Math.Clamp(desired, minSamples, maxSamples);
        }

        private static long ClampNonNegativeWholeNumber(double value)
        {
            if (double.IsNaN(value) || value <= 0d)
                return 0;
            if (double.IsPositiveInfinity(value) || value >= long.MaxValue)
                return long.MaxValue;

            return (long)value;
        }
    }

    internal readonly struct ImuTickSamplePlan
    {
        public ImuTickSamplePlan(
            long firstSampleIndex,
            int sampleCount,
            long nextSampleIndex,
            long skippedSampleCount)
        {
            FirstSampleIndex = firstSampleIndex;
            SampleCount = sampleCount;
            NextSampleIndex = nextSampleIndex;
            SkippedSampleCount = skippedSampleCount;
        }

        public long FirstSampleIndex { get; }

        public int SampleCount { get; }

        public long NextSampleIndex { get; }

        public long SkippedSampleCount { get; }
    }
}
