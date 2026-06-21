// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Attaches an outbound ROS2 sink to the FoxRun sink router without the
//          core SDK knowing any concrete ROS2 type.

using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// Registers a <see cref="Ros2R2FUTopicSink"/> with the active
    /// <see cref="FoxgloveLogHub"/> sink router so exported FoxRun topics fan out
    /// to ROS2. Subclass this and supply the concrete ros2cs message converters;
    /// the core SDK never references this component or any ROS2 type.
    /// </summary>
    public abstract class Ros2TopicSinkBootstrap : MonoBehaviour
    {
        [Tooltip("ROS2 node name used by the FoxRun outbound sink.")]
        [SerializeField] private string _nodeName = "unity2foxglove_foxrun";

        private Ros2R2FUTopicSink _sink;
        private IUnity2FoxgloveRos2Context _context;
        private FoxgloveLogHub _hub;

        /// <summary>
        /// Supply the explicit, fail-closed converter factory mapping FoxRun
        /// contracts to concrete ROS2 messages. Return <c>null</c> to disable.
        /// </summary>
        protected abstract IRos2TopicPublisherFactory CreatePublisherFactory();

        private void OnEnable() => TryAttach();

        private void Update()
        {
            // The hub auto-creates when FoxRun sources register; retry until it
            // exists, then stop. Once attached, _sink stays non-null.
            if (_sink == null)
                TryAttach();
        }

        private void TryAttach()
        {
            if (_sink != null)
                return;

            _hub = FindFirstObjectByType<FoxgloveLogHub>();
            if (_hub == null)
                return;

            var factory = CreatePublisherFactory();
            if (factory == null)
            {
                enabled = false;
                return;
            }

            _context = Unity2FoxgloveRos2ContextFactory.Create();
            _sink = new Ros2R2FUTopicSink(_context, factory, _nodeName, Debug.LogWarning);
            _hub.TopicSinkRouter.AddSink(_sink);
        }

        private void OnDisable()
        {
            if (_sink != null && _hub != null)
                _hub.TopicSinkRouter.RemoveSink(_sink);

            _sink?.Dispose();
            _context?.Dispose();
            _sink = null;
            _context = null;
        }
    }
}
