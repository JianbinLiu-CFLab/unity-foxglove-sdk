// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Convenience extension methods for publishing Google.Protobuf
// messages through FoxgloveSession.

using Google.Protobuf;
using Unity.FoxgloveSDK.Core;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Extension methods for publishing protobuf messages through FoxgloveSession.
    /// </summary>
    public static class ProtobufPublisherExtensions
    {
        /// <summary>
        /// Serialize a protobuf message and publish it to the given channel using the session clock time.
        /// The channel must have been registered with encoding "protobuf".
        /// </summary>
        public static void PublishProto(this FoxgloveSession session, uint channelId, IMessage message)
        {
            if (session == null) return;
            if (message == null) return;
            session.Publish(channelId, message.ToByteArray());
        }

        /// <summary>
        /// Serialize a protobuf message and publish it to the given channel with an explicit log timestamp.
        /// The channel must have been registered with encoding "protobuf".
        /// </summary>
        public static void PublishProto(this FoxgloveSession session, uint channelId, IMessage message, ulong logTimeNs)
        {
            if (session == null) return;
            if (message == null) return;
            session.Publish(channelId, message.ToByteArray(), logTimeNs);
        }
    }
}
