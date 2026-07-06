// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.FoxgloveSDK.RemoteGateway
{
    internal static class RemoteGatewayLifecycleGate
    {
        private static bool _applicationQuitting;
        private static bool _assemblyReloading;

        static RemoteGatewayLifecycleGate()
        {
            Application.quitting += OnApplicationQuitting;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif
        }

        internal static bool CanStartNativeGateway()
        {
            if (_applicationQuitting || _assemblyReloading)
                return false;

#if UNITY_EDITOR
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return false;
#endif

            return Application.isPlaying;
        }

        internal static bool CanStopNativeGateway()
        {
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            _applicationQuitting = false;
            _assemblyReloading = false;
        }

        private static void OnApplicationQuitting()
        {
            _applicationQuitting = true;
        }

#if UNITY_EDITOR
        private static void OnBeforeAssemblyReload()
        {
            _assemblyReloading = true;
        }
#endif
    }
}
