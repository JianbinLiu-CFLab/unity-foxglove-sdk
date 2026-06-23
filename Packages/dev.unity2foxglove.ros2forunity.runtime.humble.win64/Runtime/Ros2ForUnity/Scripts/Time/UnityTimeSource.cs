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
using System.Threading;
using UnityEngine;

namespace ROS2
{

/// <summary>
/// Acquires Unity play time from Time.timeAsDouble.
/// </summary>
/// <remarks>
/// This source reports seconds since Unity play-mode or player startup, not Unix epoch or ROS wall time.
/// Use ROS2ScalableTimeSource when timestamps must stay aligned with ROS/system time.
///
/// Unity's Time API only allows main-thread access. Background callers receive the last value sampled
/// by the main thread, so they may observe a stale timestamp until the next main-thread call.
/// </remarks>
public class UnityTimeSource : ITimeSource
{
  private readonly object mutex = new object();
  private int mainThreadId;
  private double lastReadingSecs;

  public UnityTimeSource()
  {
    mainThreadId = Thread.CurrentThread.ManagedThreadId;
    lastReadingSecs = Time.timeAsDouble;
  }

  /// <summary>
  /// Acquires Unity play time as ROS sec/nanosec fields.
  /// </summary>
  /// <returns>
  /// Always true. Background callers may receive the last main-thread sample because Unity time cannot
  /// be sampled off the main thread.
  /// </returns>
  public bool GetTime(out int seconds, out uint nanoseconds)
  {
    double reading;
    if (mainThreadId == Thread.CurrentThread.ManagedThreadId)
    {
      reading = Time.timeAsDouble;
      lock (mutex)
      {
        lastReadingSecs = reading;
      }
    }
    else
    {
      // Unity time can only be sampled on the main thread; background callers receive the last main-thread sample.
      lock (mutex)
      {
        reading = lastReadingSecs;
      }
    }
    TimeUtils.TimeFromTotalSeconds(reading, out seconds, out nanoseconds);
    return true;
  }
}

}  // namespace ROS2
