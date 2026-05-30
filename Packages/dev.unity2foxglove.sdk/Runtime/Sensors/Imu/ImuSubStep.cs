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
        private const double TickBoundaryEpsilonSeconds = 1e-12;
        private const ulong NanosPerSecond = 1_000_000_000UL;

        public static long AlignSampleIndexToTickStart(double tickStartSeconds, int targetRateHz, long nextSampleIndex)
        {
            if (targetRateHz <= 0)
                return nextSampleIndex;

            var wanted = Math.Ceiling((tickStartSeconds * targetRateHz) - TickBoundaryEpsilonSeconds);
            var aligned = (long)wanted;
            if (aligned < 0)
                aligned = 0;

            return aligned > nextSampleIndex ? aligned : nextSampleIndex;
        }

        public static bool TryGetSampleTime(int targetRateHz, long sampleIndex, out double sampleTimeSeconds)
        {
            if (targetRateHz <= 0 || sampleIndex < 0)
            {
                sampleTimeSeconds = 0.0;
                return false;
            }

            sampleTimeSeconds = (double)sampleIndex / targetRateHz;
            return true;
        }

        public static ulong SampleTimestampNs(ulong epochUnixNs, long sampleIndex, int targetRateHz)
        {
            if (targetRateHz <= 0)
                return epochUnixNs;

            if (sampleIndex < 0)
                sampleIndex = 0;

            var nanosFromEpoch = (ulong)sampleIndex * NanosPerSecond / (ulong)targetRateHz;
            return epochUnixNs + nanosFromEpoch;
        }

        public static int ComputeQueueCapacity(int targetRateHz, int minSamples, int maxSamples)
        {
            var target = Math.Max(targetRateHz, 1);
            var desired = (int)Math.Ceiling(target / 10.0) * 2;
            return Math.Clamp(desired, minSamples, maxSamples);
        }
    }
}
