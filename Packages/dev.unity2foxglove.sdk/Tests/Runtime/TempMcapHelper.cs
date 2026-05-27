// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared helper for temporary MCAP file creation with automatic cleanup.

using System;
using System.Collections.Generic;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Tracks temporary MCAP files created during validation and supports bulk cleanup.
    /// </summary>
    internal static class TempMcapHelper
    {
        private static readonly List<string> _paths = new();

        /// <summary>
        /// Creates a temporary .mcap file path under <c>Path.GetTempPath()</c>
        /// and registers it for later cleanup.
        /// </summary>
        public static string CreatePath(string label)
        {
            var path = Path.Combine(Path.GetTempPath(), label + "_" + Guid.NewGuid().ToString("N") + ".mcap");
            lock (_paths) { _paths.Add(path); }
            return path;
        }

        /// <summary>
        /// Deletes all registered temporary MCAP files. Best-effort; does not throw.
        /// </summary>
        public static void Cleanup()
        {
            lock (_paths)
            {
                foreach (var path in _paths)
                {
                    try { File.Delete(path); } catch { /* best effort */ }
                }

                _paths.Clear();
            }
        }
    }
}
