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

namespace ROS2
{

/// <summary>
/// Interace for acquiring time
/// </summary>
internal static class TimeUtils
{
  public static void TimeFromTotalSeconds(in double secondsIn, out int seconds, out uint nanoseconds)
  {
    long nanosec = (long)(secondsIn * 1e9);
    // U2F-LOCAL-PATCH: keep nanoseconds normalized for negative inputs.
    seconds = (int)Math.Floor(secondsIn);
    long normalizedNanoseconds = nanosec - ((long)seconds * 1000000000L);
    if (normalizedNanoseconds < 0)
    {
      seconds--;
      normalizedNanoseconds += 1000000000L;
    }
    else if (normalizedNanoseconds >= 1000000000L)
    {
      seconds++;
      normalizedNanoseconds -= 1000000000L;
    }
    nanoseconds = (uint)normalizedNanoseconds;
  }
}

}  // namespace ROS2
