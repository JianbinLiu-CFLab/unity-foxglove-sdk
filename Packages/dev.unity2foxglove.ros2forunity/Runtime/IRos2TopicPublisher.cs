// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Pluggable converter boundary that maps a FoxRun topic payload to a
//          concrete ROS2 message and publishes it, isolating ros2cs message
//          types from the generic sink.

using System;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// A topic-bound publisher that converts an already-serialized FoxRun JSON
    /// payload into one concrete ROS2 message and publishes it. One instance is
    /// created per supported topic.
    /// </summary>
    /// <remarks>
    /// This is the only seam that knows concrete ros2cs message types. The
    /// <see cref="Ros2R2FUTopicSink"/> stays message-type agnostic so it can be
    /// reviewed and reasoned about without the ROS2 runtime present.
    /// </remarks>
    public interface IRos2TopicPublisher : IDisposable
    {
        /// <summary>Normalized ROS2 topic this publisher targets.</summary>
        string Topic { get; }

        /// <summary>
        /// Convert <paramref name="jsonPayload"/> to the concrete ROS2 message and
        /// publish it. Returns <c>false</c> with a concise <paramref name="error"/>
        /// instead of throwing for conversion or runtime failures.
        /// </summary>
        bool TryPublish(byte[] jsonPayload, ulong timestampNs, out string error);
    }

    /// <summary>
    /// Resolves a concrete <see cref="IRos2TopicPublisher"/> for a FoxRun topic
    /// contract, or fails closed for unsupported contracts.
    /// </summary>
    public interface IRos2TopicPublisherFactory
    {
        /// <summary>
        /// Try to create a publisher for <paramref name="contract"/> on
        /// <paramref name="node"/>. Implementations must return <c>false</c> with a
        /// <paramref name="reason"/> for any contract they do not explicitly support
        /// — there is no best-guess conversion.
        /// </summary>
        bool TryCreate(
            FoxTopicContract contract,
            IUnity2FoxgloveRos2Node node,
            out IRos2TopicPublisher publisher,
            out string reason);
    }
}
