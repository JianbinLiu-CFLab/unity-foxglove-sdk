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
        /// <summary>MessageData frame: opcode(1) + subscriptionId(u32) + logTime(u64) + payload.</summary>
        public const byte MessageData = 1;
        /// <summary>Time frame: opcode(1) + timestamp(u64 nanoseconds).</summary>
        public const byte Time = 2;
        /// <summary>ServiceCallResponse frame.</summary>
        public const byte ServiceCallResponse = 3;
        /// <summary>FetchAssetResponse frame.</summary>
        public const byte FetchAssetResponse = 4;
        /// <summary>PlaybackState frame.</summary>
        public const byte PlaybackState = 5;
    }

    /// <summary>Foxglove WebSocket protocol v1 binary opcodes (client → server).</summary>
    public static class ClientOpcode
    {
        /// <summary>Client MessageData frame: opcode(1) + channelId(u32) + payload.</summary>
        public const byte MessageData = 1;
        /// <summary>ServiceCallRequest frame.</summary>
        public const byte ServiceCallRequest = 2;
        /// <summary>PlaybackControlRequest frame.</summary>
        public const byte PlaybackControlRequest = 3;
    }
}
