// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Declared FoxRun topic wire-encoding policy.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Declared wire-encoding policy for a FoxRun topic.
    /// <see cref="Inherit"/> is valid on source attributes and resolves through
    /// the FoxgloveManager default when the session is registered.
    /// </summary>
    public enum FoxRunWireEncoding
    {
        /// <summary>Resolve through the FoxgloveManager default.</summary>
        Inherit = 0,

        /// <summary>Use a generated Protobuf payload and descriptor.</summary>
        Protobuf = 1,

        /// <summary>Use the existing JSON payload contract.</summary>
        Json = 2
    }
}
