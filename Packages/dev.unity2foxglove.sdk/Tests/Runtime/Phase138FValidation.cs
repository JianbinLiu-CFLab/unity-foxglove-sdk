// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138F validation for IMU sub-step scheduling math and queue budget.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Sensors.Imu;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Regression coverage for high-rate IMU sub-step resampling helpers.
    /// </summary>
    public static class Phase138FValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138F: Virtual IMU Sub-Step Resampling ===");
            _passed = 0;

            VerifySubStepGridForUpsampling();
            VerifySubStepGridForDownsampling();
            VerifyTimestampGridMonotonicAndUniform();
            VerifyQueueCapacityDefaults();

            Console.WriteLine($"Phase 138F: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void VerifySubStepGridForUpsampling()
        {
            var dt = 1.0 / 60.0;
            var times = CollectSampleTimes(200, dt, 12);
            var period = 1.0 / 200.0;

            Check(times.Count > 0, "138F-1: sub-step scheduler emits >0 samples for 200Hz target / 60Hz tick");

            for (var i = 1; i < times.Count; i++)
            {
                Check(times[i] > times[i - 1], "138F-2: generated sample times are strictly increasing");
                Check(Math.Abs(times[i] - times[i - 1] - period) <= 1e-12,
                    "138F-3: generated sample spacing stays at 1/200s in physics-time grid");
            }
        }

        private static void VerifySubStepGridForDownsampling()
        {
            var dt = 1.0 / 60.0;
            var times = CollectSampleTimes(50, dt, 10);

            Check(times.Count > 0, "138F-4: sub-step scheduler emits samples when target < physics rate");
            Check(times[0] >= 0.0, "138F-5: first emitted time is non-negative");

            var period = 1.0 / 50.0;
            for (var i = 1; i < times.Count; i++)
            {
                var gap = times[i] - times[i - 1];
                Check(gap >= period - 1e-12 && gap <= period + 1e-12,
                    "138F-6: downsampled sample spacing stays on 1/50s physics grid");
            }
        }

        private static void VerifyTimestampGridMonotonicAndUniform()
        {
            const int targetRate = 200;
            const ulong epoch = 1_000_000_000UL;

            var t0 = ImuSubStep.SampleTimestampNs(epoch, 0, targetRate);
            var t1 = ImuSubStep.SampleTimestampNs(epoch, 1, targetRate);
            var t2 = ImuSubStep.SampleTimestampNs(epoch, 2, targetRate);
            Check(t1 > t0 && t2 > t1, "138F-7: sample timestamp ns sequence is monotonic");
            Check(t1 - t0 == 5_000_000UL, "138F-8: 1st step equals 5ms in nanoseconds at 200Hz");
            Check(t2 - t1 == 5_000_000UL, "138F-9: grid step remains uniform at 200Hz");

            var t10 = ImuSubStep.SampleTimestampNs(epoch, 10, targetRate);
            Check(t10 - t0 == 50_000_000UL, "138F-10: 10-step offset is exactly 50ms in nanoseconds");
        }

        private static void VerifyQueueCapacityDefaults()
        {
            Check(ImuSubStep.ComputeQueueCapacity(200, 8, 512) == 40,
                "138F-11: high target queue capacity follows ceil(target/10)*2");
            Check(ImuSubStep.ComputeQueueCapacity(0, 8, 512) == 8,
                "138F-12: target 0 uses minimum fallback queue size");
            Check(ImuSubStep.ComputeQueueCapacity(5000, 8, 512) == 512,
                "138F-13: high target queue capacity is clamped to maximum");
        }

        private static List<double> CollectSampleTimes(int targetRateHz, double dt, int tickCount)
        {
            var tickStart = 0.0;
            var tickEnd = 0.0;
            var nextIndex = 0L;
            var times = new List<double>(32);

            for (var tick = 0; tick < tickCount; tick++)
            {
                tickEnd += dt;
                tickStart = tickEnd - dt;

                nextIndex = ImuSubStep.AlignSampleIndexToTickStart(tickStart, targetRateHz, nextIndex);
                while (ImuSubStep.TryGetSampleTime(targetRateHz, nextIndex, out var sampleTime))
                {
                    if (sampleTime > tickEnd + 1e-12)
                        break;

                    times.Add(sampleTime);
                    nextIndex++;
                }
            }

            return times;
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException($"Phase 138F validation failed: {label}");

            Console.WriteLine($"[PASS] {label}");
            _passed++;
        }
    }
}
