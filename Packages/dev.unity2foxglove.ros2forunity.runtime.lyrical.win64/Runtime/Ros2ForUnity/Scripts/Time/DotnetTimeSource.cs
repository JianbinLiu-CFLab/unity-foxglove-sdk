// Copyright 2022 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Diagnostics;

namespace ROS2
{

/// <summary>
/// UTC wall-clock time source anchored to the Unix epoch.
/// </summary>
/// <remarks>
/// DateTime.UtcNow provides the epoch alignment, while Stopwatch improves short-term resolution
/// between periodic wall-clock resynchronizations.
/// Outputs are clamped to the last emitted timestamp so wall-clock corrections cannot move time backward.
/// </remarks>
public class DotnetTimeSource : ITimeSource
{
    private static readonly DateTime UnixEpoch =
        new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Resynchronize with DateTime.UtcNow periodically so Stopwatch drift cannot accumulate indefinitely.
    private const double MaxUnsyncedSeconds = 10.0;

    private Stopwatch stopwatch = new Stopwatch();

    private readonly object mutex = new object();

    private double systemTimeIntervalStart = 0;
    private double lastEmittedSeconds = double.NegativeInfinity;

    private double TotalSystemTimeSeconds()
    {
        return (DateTime.UtcNow - UnixEpoch).TotalSeconds;
    }

    private void UpdateSystemTime()
    {
        systemTimeIntervalStart = TotalSystemTimeSeconds();
        stopwatch.Restart();
    }

    public DotnetTimeSource()
    {
        UpdateSystemTime();
    }

    /// <summary>
    /// Acquires Unix-epoch UTC wall time as ROS sec/nanosec fields.
    /// </summary>
    /// <returns>Always true.</returns>
    public bool GetTime(out int seconds, out uint nanoseconds)
    {
        lock(mutex) // Threading
        {
            var durationInSeconds = stopwatch.ElapsedTicks / (double)Stopwatch.Frequency;
            double timeOffset = 0;
            if (durationInSeconds >= MaxUnsyncedSeconds)
            {   // acquire DateTime to sync
                UpdateSystemTime();
            }
            else
            {   // use Stopwatch offset
                timeOffset = durationInSeconds;
            }
            
            var totalSeconds = systemTimeIntervalStart + timeOffset;
            if (totalSeconds < lastEmittedSeconds)
            {
                totalSeconds = lastEmittedSeconds;
            }
            else
            {
                lastEmittedSeconds = totalSeconds;
            }

            TimeUtils.TimeFromTotalSeconds(totalSeconds, out seconds, out nanoseconds);
        }
        return true;
    }
}

}  // namespace ROS2
