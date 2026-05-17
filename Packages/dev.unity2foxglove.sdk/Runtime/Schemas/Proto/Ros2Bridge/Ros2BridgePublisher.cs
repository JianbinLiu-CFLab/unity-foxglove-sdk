// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Message-to-CDR-to-bridge publisher wrapper for the Phase 94 spike.

using System;
using Google.Protobuf;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Serializes generated Foxglove protobuf messages to CDR and sends bridge frames.</summary>
    public sealed class Ros2BridgePublisher
    {
        public const int DefaultSendTimeoutMs = 3000;

        private readonly IRos2BridgeSink _sink;
        private ulong _sequence;

        public Ros2BridgePublisher(IRos2BridgeSink sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public void Publish(string topic, string schemaName, IMessage message, ulong logTimeNs)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            var payload = Ros2CdrSerializerRegistry.Serialize(schemaName, message);
            Ros2CdrPayloadValidator.Validate(payload);
            var frame = new Ros2BridgeFrame(topic, schemaName, Ros2BridgeFrame.CdrEncoding, logTimeNs, ++_sequence, payload);
            _sink.Send(frame, DefaultSendTimeoutMs);
        }
    }
}
