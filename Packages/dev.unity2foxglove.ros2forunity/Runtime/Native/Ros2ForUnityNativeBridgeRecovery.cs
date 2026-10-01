// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Shared transient runtime-loss handling for native product bridges.

using System;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal static class Ros2ForUnityNativeBridgeRecovery
    {
        internal static void ResetAfterRuntimeLoss(
            ref bool runtimeWasReady,
            Action clearBindings)
        {
            if (!runtimeWasReady)
                return;

            clearBindings();
            Ros2ForUnityNativeRuntimeIdentity.ResetForRuntimeLoss();
            runtimeWasReady = false;
        }
    }
}
