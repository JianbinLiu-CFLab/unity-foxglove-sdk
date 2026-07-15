// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Public closed-generic registration seam consumed by generated user code.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Receives generated, statically typed subscription bindings. Implementations
    /// own mailbox and transport lifecycle; generated delegates own message graph
    /// copying, main-thread assignment, conditional clearing, and disposal.
    /// </summary>
    public interface IFoxRunRos2SubscriptionRegistrar
    {
        void Register<T>(
            FoxRunRos2GeneratedContract contract,
            Func<T, FoxRunRos2CopyContext, T> copy,
            Action<T> dispose,
            Action<T> apply,
            Func<T, bool> clearIfOwned)
            where T : ROS2.Message, new();
    }
}
#endif
