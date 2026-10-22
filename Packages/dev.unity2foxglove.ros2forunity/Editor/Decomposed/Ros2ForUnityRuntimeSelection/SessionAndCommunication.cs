// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Project-level active runtime selection for optional R2FU runtime packages.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal static partial class Ros2ForUnityRuntimeSelection
    {
        public static string GetSessionRuntimePackage()
            => SessionState.GetString(SessionRuntimeKey, string.Empty);

        public static string GetSessionCommunicationMode()
            => SessionState.GetString(SessionCommunicationModeKey, string.Empty);

        public static string GetSessionZenohRouterEndpoint()
            => SessionState.GetString(SessionZenohRouterEndpointKey, string.Empty);

        public static string GetSessionCustomTypesupportIdentity()
            => SessionState.GetString(SessionCustomTypesupportIdentityKey, string.Empty);

        public static Ros2ForUnityCustomTypesupportSelectionResult GetActiveCustomTypesupportSelection(
            string projectDirectory)
        {
            var status = GetStatus(projectDirectory);
            return status?.SelectedRuntime == null
                ? new Ros2ForUnityCustomTypesupportSelectionResult(
                    Ros2ForUnityCustomTypesupportSelectionCode.InvalidBaseRuntime,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty)
                : Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                    projectDirectory,
                    status.SelectedRuntime.PackageName);
        }

        public static IReadOnlyList<string> GetCommunicationModeIds(Ros2ForUnityRuntimeDescriptor runtime)
            => runtime?.CommunicationModeIds ?? NoCommunicationModeIds;

        public static string[] GetCommunicationModeLabels(Ros2ForUnityRuntimeDescriptor runtime)
            => runtime?.CommunicationModeLabels ?? NoCommunicationModeLabels;

        public static string GetCommunicationModeDisplayName(
            Ros2ForUnityRuntimeDescriptor runtime,
            string mode)
        {
            var resolved = runtime?.FindCommunicationMode(mode);
            return resolved?.DisplayName ?? mode ?? string.Empty;
        }

        public static string GetRmwImplementationForCommunicationMode(
            Ros2ForUnityRuntimeDescriptor runtime,
            string mode)
        {
            var resolved = runtime?.FindCommunicationMode(mode) ?? runtime?.DefaultCommunicationMode;
            return resolved?.RmwImplementation ?? string.Empty;
        }

        public static string GetCommunicationModeForRuntime(Ros2ForUnityRuntimeDescriptor runtime)
        {
            if (runtime == null)
                return string.Empty;

            var saved = EditorUserSettings.GetConfigValue(GetCommunicationModeSettingsKey(runtime));
            if (string.IsNullOrWhiteSpace(saved))
                saved = EditorUserSettings.GetConfigValue(CommunicationModeEditorUserSettingsKey);
            return runtime.FindCommunicationMode(saved)?.Id
                   ?? runtime.DefaultCommunicationMode?.Id
                   ?? string.Empty;
        }

        public static void SetCommunicationMode(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            string mode)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Cannot switch ROS2 For Unity communication mode while Play Mode is active or changing.");

            if (runtime == null)
                throw new InvalidOperationException("Select an active ROS2 For Unity runtime before changing communication mode.");

            if (runtime.FindCommunicationMode(mode) == null)
                throw new InvalidOperationException("Communication mode is not supported by the active runtime: " + mode);

            EditorUserSettings.SetConfigValue(GetCommunicationModeSettingsKey(runtime), mode);
            ApplyCommunicationModeEnvironment(projectDirectory);
        }

        public static void ApplyCommunicationModeEnvironment(string projectDirectory)
        {
            var status = GetStatus(projectDirectory);
            if (status.SelectedRuntime == null)
            {
                RestoreProcessEnvironment();
                return;
            }

            var mode = GetCommunicationModeForRuntime(status.SelectedRuntime);
            ApplySelectedRuntimeEnvironment(projectDirectory, status.SelectedRuntime, mode);
        }

        internal static bool RestoreProcessEnvironment()
            => Ros2ForUnityEditorEnvironmentLease.Restore();

        public static void BindActiveRuntimeForPlayMode(string projectDirectory)
        {
            BindActiveRuntimeForPlayMode(GetStatus(projectDirectory));
        }

        public static void BindActiveRuntimeForPlayMode(Ros2ForUnityRuntimeSelectionStatus status)
        {
            if (status == null || status.SelectedRuntime == null)
                return;

            var projectDirectory = ProjectDirectoryFromApplication();
            var customTypesupport = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                projectDirectory,
                status.SelectedRuntime.PackageName);
            ThrowIfCustomTypesupportTransactionFailed(customTypesupport, "binding ROS2 For Unity for Play Mode");

            var communicationMode = GetCommunicationModeForRuntime(status.SelectedRuntime);
            ApplySelectedRuntimeEnvironment(projectDirectory, status.SelectedRuntime, communicationMode);

            if (string.IsNullOrWhiteSpace(GetSessionRuntimePackage()))
                SessionState.SetString(SessionRuntimeKey, status.SelectedRuntime.PackageName);

            if (string.IsNullOrWhiteSpace(GetSessionCommunicationMode()))
                SessionState.SetString(SessionCommunicationModeKey, communicationMode);

            if (string.IsNullOrWhiteSpace(GetSessionZenohRouterEndpoint())
                && string.Equals(
                    GetRmwImplementationForCommunicationMode(status.SelectedRuntime, communicationMode),
                    ZenohRmwImplementation,
                    StringComparison.Ordinal))
            {
                SessionState.SetString(
                    SessionZenohRouterEndpointKey,
                    Ros2ForUnityZenohRouterSettings.Get(status.SelectedRuntime).Endpoint);
            }

            if (string.IsNullOrWhiteSpace(GetSessionCustomTypesupportIdentity()))
                SessionState.SetString(SessionCustomTypesupportIdentityKey, BuildCustomTypesupportIdentity(customTypesupport));
        }

    }
}
#endif
