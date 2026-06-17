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
/// ros2 time source (system time by default).
/// </summary>
public class ROS2ScalableTimeSource : ITimeSource, IDisposable
{
  private readonly object mutex = new object();
  private readonly object clockMutex = new object();
  private int mainThreadId;
  private double lastReadingSecs;
  private ROS2.Clock clock;
  private double rosUnityTimeOffset = 0;
  private double initialTimeScale = 0;
  private bool rosUnityTimeOffsetAcquired = false;
  private bool initialTimeScaleAcquired = false;
  private bool timeScaleChanged = false;

  public ROS2ScalableTimeSource()
  {
    mainThreadId = Thread.CurrentThread.ManagedThreadId;
    UpdateUnityTimeSnapshot();
  }

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

    bool isMainThread = mainThreadId == Thread.CurrentThread.ManagedThreadId;
    if (isMainThread)
    {
      UpdateUnityTimeSnapshot();
    }

    double readingSecs;
    double scaleAtRead;
    bool scaleChangedAtRead;
    bool offsetAcquiredAtRead;
    lock (mutex)
    {
      readingSecs = lastReadingSecs;
      scaleAtRead = initialTimeScale;
      scaleChangedAtRead = timeScaleChanged;
      offsetAcquiredAtRead = rosUnityTimeOffsetAcquired;
    }

    if (scaleAtRead == 1.0 && !scaleChangedAtRead)
    {
      // Until Unity timeScale changes, preserve the default ROS/system clock behavior.
      TimeUtils.TimeFromTotalSeconds(GetRosNowSeconds(), out seconds, out nanoseconds);
    }
    else
    {
      double rosNow = offsetAcquiredAtRead ? 0.0 : GetRosNowSeconds();
      double adjustedTime;
      lock (mutex)
      {
        if (!rosUnityTimeOffsetAcquired)
        {
          rosUnityTimeOffsetAcquired = true;
          rosUnityTimeOffset = rosNow - readingSecs;
        }
        adjustedTime = readingSecs + rosUnityTimeOffset;
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
