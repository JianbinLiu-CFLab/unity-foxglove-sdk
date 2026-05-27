// Copyright 2022 Robotec.ai.
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
/// Acquires Unity time. Note that Time API only allows main thread access,
/// but this class object also stores last acquired value for other threads.
/// This is done without a warning, so the class will not behave as expected
/// when not used by main thread.
/// </summary>
public class UnityTimeSource : ITimeSource
{
  private readonly object mutex = new object();
  private int mainThreadId;
  private double lastReadingSecs;

  public UnityTimeSource()
  {
    mainThreadId = Thread.CurrentThread.ManagedThreadId;
    try
    {
      lastReadingSecs = Time.timeAsDouble;
    }
    catch (UnityException exception)
    {
      throw new InvalidOperationException(
          "UnityTimeSource must be constructed on the Unity main thread.", exception);
    }
  }

  public void GetTime(out int seconds, out uint nanoseconds)
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
      lock (mutex)
      {
        reading = lastReadingSecs;
      }
    }
    TimeUtils.TimeFromTotalSeconds(reading, out seconds, out nanoseconds);
  }
}

}  // namespace ROS2
