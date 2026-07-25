// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Shared fatal-exception boundary for adapter and native ROS 2 ownership.

using System;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Classifies exceptions that may be isolated from one ROS 2 endpoint.
    /// Fatal process/runtime failures must escape after mandatory cleanup.
    /// </summary>
    internal static class FoxRunRos2NativeExceptionPolicy
    {
        internal static bool IsRecoverable(Exception exception)
            => !(exception is OutOfMemoryException)
               && !(exception is StackOverflowException)
               && !(exception is AccessViolationException)
               && !(exception is AppDomainUnloadedException);
    }
}
