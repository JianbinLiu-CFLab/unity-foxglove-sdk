// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Classifies validation phases by whether they are safe for default CI.

namespace Unity.FoxgloveSDK.Tests
{
    internal enum ValidationCategory
    {
        CiSafe,
        LocalEvidence,
        ManualSmoke,
        OptionalTooling
    }
}
