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
    internal sealed class Ros2ForUnityRuntimeDescriptor
    {
        public Ros2ForUnityRuntimeDescriptor(
            string displayName,
            string packageName,
            string runtimeId,
            string rosDistro,
            string platform,
            Ros2ForUnityRuntimeCapabilities capabilities,
            string zenohPayloadDiagnostic)
        {
            DisplayName = displayName ?? string.Empty;
            PackageName = packageName ?? string.Empty;
            RuntimeId = runtimeId ?? string.Empty;
            RosDistro = rosDistro ?? string.Empty;
            Platform = platform ?? string.Empty;
            ZenohPayloadDiagnostic = zenohPayloadDiagnostic ?? string.Empty;
            Capabilities = capabilities ?? Ros2ForUnityRuntimeCapabilityParser.Parse(string.Empty);
            CommunicationModes = BuildAvailableCommunicationModes(Capabilities.CommunicationModes, ZenohPayloadDiagnostic);
            DefaultCommunicationMode = SelectDefaultCommunicationMode(CommunicationModes);
            CommunicationModeIds = BuildCommunicationModeIds(CommunicationModes);
            CommunicationModeLabels = BuildCommunicationModeLabels(CommunicationModes);
            SupportsZenoh = ContainsRmw(CommunicationModes, Ros2ForUnityRuntimeSelection.ZenohRmwImplementation);
        }

        public string DisplayName { get; }
        public string PackageName { get; }
        public string RuntimeId { get; }
        public string RosDistro { get; }
        public string Platform { get; }
        public Ros2ForUnityRuntimeCapabilities Capabilities { get; }
        public IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> CommunicationModes { get; }
        public Ros2ForUnityRuntimeCommunicationMode DefaultCommunicationMode { get; }
        public IReadOnlyList<string> CommunicationModeIds { get; }
        public string[] CommunicationModeLabels { get; }
        public bool SupportsZenoh { get; }
        public string ZenohPayloadDiagnostic { get; }

        public Ros2ForUnityRuntimeCommunicationMode FindCommunicationMode(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            for (var i = 0; i < CommunicationModes.Count; i++)
            {
                var mode = CommunicationModes[i];
                if (string.Equals(mode.Id, id, StringComparison.Ordinal))
                    return mode;
            }

            return null;
        }

        private static IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> BuildAvailableCommunicationModes(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> capabilities,
            string zenohPayloadDiagnostic)
        {
            if (capabilities == null || capabilities.Count == 0)
                return Array.Empty<Ros2ForUnityRuntimeCommunicationMode>();

            var suppressZenoh = !string.IsNullOrWhiteSpace(zenohPayloadDiagnostic);
            var available = new List<Ros2ForUnityRuntimeCommunicationMode>(capabilities.Count);
            for (var i = 0; i < capabilities.Count; i++)
            {
                var mode = capabilities[i];
                if (suppressZenoh
                    && string.Equals(
                        mode.RmwImplementation,
                        Ros2ForUnityRuntimeSelection.ZenohRmwImplementation,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                available.Add(mode);
            }

            return available.ToArray();
        }

        private static Ros2ForUnityRuntimeCommunicationMode SelectDefaultCommunicationMode(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> modes)
        {
            if (modes == null || modes.Count == 0)
                return null;

            for (var i = 0; i < modes.Count; i++)
            {
                if (modes[i].IsDefault)
                    return modes[i];
            }

            return modes[0];
        }

        private static IReadOnlyList<string> BuildCommunicationModeIds(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> modes)
        {
            var ids = new string[modes?.Count ?? 0];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = modes[i].Id;
            return ids;
        }

        private static string[] BuildCommunicationModeLabels(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> modes)
        {
            var labels = new string[modes?.Count ?? 0];
            for (var i = 0; i < labels.Length; i++)
                labels[i] = modes[i].DisplayName;
            return labels;
        }

        private static bool ContainsRmw(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> modes,
            string rmwImplementation)
        {
            for (var i = 0; i < modes.Count; i++)
            {
                if (string.Equals(modes[i].RmwImplementation, rmwImplementation, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

}
#endif
