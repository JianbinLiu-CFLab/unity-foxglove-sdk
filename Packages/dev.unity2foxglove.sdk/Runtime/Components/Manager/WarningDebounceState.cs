// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Runtime-only warning debounce state for FoxgloveManager.

namespace Unity.FoxgloveSDK.Components
{
    internal sealed class WarningDebounceState
    {
        internal bool WarnedNotRunning;
        internal string LastInvalidPublishTopicWarningKey;
        internal long LastClientEventOverflowWarningTicks;

        internal void ResetNotRunning()
        {
            WarnedNotRunning = false;
        }
    }
}
