// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 151 validation for profiler infrastructure boundaries.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase151Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 151 Tests ---");
            _passCount = 0;

            VerifyUnityNeutralProfilerCore();
            VerifyUnityProfilerAdapterShape();
            VerifyManagerProfilerLifecycleShape();
            VerifyProfilerUnitCoverage();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 151: " + _passCount + " checks passed.\n");
        }

        private static void VerifyUnityNeutralProfilerCore()
        {
            var abstraction = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Abstractions/IFoxgloveProfiler.cs");
            var nullProfiler = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Profiling/NullProfiler.cs");
            var global = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Profiling/FoxgloveProfiler.cs");

            Check(abstraction.Contains("public interface IFoxgloveProfiler", StringComparison.Ordinal)
                  && abstraction.Contains("IDisposable Sample(string name)", StringComparison.Ordinal)
                  && abstraction.Contains("void BeginSample(string name)", StringComparison.Ordinal)
                  && abstraction.Contains("void EndSample()", StringComparison.Ordinal)
                  && !abstraction.Contains("UnityEngine", StringComparison.Ordinal)
                  && !abstraction.Contains("Unity.Profiling", StringComparison.Ordinal),
                "151-1: profiler abstraction is Unity-neutral");

            Check(nullProfiler.Contains("public sealed class NullProfiler : IFoxgloveProfiler", StringComparison.Ordinal)
                  && nullProfiler.Contains("public static readonly NullProfiler Instance", StringComparison.Ordinal)
                  && nullProfiler.Contains("public static readonly IDisposable Scope", StringComparison.Ordinal)
                  && nullProfiler.Contains("public IDisposable Sample(string name) => Scope", StringComparison.Ordinal)
                  && !nullProfiler.Contains("public IDisposable Sample(string name) => new", StringComparison.Ordinal),
                "151-2: NullProfiler returns a reusable no-op scope");

            Check(global.Contains("public static class FoxgloveProfiler", StringComparison.Ordinal)
                  && global.Contains("private static volatile IFoxgloveProfiler _global = NullProfiler.Instance", StringComparison.Ordinal)
                  && global.Contains("throw new ArgumentNullException", StringComparison.Ordinal)
                  && global.Contains("SetGlobal(object owner, IFoxgloveProfiler profiler)", StringComparison.Ordinal)
                  && global.Contains("ResetGlobal(object owner)", StringComparison.Ordinal)
                  && global.Contains("ResetGlobal()", StringComparison.Ordinal),
                "151-3: global profiler defaults to NullProfiler and supports owner-scoped resets");
        }

        private static void VerifyUnityProfilerAdapterShape()
        {
            var adapter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Profiling/UnityProfilerAdapter.cs");

            Check(adapter.Contains("public sealed class UnityProfilerAdapter : IFoxgloveProfiler", StringComparison.Ordinal)
                  && adapter.Contains("Unity.Profiling", StringComparison.Ordinal)
                  && adapter.Contains("ProfilerMarker", StringComparison.Ordinal)
                  && adapter.Contains("ConcurrentDictionary<string, ProfilerMarker>", StringComparison.Ordinal)
                  && adapter.Contains("ConcurrentBag<ProfilerScope>", StringComparison.Ordinal)
                  && adapter.Contains("public IDisposable Sample(string name)", StringComparison.Ordinal)
                  && adapter.Contains("public void BeginSample(string name)", StringComparison.Ordinal)
                  && adapter.Contains("public void EndSample()", StringComparison.Ordinal),
                "151-4: Unity profiler adapter maps IFoxgloveProfiler to pooled ProfilerMarker scopes");
        }

        private static void VerifyManagerProfilerLifecycleShape()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var diagnosticsEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Diagnostics.cs");

            Check(manager.Contains("_profilingEnabled", StringComparison.Ordinal)
                  && manager.Contains("ConfigureProfiler()", StringComparison.Ordinal)
                  && manager.Contains("FoxgloveProfiler.SetGlobal(this, UnityProfilerAdapter.Instance)", StringComparison.Ordinal)
                  && manager.Contains("FoxgloveProfiler.ResetGlobal(this)", StringComparison.Ordinal)
                  && manager.Contains("OnDisable()", StringComparison.Ordinal)
                  && manager.Contains("OnDestroy()", StringComparison.Ordinal)
                  && diagnosticsEditor.Contains("DrawProfilerDiagnostics()", StringComparison.Ordinal)
                  && diagnosticsEditor.Contains("DrawProperty(\"_profilingEnabled\", \"Unity Profiler Markers\")", StringComparison.Ordinal),
                "151-5: FoxgloveManager exposes profiling toggle, custom Inspector UI, and owner-scoped lifecycle hook");
        }

        private static void VerifyProfilerUnitCoverage()
        {
            var unitTest = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/Profiling/ProfilerCoreTests.cs");

            Check(unitTest.Contains("GlobalProfilerDefaultsToNullProfiler", StringComparison.Ordinal)
                  && unitTest.Contains("NullProfilerHotLoopDoesNotAllocate", StringComparison.Ordinal)
                  && unitTest.Contains("OwnerScopedResetOnlyClearsMatchingOwner", StringComparison.Ordinal)
                  && unitTest.Contains("UnityProfilerAdapterSampleScopesArePooledAfterDispose", StringComparison.Ordinal),
                "151-6: unit tests cover profiler defaults, owner resets, and adapter scope pooling");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase151"),
                "151-7: validation registry exposes the profiler infrastructure flag");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
