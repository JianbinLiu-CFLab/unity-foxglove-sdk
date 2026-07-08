// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Profiling
// Purpose: Phase151 profiler core behavior tests.

using System;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Xunit;

namespace FoxgloveSdk.UnitTests.Profiling
{
    [Collection("ProfilerCoreTests")]
    public sealed class ProfilerCoreTests
    {
        [Fact]
        public void GlobalProfilerDefaultsToNullProfiler()
        {
            FoxgloveProfiler.ResetGlobal();

            Assert.Same(NullProfiler.Instance, FoxgloveProfiler.Global);
        }

        [Fact]
        public void GlobalProfilerRejectsNullAssignmentAndCanReset()
        {
            var profiler = new CountingProfiler();
            FoxgloveProfiler.Global = profiler;

            try
            {
                Assert.Same(profiler, FoxgloveProfiler.Global);
                Assert.Throws<ArgumentNullException>(() => FoxgloveProfiler.Global = null);
            }
            finally
            {
                FoxgloveProfiler.ResetGlobal();
            }

            Assert.Same(NullProfiler.Instance, FoxgloveProfiler.Global);
        }

        [Fact]
        public void OwnerScopedResetOnlyClearsMatchingOwner()
        {
            var ownerA = new object();
            var ownerB = new object();
            var profiler = new CountingProfiler();
            FoxgloveProfiler.SetGlobal(ownerA, profiler);

            try
            {
                FoxgloveProfiler.ResetGlobal(ownerB);
                Assert.Same(profiler, FoxgloveProfiler.Global);

                FoxgloveProfiler.ResetGlobal(ownerA);
                Assert.Same(NullProfiler.Instance, FoxgloveProfiler.Global);
            }
            finally
            {
                FoxgloveProfiler.ResetGlobal();
            }
        }

        [Fact]
        public void GlobalAssignmentClearsPreviousOwner()
        {
            var owner = new object();
            var ownerProfiler = new CountingProfiler();
            var directProfiler = new CountingProfiler();
            FoxgloveProfiler.SetGlobal(owner, ownerProfiler);

            try
            {
                FoxgloveProfiler.Global = directProfiler;
                FoxgloveProfiler.ResetGlobal(owner);

                Assert.Same(directProfiler, FoxgloveProfiler.Global);
            }
            finally
            {
                FoxgloveProfiler.ResetGlobal();
            }
        }

        [Fact]
        public void NullProfilerSamplesAreReusableNoOps()
        {
            var sample = NullProfiler.Instance.Sample("phase151.sample");

            Assert.Same(sample, NullProfiler.Instance.Sample("phase151.sample"));
            sample.Dispose();
            NullProfiler.Instance.BeginSample("phase151.begin");
            NullProfiler.Instance.EndSample();
        }

        [Fact]
        public void NullProfilerHotLoopDoesNotAllocate()
        {
            FoxgloveProfiler.ResetGlobal();
            NullProfiler.Instance.Sample("phase151.warmup").Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                using (NullProfiler.Instance.Sample("phase151.hot"))
                {
                }

                NullProfiler.Instance.BeginSample("phase151.hot");
                NullProfiler.Instance.EndSample();
            }

            var after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(before, after);
        }

        [Fact]
        public void UnityProfilerAdapterHandlesBalancedAndUnbalancedSamples()
        {
            UnityProfilerAdapter.Instance.EndSample();
            UnityProfilerAdapter.Instance.BeginSample("phase151.adapter.begin");
            UnityProfilerAdapter.Instance.BeginSample("phase151.adapter.nested");
            UnityProfilerAdapter.Instance.EndSample();
            UnityProfilerAdapter.Instance.EndSample();
            UnityProfilerAdapter.Instance.EndSample();
        }

        [Fact]
        public void UnityProfilerAdapterSampleScopesArePooledAfterDispose()
        {
            var first = UnityProfilerAdapter.Instance.Sample("phase151.adapter.scope");
            first.Dispose();

            var second = UnityProfilerAdapter.Instance.Sample("phase151.adapter.scope");
            try
            {
                Assert.Same(first, second);
            }
            finally
            {
                second.Dispose();
            }
        }

        private sealed class CountingProfiler : IFoxgloveProfiler
        {
            public IDisposable Sample(string name) => NullProfiler.Scope;

            public void BeginSample(string name)
            {
            }

            public void EndSample()
            {
            }
        }
    }

    [CollectionDefinition("ProfilerCoreTests", DisableParallelization = true)]
    public sealed class ProfilerCoreTestCollection
    {
    }
}
