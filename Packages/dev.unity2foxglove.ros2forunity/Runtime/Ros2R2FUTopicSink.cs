// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Optional outbound ROS2 sink that fans exported FoxRun topics into
//          ROS2 through the validated R2FU facade.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

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
    public sealed class Ros2R2FUTopicSink :
        IFoxTopicSink,
        IFoxTopicSinkContractLifecycle,
        IFoxTopicResolvedContractSink,
        IFoxTopicTargetSink
    {
        private const string DefaultNodeName = "unity2foxglove_foxrun";

        private readonly IUnity2FoxgloveRos2Context _context;
        private readonly IRos2TopicPublisherFactory _factory;
        private readonly Action<string> _log;
        private readonly IUnity2FoxgloveRos2Node _node;
        private readonly object _gate = new object();
        private readonly Dictionary<string, IRos2TopicPublisher> _publishers = new Dictionary<string, IRos2TopicPublisher>(StringComparer.Ordinal);
        private readonly Dictionary<string, FoxRunResolvedQos?> _publisherQos =
            new Dictionary<string, FoxRunResolvedQos?>(StringComparer.Ordinal);
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

        public FoxRunEndpoint Target => FoxRunEndpoint.Ros2Native;

        public bool IsReady(FoxTopicContract contract, out string reason)
        {
            reason = string.Empty;
            lock (_gate)
            {
                if (_disposed)
                {
                    reason = "ROS2 target sink is disposed.";
                    return false;
                }
                if (!_context.IsAvailable)
                {
                    reason = "ROS2 runtime unavailable (" + _context.StatusMessage + ").";
                    return false;
                }
                if (contract == null || !_publishers.ContainsKey(contract.Topic))
                {
                    reason = "No ROS2 publisher is registered for the topic.";
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Resolve a concrete ROS2 publisher for the contract. Unsupported
        /// contracts and an unavailable runtime fail closed with a one-time reason.
        /// </summary>
        public void Register(FoxTopicContract contract)
            => RegisterCore(contract, null);

        void IFoxTopicResolvedContractSink.Register(
            FoxTopicContract contract,
            FoxRunResolvedPublishContract resolved)
            => RegisterCore(contract, resolved?.NativeQos);

        private void RegisterCore(
            FoxTopicContract contract,
            FoxRunResolvedQos? resolvedQos)
        {
            IRos2TopicPublisher replaced = null;
            lock (_gate)
            {
                if (_disposed || contract == null)
                    return;
                if (_publishers.TryGetValue(contract.Topic, out var existing))
                {
                    _publisherQos.TryGetValue(contract.Topic, out var existingQos);
                    if (Nullable.Equals(existingQos, resolvedQos))
                        return;
                    _publishers.Remove(contract.Topic);
                    _publisherQos.Remove(contract.Topic);
                    replaced = existing;
                }
            }

            DisposePublisher(replaced, "qos-replace:" + contract.Topic);

            if (!_context.IsAvailable)
            {
                ReportOnce(contract.Topic, "ROS2 runtime unavailable (" + _context.StatusMessage + "); topic not published.");
                return;
            }

            IRos2TopicPublisher publisher;
            string reason;
            bool created;
            if (resolvedQos.HasValue)
            {
                if (_factory is IRos2QosAwareTopicPublisherFactory qosFactory)
                {
                    created = qosFactory.TryCreate(
                        contract,
                        resolvedQos.Value,
                        _node,
                        out publisher,
                        out reason);
                }
                else if (resolvedQos.Value != FoxRunResolvedQos.Default)
                {
                    created = false;
                    publisher = null;
                    reason = "publisher factory is not QoS-aware and cannot apply the resolved native QoS";
                }
                else
                {
                    created = _factory.TryCreate(
                        contract,
                        _node,
                        out publisher,
                        out reason);
                }
            }
            else
            {
                created = _factory.TryCreate(
                    contract,
                    _node,
                    out publisher,
                    out reason);
            }

            if (created && publisher != null)
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
                        _publisherQos[contract.Topic] = resolvedQos;
                    }
                }

                DisposePublisher(duplicate, "duplicate:" + contract.Topic);

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
            if (!TryPublish(contract, timestampNs, payload, origin, out var reason)
                && contract != null
                && !string.IsNullOrWhiteSpace(reason))
            {
                ReportOnce(contract.Topic + ":publish", reason);
            }
        }

        public bool TryPublish(
            FoxTopicContract contract,
            ulong timestampNs,
            byte[] payload,
            string origin,
            out string reason)
        {
            reason = string.Empty;
            IRos2TopicPublisher publisher;
            lock (_gate)
            {
                if (_disposed)
                {
                    reason = "ROS2 target sink is disposed.";
                    return false;
                }
                if (contract == null)
                {
                    reason = "ROS2 publish contract is missing.";
                    return false;
                }
                if (!_publishers.TryGetValue(contract.Topic, out publisher))
                {
                    reason = "No ROS2 publisher is registered for FoxRun topic '" + contract.Topic + "'.";
                    return false;
                }
            }

            if (payload == null)
            {
                reason = "ROS2 publish skipped for '" + contract.Topic + "': payload was null.";
                return false;
            }

            if (!publisher.TryPublish(payload, timestampNs, out var error))
            {
                reason = "ROS2 publish failed for '" + contract.Topic + "': " + error;
                return false;
            }

            return true;
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
                _publisherQos.Remove(topic);
            }

            DisposePublisher(publisher, "unregister:" + topic);
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
                _publisherQos.Clear();
            }

            ExceptionDispatchInfo fatal = null;
            foreach (var publisher in publishers)
            {
                try
                {
                    publisher.Dispose();
                }
                catch (Exception ex) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(ex))
                {
                    try
                    {
                        ReportOnce("dispose:publisher:" + ex.GetType().FullName, "ROS2 publisher teardown failed: "
                            + ex.GetType().Name + ": " + ex.Message);
                    }
                    catch (Exception reportException)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(reportException);
                    }
                }
                catch (Exception ex)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(ex);
                }
            }

            try
            {
                _node?.Dispose();
            }
            catch (Exception ex) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(ex))
            {
                try
                {
                    ReportOnce("dispose:node:" + ex.GetType().FullName, "ROS2 node teardown failed: "
                        + ex.GetType().Name + ": " + ex.Message);
                }
                catch (Exception reportException)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(reportException);
                }
            }
            catch (Exception ex)
            {
                fatal ??= ExceptionDispatchInfo.Capture(ex);
            }

            fatal?.Throw();
        }

        private void ReportOnce(string key, string message)
        {
            lock (_gate)
            {
                if (!_reported.Add(key))
                    return;
            }

            try
            {
                _log?.Invoke("[Ros2TopicSink] " + message);
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                // Diagnostics cannot prevent endpoint teardown or routing.
            }
        }

        private void DisposePublisher(IRos2TopicPublisher publisher, string context)
        {
            if (publisher == null)
                return;
            try
            {
                publisher.Dispose();
            }
            catch (Exception ex) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(ex))
            {
                ReportOnce(
                    "dispose:" + context + ":" + ex.GetType().FullName,
                    "ROS2 publisher teardown failed (" + context + "): "
                    + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
