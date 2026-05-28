// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Shared schema identity policy enums for recording and replay.

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Selects whether a component follows project-level schema identity policy
    /// or uses its own Inspector override.
    /// </summary>
    public enum SchemaIdentityModeSource
    {
        /// <summary>Use the project-level default schema identity mode.</summary>
        ProjectSettings = 0,

        /// <summary>Use the component-local override value.</summary>
        Override = 1
    }

    /// <summary>
    /// Controls how strictly recorded schema evidence is compared with the
    /// current runtime schema identity.
    /// </summary>
    public enum SchemaIdentityMode
    {
        /// <summary>Do not enforce or warn on schema identity differences.</summary>
        Off = 0,

        /// <summary>Warn on schema identity differences but continue operation.</summary>
        Warn = 1,

        /// <summary>Block operations that require complete, matching schema identity evidence.</summary>
        Strict = 2
    }
}
