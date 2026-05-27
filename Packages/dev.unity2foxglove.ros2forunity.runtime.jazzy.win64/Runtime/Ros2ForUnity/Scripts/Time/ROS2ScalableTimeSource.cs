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
using System.Threading;
using UnityEngine;

namespace ROS2
{

/// <summary>
/// ros2 time source (system time by default).
/// </summary>
public class ROS2ScalableTimeSource : ITimeSource, IDisposable
{
  private Thread mainThread;
  private double lastReadingSecs;
  private double lastTimeScale;
  private ROS2.Clock clock;
  private double initialTime = 0;
  private double initialTimeScale = 0;
  private bool initialTimeAcquired = false;
  private bool initialTimeScaleAcquired = false;
  private bool timeScaleChanged = false;

  public ROS2ScalableTimeSource()
  {
    mainThread = Thread.CurrentThread;
    RefreshUnityTimeCache();
  }

  public void GetTime(out int seconds, out uint nanoseconds)
  {
    if (!ROS2.Ros2cs.Ok())
    {
      seconds = 0;
      nanoseconds = 0;
      Debug.LogWarning("Cannot acquire valid ros time, ros either not initialized or shut down already");
      return;
    }

    if (clock == null)
    { // Create clock which uses system time by default (unless use_sim_time is set in ros2)
      clock = new ROS2.Clock();
    }

    if (mainThread.Equals(Thread.CurrentThread))
    {
      RefreshUnityTimeCache();
    }

    if (!initialTimeScaleAcquired)
    {
      initialTimeScaleAcquired = true;
      initialTimeScale = lastTimeScale;
    }

    if (initialTimeScale != lastTimeScale)
    {
      timeScaleChanged = true;
    }

    if (initialTimeScale == 1.0 && !timeScaleChanged)
    {
      TimeUtils.TimeFromTotalSeconds(clock.Now.Seconds, out seconds, out nanoseconds);
    }
    else
    {
      if (!initialTimeAcquired)
      {
        initialTimeAcquired = true;
        initialTime = clock.Now.Seconds - lastReadingSecs;
      }
      TimeUtils.TimeFromTotalSeconds(lastReadingSecs + initialTime, out seconds, out nanoseconds);
    }
  }

  private void RefreshUnityTimeCache()
  {
    lastReadingSecs = Time.timeAsDouble;
    lastTimeScale = Time.timeScale;
  }

  public void Dispose()
  {
    // U2F-LOCAL-PATCH: avoid native cleanup from the finalizer thread.
    if (clock != null)
    {
      clock.Dispose();
      clock = null;
    }
  }
}

}  // namespace ROS2
