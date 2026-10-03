// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Invalidate reflection discovery snapshots at Editor compilation boundaries.

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;

namespace Unity.FoxgloveSDK.Editor
{
    [InitializeOnLoad]
    internal static class FoxrunAssemblyScannerLifecycle
    {
        static FoxrunAssemblyScannerLifecycle()
        {
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnCompilationStarted(object _)
            => FoxrunCodeGenerator.InvalidateReflectionDiscoveryCache();

        private static void OnBeforeAssemblyReload()
            => FoxrunCodeGenerator.InvalidateReflectionDiscoveryCache();
    }
}
#endif
