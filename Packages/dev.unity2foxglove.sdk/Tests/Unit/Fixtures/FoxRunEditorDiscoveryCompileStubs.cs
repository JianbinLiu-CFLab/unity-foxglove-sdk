// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Fixtures
// Purpose: Supplies the single Editor settings seam needed when compiling the
//          production FoxRun discovery boundary in the .NET unit lane.

namespace Unity.FoxgloveSDK.Editor
{
    internal static class Unity2FoxgloveSchemaEvidenceSettings
    {
        internal static string CurrentEvidenceRoot =>
            Unity2FoxgloveSchemaEvidencePaths.DefaultCurrentEvidenceRoot;
    }
}
