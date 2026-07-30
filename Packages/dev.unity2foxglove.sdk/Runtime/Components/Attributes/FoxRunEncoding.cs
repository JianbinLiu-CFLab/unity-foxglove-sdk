// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Public FoxRun encoding vocabulary for Foxglove directions.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Selects the wire encoding used by Foxglove publish and subscribe
    /// directions on the built-in WebSocket transport.
    /// </summary>
    public enum FoxRunEncoding
    {
        /// <summary>Use a generated Protobuf payload and descriptor.</summary>
        Protobuf = 1,

        /// <summary>Use the generated JSON payload contract.</summary>
        JSON = 2,

        /// <summary>Use the generated schemaless MessagePack payload contract.</summary>
        MessagePack = 3
    }
}
