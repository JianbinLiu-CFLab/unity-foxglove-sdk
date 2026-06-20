// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Unity-neutral profiler abstraction for optional runtime instrumentation.

using System;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Unity-neutral profiler bridge for optional coarse-grained runtime instrumentation.
    /// </summary>
    /// <remarks>
    /// Sample names must come from a bounded static set. Do not include per-frame
    /// ids, timestamps, object names, or other unbounded values in sample names.
    /// Prefer <see cref="BeginSample"/> and <see cref="EndSample"/> for hot loops.
    /// </remarks>
    public interface IFoxgloveProfiler
    {
        IDisposable Sample(string name);
        void BeginSample(string name);
        void EndSample();
    }
}
