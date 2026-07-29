// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime

using System.Collections.Generic;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Factory that wires a new <see cref="FoxgloveSession"/> with transport,
    /// clock, schema registry, parameter store, service registry, recording
    /// controller, and optional message encodings.
    /// </summary>
    internal static class SessionFactory
    {
        /// <summary>
        /// Creates a fully-wired <see cref="FoxgloveSession"/> with protobuf and
        /// immutable Provider-owned message encodings.
        /// </summary>
        public static FoxgloveSession Create(
            string name,
            IFoxgloveTransport transport, PlaybackClock playbackClock,
            ISchemaRegistry schemaRegistry, IFoxgloveLogger logger,
            FoxgloveParameterStore parameters, FoxgloveServiceRegistry services,
            RecordingController recording,
            bool protobufSchemasRegistered,
            IReadOnlyCollection<string> additionalMessageEncodings,
            IRuntimeContext runtimeContext,
            ISinkChannelFilter liveWebSocketChannelFilter = null,
            ISinkChannelFilter mcapRecordingChannelFilter = null,
            IFoxgloveMirrorSink mirrorSink = null)
        {
            var session = new FoxgloveSession(name, transport, playbackClock, schemaRegistry, logger, parameters, services);
            session.SetRuntimeContext(runtimeContext);
            session.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, liveWebSocketChannelFilter);
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording, mcapRecordingChannelFilter);
            session.SetMirrorSink(mirrorSink, replayExistingChannels: false);
            if (protobufSchemasRegistered)
                session.EnableProtobuf();
            if (additionalMessageEncodings != null)
            {
                foreach (var encoding in additionalMessageEncodings)
                    session.EnableMessageEncoding(encoding);
            }
            recording.AttachToSession(parameters, session);
            return session;
        }
    }
}
