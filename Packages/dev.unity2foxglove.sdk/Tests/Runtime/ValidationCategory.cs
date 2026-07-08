// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Classifies validation phases by whether they are safe for default CI.

namespace Unity.FoxgloveSDK.Tests
{
    internal enum ValidationCategory
    {
        /// <summary>No Unity Editor, network, hardware, or machine-local files required.</summary>
        CiSafe,
        /// <summary>Requires local artifacts, installed tools, generated files, or Editor/player evidence.</summary>
        LocalEvidence,
        /// <summary>Requires human observation, interactive Unity/Foxglove use, hardware, or external apps.</summary>
        ManualSmoke
    }
}
