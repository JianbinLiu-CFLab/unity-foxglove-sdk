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

namespace ROS2
{

/// <summary>
/// Interface for acquiring ROS-compatible timestamp fields from a concrete time source.
/// </summary>
public interface ITimeSource
{
  /// <summary>
  /// Tries to acquire the current timestamp for ROS message headers and clock messages.
  /// </summary>
  /// <param name="seconds">Whole seconds of the acquired timestamp, or 0 when this method returns false.</param>
  /// <param name="nanoseconds">Nanoseconds within the second, or 0 when this method returns false.</param>
  /// <returns>True when a valid timestamp was acquired; false when the source is not currently usable.</returns>
  /// <remarks>
  /// Epoch semantics are source-specific: DotnetTimeSource and ROS2TimeSource report Unix/ROS-aligned time,
  /// while UnityTimeSource reports Unity play time. Callers must not use the out values when this method
  /// returns false.
  /// </remarks>
  bool GetTime(out int seconds, out uint nanoseconds);
}

}  // namespace ROS2
