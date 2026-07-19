// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Source-only Phase181 custom FoxRun DTO native ROS2 interface sample.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

// IMPORTANT: The following DTO names are the static interface identity locked
// by dev.unity2foxglove.foxrun.ros2.interfaces v1. Do not rename their
// namespace, type names, or public members without intentionally revising the
// static interface package and rebuilding its distro-specific typesupport add-on.
namespace Unity.FoxgloveSDK.Tests.FoxRun.Fixtures
{
    [Serializable]
    public enum Phase181StateKind : ushort
    {
        Unknown = 0,
        Active = 1,
    }

    [Serializable]
    public sealed class Phase181NestedState
    {
        public bool Enabled { get; set; }
        public string Label { get; set; }
    }

    [Serializable]
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
}

namespace Unity2Foxglove.Ros2ForUnity.Samples
{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    /// <summary>
    /// Demonstrates the three Phase181 custom DTO contract directions. The
    /// generated binding chooses the selected static typesupport add-on; this
    /// sample neither creates a ROS2 node nor retains a ROS2 message object.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Foxglove/ROS2 For Unity/FoxRun Custom ROS2 Interface")]
    public sealed partial class Phase181FoxRunCustomRos2Interface : MonoBehaviour
    {
        public const string NativePublishTopic = "/foxrun/phase181/custom/publish";
        public const string NativeSubscribeTopic = "/foxrun/phase181/custom/subscribe";
        public const string NativeBidirectionalTopic = "/foxrun/phase181/custom/bidirectional";

        [Header("Custom DTO Native ROS2 Contracts")]
        [Tooltip("Unity publishes this custom DTO through the selected native ROS2 typesupport add-on.")]
        [FoxRun(
            NativePublishTopic,
            Mode = FoxRunMode.PublishOnly,
            Ros2Qos = FoxRunRos2QosPreset.Reliable)]
        [SerializeField] private Phase181State _nativePublishOnly = CreateState("publish-only", 1);

        [Tooltip("The selected native ROS2 runtime applies this custom DTO on Unity's main thread.")]
        [FoxRun(
            NativeSubscribeTopic,
            Mode = FoxRunMode.SubscribeOnly,
            SubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native,
            Ros2Qos = FoxRunRos2QosPreset.Reliable)]
        [SerializeField] private Phase181State _nativeSubscribeOnly;

        [Tooltip("Native ROS2 is the inbound provider while this member keeps an explicit JSON WebSocket output contract.")]
#pragma warning disable FOXRUN400 // The sample deliberately documents its bidirectional ownership and peer protocol.
        [FoxRun(
            NativeBidirectionalTopic,
            Mode = FoxRunMode.PublishAndSubscribe,
            Encoding = FoxRunWireEncoding.Json,
            SubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native,
            Ros2Qos = FoxRunRos2QosPreset.Reliable)]
        [SerializeField] private Phase181State _nativeInputWebSocketOutput = CreateState("bidirectional", 2);
#pragma warning restore FOXRUN400

        private void Reset()
        {
            _nativePublishOnly = CreateState("publish-only", 1);
            _nativeSubscribeOnly = null;
            _nativeInputWebSocketOutput = CreateState("bidirectional", 2);
        }

        private static Phase181State CreateState(string label, int count)
            => new Phase181State
            {
                Count = count,
                Kind = Phase181StateKind.Active,
                Message = label,
                Bytes = new byte[] { 0x18, 0x1, 0x81 },
                Values = new List<long> { count, count + 1L, count + 2L },
                Nested = new Phase181NestedState { Enabled = true, Label = label },
                OptionalCount = count,
                OptionalText = label,
            };
    }
}
