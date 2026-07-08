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
/// ROS-aligned time source that preserves ROS/system time until Unity timeScale changes.
/// </summary>
/// <remarks>
/// Before the first timeScale change, timestamps come directly from the ROS 2 clock. After timeScale
/// changes once, the source permanently switches to Unity scaled time plus the ROS-time offset captured
/// at that transition, avoiding timeline jumps while respecting Unity time scaling.
/// </remarks>
public class ROS2ScalableTimeSource : ITimeSource, IDisposable
{
  private readonly object mutex = new object();
  private readonly object clockMutex = new object();
  private int mainThreadId;

  // State machine:
  // 1. Capture the first observed Unity timeScale (Unity defaults to 1.0, but projects may change it early).
  // 2. While timeScale is unchanged, forward ROS/system time directly.
  // 3. After the first timeScale change, use Unity scaled time plus a one-time ROS offset forever.
  private double lastReadingSecs;
  private ROS2.Clock clock;
  private double rosUnityTimeOffset = 0;
  private double initialTimeScale = 0;
  private bool rosUnityTimeOffsetAcquired = false;
  private bool initialTimeScaleAcquired = false;
  private bool timeScaleChanged = false;
  private bool timeScaleChangeLogged = false;
  private int rosUnavailableWarningLogged = 0;

  public ROS2ScalableTimeSource()
  {
    mainThreadId = Thread.CurrentThread.ManagedThreadId;
    UpdateUnityTimeSnapshot();
  }

  /// <summary>
  /// Acquires ROS-aligned time, switching to Unity-scaled time only after Time.timeScale changes.
  /// </summary>
  /// <returns>False when ROS 2 is not initialized; otherwise true.</returns>
  public bool GetTime(out int seconds, out uint nanoseconds)
  {
    // U2F-LOCAL-PATCH: match newer ros2cs bool-returning ITimeSource contract.
    if (!ROS2.Ros2cs.Ok())
    {
      seconds = 0;
      nanoseconds = 0;
      if (Interlocked.Exchange(ref rosUnavailableWarningLogged, 1) == 0)
      {
        Debug.LogWarning("Cannot acquire valid ros time, ros either not initialized or shut down already");
      }
      return false;
    }

    bool isMainThread = mainThreadId == Thread.CurrentThread.ManagedThreadId;
    if (isMainThread)
    {
      UpdateUnityTimeSnapshot();
    }

    double readingSecs;
    bool scaleChangedAtRead;
    lock (mutex)
    {
      readingSecs = lastReadingSecs;
      scaleChangedAtRead = timeScaleChanged;
    }

    if (!scaleChangedAtRead)
    {
      // Until Unity timeScale changes, preserve the default ROS/system clock behavior.
      TimeUtils.TimeFromTotalSeconds(GetRosNowSeconds(), out seconds, out nanoseconds);
    }
    else
    {
      double adjustedTime;
      bool needsOffset;
      lock (mutex)
      {
        readingSecs = lastReadingSecs;
        needsOffset = !rosUnityTimeOffsetAcquired;
        adjustedTime = readingSecs + rosUnityTimeOffset;
      }
      if (needsOffset)
      {
        var rosNowSecs = GetRosNowSeconds();
        lock (mutex)
        {
          readingSecs = lastReadingSecs;
          if (!rosUnityTimeOffsetAcquired)
          {
            rosUnityTimeOffset = rosNowSecs - readingSecs;
            rosUnityTimeOffsetAcquired = true;
          }
          adjustedTime = readingSecs + rosUnityTimeOffset;
        }
      }
      TimeUtils.TimeFromTotalSeconds(adjustedTime, out seconds, out nanoseconds);
    }
    return true;
  }

  private double GetRosNowSeconds()
  {
    lock (clockMutex)
    {
      if (clock == null)
      { // Create clock which uses system time by default (unless use_sim_time is set in ros2)
        clock = new ROS2.Clock();
      }
      return clock.Now.Seconds;
    }
  }

  private void UpdateUnityTimeSnapshot()
  {
    lock (mutex)
    {
      if (!initialTimeScaleAcquired)
      {
        initialTimeScaleAcquired = true;
        initialTimeScale = Time.timeScale;
      }

      if (initialTimeScale != Time.timeScale)
      {
        // Once scaling has changed, keep using the adjusted timeline to avoid jumping back to system time.
        timeScaleChanged = true;
        if (!timeScaleChangeLogged)
        {
          timeScaleChangeLogged = true;
          Debug.Log("ROS2ScalableTimeSource switched to Unity-scaled time after Time.timeScale changed.");
        }
      }

      lastReadingSecs = Time.timeAsDouble;
    }
  }

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
