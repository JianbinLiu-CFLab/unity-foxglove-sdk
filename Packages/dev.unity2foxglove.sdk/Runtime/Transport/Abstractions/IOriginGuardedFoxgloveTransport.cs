// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: Optional transport extension for browser Origin allowlist
// management shared by plain and secure managed WebSocket backends.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Optional transport extension for Cross-Site WebSocket Hijacking
    /// protection. Implementations reject browser clients with non-allowlisted
    /// Origin headers while keeping non-browser no-Origin clients compatible.
    /// </summary>
    public interface IOriginGuardedFoxgloveTransport
    {
        /// <summary>Snapshot of allowed browser origins. Empty means browser-origin clients are rejected.</summary>
        IReadOnlyCollection<string> AllowedOrigins { get; }

        /// <summary>Add one browser origin to the allowlist.</summary>
        void AddAllowedOrigin(string origin);

        /// <summary>Clear the browser origin allowlist.</summary>
        void ClearAllowedOrigins();
    }
}
