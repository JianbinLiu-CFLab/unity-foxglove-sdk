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
        public string Foxrun_origin_id { get; set; }
        public ulong Foxrun_sequence { get; set; }
        public builtin_interfaces.msg.Time Foxrun_stamp { get; set; }
        public Phase181State48D288ED82F1 Payload { get; set; }
        public bool IsDisposed { get { return false; } }
        public void Dispose() { }
    }

    /// <summary>Compile-surface-only payload identity for a closed mapping.</summary>
    public sealed class Phase181State48D288ED82F1 : ROS2.Message
    {
        public byte[] Bytes { get; set; }
        public bool Foxrun_has_bytes { get; set; }
        public int Count { get; set; }
        public ushort Kind { get; set; }
        public string Message { get; set; }
        public bool Foxrun_has_message { get; set; }
        public Phase181NestedState3281D0E21244 Nested { get; set; }
        public bool Foxrun_has_nested { get; set; }
        public int Optional_count { get; set; }
        public bool Foxrun_has_optional_count { get; set; }
        public string Optional_text { get; set; }
        public bool Foxrun_has_optional_text { get; set; }
        public long[] Values { get; set; }
        public bool Foxrun_has_values { get; set; }
        public bool IsDisposed { get { return false; } }
        public void Dispose() { }
    }

    /// <summary>Compile-surface-only nested message identity.</summary>
    public sealed class Phase181NestedState3281D0E21244 : ROS2.Message
    {
        public bool Enabled { get; set; }
        public string Label { get; set; }
        public bool Foxrun_has_label { get; set; }
        public bool IsDisposed { get { return false; } }
        public void Dispose() { }
    }
}
#endif
