// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Closed overflow vocabulary for bounded FoxRun input streams.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Determines which owned sample is discarded when a stream is full.</summary>
    public enum FoxRunStreamOverflowPolicy
    {
        DropOldest = 1,
        DropNewest = 2
    }
}
