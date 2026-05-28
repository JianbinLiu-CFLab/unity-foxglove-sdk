// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime

using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    internal static class SessionFactory
    {
        public static FoxgloveSession Create(
            string name, bool enableCdrClientPublish,
            IFoxgloveTransport transport, PlaybackClock playbackClock,
            ISchemaRegistry schemaRegistry, IFoxgloveLogger logger,
            FoxgloveParameterStore parameters, FoxgloveServiceRegistry services,
            RecordingController recording,
            bool protobufSchemasRegistered, bool ros2MsgSchemasRegistered,
            IRuntimeContext runtimeContext)
        {
            var session = new FoxgloveSession(name, transport, playbackClock, schemaRegistry, logger, parameters, services);
            session.SetRuntimeContext(runtimeContext);
            if (protobufSchemasRegistered)
                session.EnableProtobuf();
            if (enableCdrClientPublish && ros2MsgSchemasRegistered)
                session.EnableCdr();
            recording.AttachToSession(parameters, session);
            return session;
        }
    }
}
