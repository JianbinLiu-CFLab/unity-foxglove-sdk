// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/NativeCompileStubs
// Purpose: Compile-surface-only stand-ins for the tracked Phase181 fixture.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;

namespace unity2foxglove_foxrun_interfaces_v1.msg
{
    /// <summary>
    /// Compile-surface-only envelope identity.  Real artifacts are generated
    /// and inspected by the Phase181 candidate/add-on validators; this class
    /// must never be treated as a custom message payload at runtime.
    /// </summary>
    public sealed class Phase181State48D288ED82F1Envelope : ROS2.Message
    {
        public bool IsDisposed { get { return false; } }
        public void Dispose() { }
    }

    /// <summary>Compile-surface-only payload identity for a closed mapping.</summary>
    public sealed class Phase181State48D288ED82F1 : ROS2.Message
    {
        public bool IsDisposed { get { return false; } }
        public void Dispose() { }
    }

    /// <summary>Compile-surface-only nested message identity.</summary>
    public sealed class Phase181NestedState3281D0E21244 : ROS2.Message
    {
        public bool IsDisposed { get { return false; } }
        public void Dispose() { }
    }
}
#endif
