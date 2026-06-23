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
using UnityEngine;

namespace ROS2
{

/// <summary>
/// ROS 2 clock time source that uses ROS system time by default and follows use_sim_time when the underlying ROS clock is configured for simulated time.
/// </summary>
/// <remarks>
/// This source requires an initialized ros2cs context. It lazily creates the underlying ROS 2 clock on
/// first use and disposes it with the time source.
/// </remarks>
public class ROS2TimeSource : ITimeSource, IDisposable
{
  private readonly object clockMutex = new object();
  private ROS2.Clock clock;

  /// <summary>
  /// Acquires the current ROS 2 clock value.
  /// </summary>
  /// <returns>False when ROS 2 is not initialized or has already shut down; otherwise true.</returns>
  public bool GetTime(out int seconds, out uint nanoseconds)
  {
    // U2F-LOCAL-PATCH: match newer ros2cs bool-returning ITimeSource contract.
    if (!ROS2.Ros2cs.Ok())
    {
      seconds = 0;
      nanoseconds = 0;
      Debug.LogWarning("Cannot acquire valid ros time, ros either not initialized or shut down already");
      return false;
    }

    double nowSeconds;
    lock (clockMutex)
    {
      if (clock == null)
      { // Create clock which uses system time by default (unless use_sim_time is set in ros2)
        clock = new ROS2.Clock();
      }
      nowSeconds = clock.Now.Seconds;
    }

    TimeUtils.TimeFromTotalSeconds(nowSeconds, out seconds, out nanoseconds);
    return true;
  }

  /// <summary>
  /// Releases the lazily-created ros2cs clock, if one was created.
  /// </summary>
  public void Dispose()
  {
    lock (clockMutex)
    {
      if (clock != null)
      {
        clock.Dispose();
        clock = null;
      }
    }
  }
}

}  // namespace ROS2
