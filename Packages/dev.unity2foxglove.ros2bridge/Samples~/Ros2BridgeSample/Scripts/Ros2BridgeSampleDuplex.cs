// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Ros2BridgeSample
// Purpose: Visible publish, subscribe, and full-duplex Bridge contracts.

using System;
using Google.Protobuf.WellKnownTypes;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity2Foxglove.Ros2Bridge.Sample
{
    /// <summary>
    /// Demonstrates the three Bridge directions with the maintained
    /// foxglove_msgs/msg/Log CDR codec. The full-duplex value changes locally
    /// only when the operator presses the button, so an inbound value can be
    /// observed without creating an immediate causal echo.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Foxglove/Samples/ROS2 Bridge Duplex")]
    public sealed partial class Ros2BridgeSampleDuplex : MonoBehaviour
    {
        public const string PublishTopic = "/ros2_bridge_sample/publish";
        public const string SubscribeTopic = "/ros2_bridge_sample/subscribe";
        public const string DuplexTopic = "/ros2_bridge_sample/duplex";

        [FoxRun(
            PublishTopic,
            Mode = FoxRunFlow.Publish,
            PublishTransportIds = new[]
            {
                Ros2BridgeTransportProvider.ProviderId
            })]
        private Foxglove.Log _publish = new Foxglove.Log();

        [FoxRun(
            SubscribeTopic,
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId = Ros2BridgeTransportProvider.ProviderId)]
        private Foxglove.Log _incoming = new Foxglove.Log();

        [FoxRun(
            DuplexTopic,
            Mode = FoxRunFlow.PublishAndSubscribe,
            Policy = FoxRunPolicy.Change,
            SubscribeTransportId = Ros2BridgeTransportProvider.ProviderId,
            PublishTransportIds = new[]
            {
                Ros2BridgeTransportProvider.ProviderId
            })]
        private Foxglove.Log _duplex = new Foxglove.Log();

        [SerializeField, Min(0.1f)] private float _publishIntervalSeconds = 1f;
        [SerializeField] private bool _showDuplexOverlay = true;
        [SerializeField] private string _lastSubscribedMessage = "waiting";
        [SerializeField] private string _lastDuplexMessage = "waiting";
        [SerializeField] private int _localDuplexRevision;

        private float _nextPublishTime;
        private int _publishRevision;
        private string _observedSubscribeMessage = string.Empty;
        private string _observedDuplexMessage = string.Empty;

        public string LastSubscribedMessage => _lastSubscribedMessage;
        public string LastDuplexMessage => _lastDuplexMessage;
        public int LocalDuplexRevision => _localDuplexRevision;

        private void Awake()
        {
            _publish = CreateLog("Unity publish sample 0", "publish");
            _incoming ??= new Foxglove.Log();
            _duplex ??= new Foxglove.Log();
            _nextPublishTime = Time.unscaledTime;
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextPublishTime)
            {
                _publishRevision++;
                _publish = CreateLog(
                    "Unity publish sample " + _publishRevision,
                    "publish");
                _nextPublishTime = Time.unscaledTime
                                   + Mathf.Max(0.1f, _publishIntervalSeconds);
            }

            ObserveInbound(_incoming, ref _observedSubscribeMessage,
                ref _lastSubscribedMessage);
            ObserveInbound(_duplex, ref _observedDuplexMessage,
                ref _lastDuplexMessage);
        }

        /// <summary>
        /// Creates a distinct local value after a remote full-duplex apply.
        /// Generated origin governance then permits one later local publish.
        /// </summary>
        public void PublishLocalDuplexMutation()
        {
            _localDuplexRevision++;
            _duplex = CreateLog(
                "Unity local duplex B" + _localDuplexRevision,
                "duplex-local");
            _lastDuplexMessage = _duplex.Message;
            _observedDuplexMessage = _duplex.Message;
        }

        private void OnGUI()
        {
            if (!_showDuplexOverlay)
                return;

            GUI.Box(new Rect(12, 48, 760, 116), "ROS2 Bridge FoxRun directions");
            GUI.Label(
                new Rect(24, 72, 720, 22),
                "Subscribe A: " + Bound(_lastSubscribedMessage));
            GUI.Label(
                new Rect(24, 94, 720, 22),
                "Duplex A/B: " + Bound(_lastDuplexMessage));
            if (GUI.Button(
                    new Rect(24, 120, 280, 30),
                    "Publish distinct local duplex B"))
            {
                PublishLocalDuplexMutation();
            }
        }

        private static void ObserveInbound(
            Foxglove.Log value,
            ref string observed,
            ref string display)
        {
            var message = value?.Message ?? "<null>";
            if (string.Equals(message, observed, StringComparison.Ordinal))
                return;
            observed = message;
            display = message;
        }

        private static Foxglove.Log CreateLog(string message, string name)
            => new Foxglove.Log
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Level = Foxglove.Log.Types.Level.Info,
                Message = message,
                Name = "Ros2BridgeSample/" + name,
                File = nameof(Ros2BridgeSampleDuplex),
                Line = 186
            };

        private static string Bound(string value)
        {
            const int maximum = 96;
            value ??= string.Empty;
            return value.Length <= maximum
                ? value
                : value.Substring(0, maximum);
        }
    }
}
