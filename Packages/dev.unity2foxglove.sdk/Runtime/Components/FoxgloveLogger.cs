// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components
// Purpose: IFoxgloveLogger implementation that routes to Unity Debug.LogWarning and Debug.LogError, prefixed with [Foxglove].

using UnityEngine;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// IFoxgloveLogger implementation that routes to Unity's Debug.LogWarning/Debug.LogError.
    /// </summary>
    public class UnityLogger : IFoxgloveLogger
    {
        public void LogWarning(string message) => Debug.LogWarning($"[Foxglove] {message}");
        public void LogError(string message) => Debug.LogError($"[Foxglove] {message}");
    }
}
