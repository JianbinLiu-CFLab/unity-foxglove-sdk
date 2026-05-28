// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime

using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Factory that wires a new <see cref="FoxgloveSession"/> with transport,
    /// clock, schema registry, parameter store, service registry, recording
    /// controller, and optional protobuf/CDR support.
    /// </summary>
    internal static class SessionFactory
    {
        /// <summary>
        /// Creates a fully-wired <see cref="FoxgloveSession"/> with protobuf and CDR
        /// support enabled when the corresponding schemas are registered.
        /// </summary>
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
