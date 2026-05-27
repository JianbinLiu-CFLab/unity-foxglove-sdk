// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Message-to-CDR-to-bridge publisher wrapper for the Phase 94 spike.

using System;
using System.Threading;
using Google.Protobuf;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Serializes generated Foxglove protobuf messages to CDR and sends bridge frames.</summary>
    public sealed class Ros2BridgePublisher
    {
        public const int DefaultSendTimeoutMs = 3000;

        private readonly IRos2BridgeSink _sink;
        private long _sequence;

        public Ros2BridgePublisher(IRos2BridgeSink sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>
        /// Serialize one generated Foxglove protobuf message to ROS2 CDR and enqueue it for the bridge sidecar.
        /// </summary>
        /// <remarks>
        /// The bridge sink controls its own backpressure and connection policy. This method can throw validation,
        /// serialization, or sink exceptions; callers that publish from Unity Update should catch and rate-limit user-facing errors.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the topic, schema, encoding, or serialized payload is invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the sink rejects the frame or is disconnected.</exception>
        public void Publish(string topic, string schemaName, IMessage message, ulong logTimeNs)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            var payload = Ros2CdrSerializerRegistry.Serialize(schemaName, message);
            Ros2CdrPayloadValidator.Validate(payload);
            var sequence = unchecked((ulong)Interlocked.Increment(ref _sequence));
            var frame = new Ros2BridgeFrame(topic, schemaName, Ros2BridgeFrame.CdrEncoding, logTimeNs, sequence, payload);
            _sink.Send(frame, DefaultSendTimeoutMs);
        }
    }
}
