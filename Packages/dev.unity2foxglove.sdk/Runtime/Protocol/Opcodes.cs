// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol
// Purpose: Foxglove WebSocket protocol v1 binary opcode constants.

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Foxglove WebSocket protocol v1 binary opcodes (server → client).</summary>
    public static class ServerOpcode
    {
        public const byte MessageData = 1;
        public const byte Time = 2;
        public const byte ServiceCallResponse = 3;
        public const byte FetchAssetResponse = 4;
        public const byte PlaybackState = 5;
    }

    /// <summary>Foxglove WebSocket protocol v1 binary opcodes (client → server).</summary>
    public static class ClientOpcode
    {
        public const byte MessageData = 1;
        public const byte ServiceCallRequest = 2;
        public const byte PlaybackControlRequest = 3;
    }
}
