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
    public sealed class Ros2R2FUTopicSink : IFoxTopicSink, IFoxTopicSinkContractLifecycle
    {
        private const string DefaultNodeName = "unity2foxglove_foxrun";

        private readonly IUnity2FoxgloveRos2Context _context;
        private readonly IRos2TopicPublisherFactory _factory;
        private readonly Action<string> _log;
        private readonly IUnity2FoxgloveRos2Node _node;
        private readonly object _gate = new object();
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
            lock (_gate)
            {
                if (_disposed || contract == null)
                    return;
                if (_publishers.ContainsKey(contract.Topic))
                    return;
            }

            if (!_context.IsAvailable)
            {
                ReportOnce(contract.Topic, "ROS2 runtime unavailable (" + _context.StatusMessage + "); topic not published.");
                return;
            }

            if (_factory.TryCreate(contract, _node, out var publisher, out var reason) && publisher != null)
            {
                IRos2TopicPublisher duplicate = null;
                lock (_gate)
                {
                    if (_disposed)
                    {
                        duplicate = publisher;
                    }
                    else if (_publishers.ContainsKey(contract.Topic))
                    {
                        duplicate = publisher;
                    }
                    else
                    {
                        _publishers[contract.Topic] = publisher;
                    }
                }

                if (duplicate != null)
                {
                    try { duplicate.Dispose(); }
                    catch (Exception ex)
                    {
                        ReportOnce("dispose:duplicate:" + ex.GetType().FullName, "ROS2 duplicate publisher teardown failed: "
                            + ex.GetType().Name + ": " + ex.Message);
                    }
                }

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
            IRos2TopicPublisher publisher;
            lock (_gate)
            {
                if (_disposed || contract == null)
                    return;
                if (!_publishers.TryGetValue(contract.Topic, out publisher))
                    return;
            }

            if (payload == null)
            {
                ReportOnce(contract.Topic + ":null-payload", "ROS2 publish skipped for '" + contract.Topic + "': payload was null.");
                return;
            }

            if (!publisher.TryPublish(payload, timestampNs, out var error))
                ReportOnce(contract.Topic + ":publish", "ROS2 publish failed for '" + contract.Topic + "': " + error);
        }

        /// <summary>
        /// Release the publisher owned by one exported topic contract. A later
        /// registration for the same topic creates a fresh endpoint and QoS.
        /// </summary>
        public void Unregister(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
                return;

            IRos2TopicPublisher publisher;
            lock (_gate)
            {
                if (_disposed || !_publishers.TryGetValue(topic, out publisher))
                    return;

                _publishers.Remove(topic);
            }

            try
            {
                publisher.Dispose();
            }
            catch (Exception ex)
            {
                ReportOnce(
                    "dispose:unregister:" + topic + ":" + ex.GetType().FullName,
                    "ROS2 publisher teardown failed for '" + topic + "': "
                    + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public void Flush()
        {
            // R2FU publishes synchronously; nothing is buffered by this sink.
        }

        public void Dispose()
        {
            List<IRos2TopicPublisher> publishers;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                publishers = new List<IRos2TopicPublisher>(_publishers.Values);
                _publishers.Clear();
            }

            foreach (var publisher in publishers)
            {
                try { publisher.Dispose(); }
                catch (Exception ex)
                {
                    ReportOnce("dispose:publisher:" + ex.GetType().FullName, "ROS2 publisher teardown failed: "
                        + ex.GetType().Name + ": " + ex.Message);
                }
            }

            try { _node?.Dispose(); }
            catch (Exception ex)
            {
                ReportOnce("dispose:node:" + ex.GetType().FullName, "ROS2 node teardown failed: "
                    + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ReportOnce(string key, string message)
        {
            lock (_gate)
            {
                if (!_reported.Add(key))
                    return;
            }

            _log?.Invoke("[Ros2TopicSink] " + message);
        }
    }
}
