// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Project-owned deterministic input for Phase181 static ROS2 interface tests.

using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests.FoxRun.Fixtures
{
    public sealed partial class Phase181CustomInterfaceFixture
    {
        [FoxRun(
            "/phase181/custom_state",
            Mode = FoxRunFlow.PublishAndSubscribe,
            Encoding = FoxRunWireEncoding.Json,
            SubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native,
            Ros2Qos = FoxRunRos2QosPreset.Reliable)]
        public Phase181State State { get; set; }
    }

    public enum Phase181StateKind : ushort
    {
        Unknown = 0,
        Active = 1
    }

    public sealed class Phase181NestedState
    {
        public bool Enabled { get; set; }
        public string Label { get; set; }
    }

    public sealed class Phase181State
    {
        public int Count { get; set; }
        public Phase181StateKind Kind { get; set; }
        public string Message { get; set; }
        public byte[] Bytes { get; set; }
        public List<long> Values { get; set; }
        public Phase181NestedState Nested { get; set; }
        public int? OptionalCount { get; set; }
        public string OptionalText { get; set; }
    }

    /// <summary>Deliberately wire-distinct fixture used to prove revision gating.</summary>
    public sealed class Phase181StateV2
    {
        public int Count { get; set; }
        public double Velocity { get; set; }
    }
}
