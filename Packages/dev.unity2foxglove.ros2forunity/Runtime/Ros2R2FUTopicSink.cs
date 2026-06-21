// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Optional outbound ROS2 sink that fans exported FoxRun topics into
//          ROS2 through the validated R2FU facade.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// An <see cref="IFoxTopicSink"/> that publishes exported FoxRun topics into
    /// ROS2 through the optional ROS2 For Unity facade.
    /// </summary>
    /// <remarks>
    /// Outbound only: inbound ROS2 subscriptions are owned by a later phase. The
    /// sink is message-type agnostic — concrete generated ROS2 message conversion lives in
    /// the injected <see cref="IRos2TopicPublisherFactory"/>, which fails closed for
    /// any unsupported contract (no best-guess conversion). The core SDK never
    /// references this type; it is added to <c>FoxgloveLogHub.TopicSinkRouter</c>
    /// from this optional package, preserving the core's ROS2-free boundary.
    /// </remarks>
    public sealed class Ros2R2FUTopicSink : IFoxTopicSink
    {
        private const string DefaultNodeName = "unity2foxglove_foxrun";

        private readonly IUnity2FoxgloveRos2Context _context;
        private readonly IRos2TopicPublisherFactory _factory;
        private readonly Action<string> _log;
        private readonly IUnity2FoxgloveRos2Node _node;
        private readonly Dictionary<string, IRos2TopicPublisher> _publishers = new Dictionary<string, IRos2TopicPublisher>(StringComparer.Ordinal);
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
        private bool _disposed;

        public Ros2R2FUTopicSink(
            IUnity2FoxgloveRos2Context context,
            IRos2TopicPublisherFactory factory,
            string nodeName = DefaultNodeName,
            Action<string> log = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _log = log;
            _node = context.CreateNode(string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName);
        }

        public string Name => "ros2-r2fu";

        public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.External;

        /// <summary>
        /// Resolve a concrete ROS2 publisher for the contract. Unsupported
        /// contracts and an unavailable runtime fail closed with a one-time reason.
        /// </summary>
        public void Register(FoxTopicContract contract)
        {
            if (_disposed || contract == null)
                return;
            if (_publishers.ContainsKey(contract.Topic))
                return;

            if (!_context.IsAvailable)
            {
                ReportOnce(contract.Topic, "ROS2 runtime unavailable (" + _context.StatusMessage + "); topic not published.");
                return;
            }

            if (_factory.TryCreate(contract, _node, out var publisher, out var reason) && publisher != null)
            {
                _publishers[contract.Topic] = publisher;
                return;
            }

            ReportOnce(contract.Topic, "no explicit ROS2 mapping for FoxRun topic '" + contract.Topic
                + "' (schema '" + contract.SchemaName + "'): " + (reason ?? "unsupported."));
        }

        /// <summary>
        /// Publish one serialized FoxRun payload to ROS2 if the topic resolved a
        /// supported mapping. Unsupported topics were already reported at register.
        /// </summary>
        public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin)
        {
            if (_disposed || contract == null)
                return;
            if (!_publishers.TryGetValue(contract.Topic, out var publisher))
                return;

            if (!publisher.TryPublish(payload ?? Array.Empty<byte>(), timestampNs, out var error))
                ReportOnce(contract.Topic + ":publish", "ROS2 publish failed for '" + contract.Topic + "': " + error);
        }

        public void Flush()
        {
            // R2FU publishes synchronously; nothing is buffered by this sink.
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var publisher in _publishers.Values)
            {
                try { publisher.Dispose(); }
                catch { /* best-effort teardown */ }
            }

            _publishers.Clear();
            try { _node?.Dispose(); }
            catch { /* best-effort teardown */ }
        }

        private void ReportOnce(string key, string message)
        {
            if (!_reported.Add(key))
                return;

            _log?.Invoke("[Ros2TopicSink] " + message);
        }
    }
}
