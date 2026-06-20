// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Zero-allocation no-op profiler implementation.

using System;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// No-op profiler used when optional instrumentation is disabled.
    /// </summary>
    public sealed class NullProfiler : IFoxgloveProfiler
    {
        public static readonly NullProfiler Instance = new();
        public static readonly IDisposable Scope = new NullProfilerScope();

        private NullProfiler()
        {
        }

        public IDisposable Sample(string name) => Scope;

        public void BeginSample(string name)
        {
        }

        public void EndSample()
        {
        }

        private sealed class NullProfilerScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
