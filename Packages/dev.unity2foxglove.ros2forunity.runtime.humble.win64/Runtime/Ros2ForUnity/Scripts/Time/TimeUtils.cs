// Copyright 2022 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu.
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

namespace ROS2
{

/// <summary>
/// Helpers for converting floating-point seconds into ROS time fields.
/// </summary>
internal static class TimeUtils
{
  private const double NanosecondsPerSecondDouble = 1_000_000_000.0;
  private const long NanosecondsPerSecond = 1_000_000_000L;

  /// <summary>
  /// Converts total seconds into ROS sec/nanosec fields.
  /// </summary>
  /// <remarks>
  /// ROS 2 builtin_interfaces/Time stores seconds in a signed 32-bit field. This helper deliberately
  /// throws when the input falls outside that range, including timestamps beyond the 2038 boundary.
  /// </remarks>
  public static void TimeFromTotalSeconds(in double secondsIn, out int seconds, out uint nanoseconds)
  {
    if (Double.IsNaN(secondsIn) || Double.IsInfinity(secondsIn))
    {
      throw new ArgumentOutOfRangeException(nameof(secondsIn), "ROS time cannot be NaN or infinity");
    }

    double wholeSeconds = Math.Floor(secondsIn);
    double fractionalSeconds = secondsIn - wholeSeconds;
    long wholeNanoseconds = (long)Math.Round(fractionalSeconds * NanosecondsPerSecondDouble);

    if (wholeNanoseconds >= NanosecondsPerSecond)
    {
      wholeSeconds += 1.0;
      wholeNanoseconds -= NanosecondsPerSecond;
    }

    if (wholeSeconds < Int32.MinValue || wholeSeconds > Int32.MaxValue)
    {
      throw new OverflowException("ROS time seconds exceed Int32 range");
    }

    seconds = (int)wholeSeconds;
    nanoseconds = (uint)wholeNanoseconds;
  }
}

}  // namespace ROS2
