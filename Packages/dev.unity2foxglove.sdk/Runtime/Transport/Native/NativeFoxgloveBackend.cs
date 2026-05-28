// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: Reserved placeholder for a native C FFI backend that wraps
// foxglove-c.h via P/Invoke. Not implemented — the managed WebSocket
// backend is the current default. This file exists so the interface
// contract is visible and the namespace stays self-documenting.

// Future integration point:
//   [RequiresNativePlugin("foxglove-c")]
//   internal class NativeFoxgloveBackend : IFoxgloveTransport { ... }
//
// Will wrap foxglove-c.h via P/Invoke if the pure C# path needs to be
// replaced for performance or platform reasons.

namespace Unity.FoxgloveSDK.Transport
{
    // Placeholder — see file header.
}
