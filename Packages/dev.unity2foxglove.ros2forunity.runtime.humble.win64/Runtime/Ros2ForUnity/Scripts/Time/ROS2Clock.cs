// Copyright 2019-2022 Robotec.ai.
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
/// Bridges an ITimeSource to ros2cs clock, header, and builtin_interfaces/Time messages.
/// </summary>
public class ROS2Clock : IDisposable
{
    private ITimeSource _timeSource;
    private bool disposed;

    /// <summary>
    /// Creates a clock backed by ROS2TimeSource.
    /// </summary>
    public ROS2Clock() : this(new ROS2TimeSource())
    {   // By default, use ROS2TimeSource
    }

    /// <summary>
    /// Creates a clock backed by the supplied time source.
    /// </summary>
    /// <param name="ts">Time source used for all subsequent timestamp updates.</param>
    public ROS2Clock(ITimeSource ts)
    {
        _timeSource = ts;
    }

    /// <summary>
    /// Updates a rosgraph_msgs.msg.Clock message in place.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after this clock is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configured time source cannot provide a timestamp.</exception>
    public void UpdateClockMessage(ref rosgraph_msgs.msg.Clock clockMessage)
    {
        int seconds;
        uint nanoseconds;
        GetCurrentTime(out seconds, out nanoseconds);
        clockMessage.Clock_.Sec = seconds;
        clockMessage.Clock_.Nanosec = nanoseconds;
    }

    /// <summary>
    /// Updates a generated builtin_interfaces.msg.Time message in place.
    /// </summary>
    /// <remarks>
    /// Generated ros2cs messages are reference types, so the Time instance itself is passed by value while
    /// its Sec/Nanosec fields are updated.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown after this clock is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configured time source cannot provide a timestamp.</exception>
    public void UpdateROSClockTime(builtin_interfaces.msg.Time time)
    {
        int seconds;
        uint nanoseconds;
        GetCurrentTime(out seconds, out nanoseconds);
        time.Sec = seconds;
        time.Nanosec = nanoseconds;
    }

    /// <summary>
    /// Updates the header timestamp of a generated ROS message.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after this clock is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configured time source cannot provide a timestamp.</exception>
    public void UpdateROSTimestamp(ref ROS2.MessageWithHeader message)
    {
        int seconds;
        uint nanoseconds;
        GetCurrentTime(out seconds, out nanoseconds);
        message.UpdateHeaderTime(seconds, nanoseconds);
    }

    private void GetCurrentTime(out int seconds, out uint nanoseconds)
    {
        if (disposed || _timeSource == null)
        {
            throw new ObjectDisposedException(nameof(ROS2Clock));
        }
        if (!_timeSource.GetTime(out seconds, out nanoseconds))
        {
            throw new InvalidOperationException("Cannot acquire valid ROS2 time from the configured time source.");
        }
    }

    /// <summary>
    /// Disposes the owned time source when it implements IDisposable.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        IDisposable disposableTimeSource = _timeSource as IDisposable;
        if (disposableTimeSource != null)
        {
            disposableTimeSource.Dispose();
        }
        _timeSource = null;
        disposed = true;
    }
}

}  // namespace ROS2
