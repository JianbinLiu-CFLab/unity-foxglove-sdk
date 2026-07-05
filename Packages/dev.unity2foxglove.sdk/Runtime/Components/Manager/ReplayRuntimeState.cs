// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Runtime-only replay state for FoxgloveManager.

namespace Unity.FoxgloveSDK.Components
{
    internal sealed class ReplayRuntimeState
    {
        internal string CachedReplayFilePathInput;
        internal string CachedResolvedReplayFilePath;
        internal bool LivePublishersDisabled;

        internal void InvalidateResolvedReplayFilePathCache()
        {
            CachedReplayFilePathInput = null;
            CachedResolvedReplayFilePath = null;
        }
    }
}
