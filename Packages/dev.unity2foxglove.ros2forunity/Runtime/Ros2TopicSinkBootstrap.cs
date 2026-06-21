// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Attaches an outbound ROS2 sink to the FoxRun sink router without the
//          core SDK knowing any concrete ROS2 type.

using System;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// Registers a <see cref="Ros2R2FUTopicSink"/> with the active
    /// <see cref="FoxgloveLogHub"/> sink router so exported FoxRun topics fan out
    /// to ROS2. Host components supply the concrete generated ROS2 message
    /// converters and context provider; the core SDK never references this
    /// bootstrap or any ROS2 type.
    /// </summary>
    public sealed class Ros2TopicSinkBootstrap : IDisposable
    {
        private readonly string _nodeName;
        private readonly Func<IRos2TopicPublisherFactory> _createPublisherFactory;
        private readonly Func<IUnity2FoxgloveRos2Context> _createContext;
        private readonly Action<string> _logWarning;

        private Ros2R2FUTopicSink _sink;
        private IUnity2FoxgloveRos2Context _context;
        private FoxTopicSinkRouter _router;

        public Ros2TopicSinkBootstrap(
            string nodeName,
            Func<IRos2TopicPublisherFactory> createPublisherFactory,
            Func<IUnity2FoxgloveRos2Context> createContext,
            Action<string> logWarning = null)
        {
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? "unity2foxglove_foxrun" : nodeName;
            _createPublisherFactory = createPublisherFactory ?? throw new ArgumentNullException(nameof(createPublisherFactory));
            _createContext = createContext ?? throw new ArgumentNullException(nameof(createContext));
            _logWarning = logWarning;
        }

        public bool IsAttached => _sink != null;

        /// <summary>
        /// Attempts to attach the ROS2 sink to the active FoxRun sink router.
        /// Returns <c>false</c> when the router is not ready or providers disable
        /// the sink.
        /// </summary>
        public bool TryAttach()
        {
            if (_sink != null)
                return true;

            if (!FoxgloveLogHub.TryGetTopicSinkRouter(out _router))
                return false;

            var factory = _createPublisherFactory();
            if (factory == null)
                return false;

            _context = _createContext();
            if (_context == null)
                return false;

            _sink = new Ros2R2FUTopicSink(_context, factory, _nodeName, _logWarning);
            _router.AddSink(_sink);
            return true;
        }

        public void Detach()
        {
            if (_sink != null && _router != null)
                _router.RemoveSink(_sink);

            _sink?.Dispose();
            _context?.Dispose();
            _sink = null;
            _context = null;
            _router = null;
        }

        public void Dispose() => Detach();
    }
}
