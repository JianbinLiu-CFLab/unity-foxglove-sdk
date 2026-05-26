// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Abstract logger bridge for protocol errors and warnings.
// ConsoleLogger writes to Console.Error; UnityLogger redirects to Debug.Log.

using System;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Abstract logger bridge for protocol errors and warnings.
    /// </summary>
    public interface IFoxgloveLogger
    {
        void LogWarning(string message);
        void LogError(string message);
    }

    /// <summary>Default logger that writes to Console.Error.</summary>
    public class ConsoleLogger : IFoxgloveLogger
    {
        public void LogWarning(string message) => Console.Error.WriteLine($"[Foxglove][Warning] {message}");
        public void LogError(string message) => Console.Error.WriteLine($"[Foxglove][Error] {message}");
    }
}
