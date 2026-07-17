// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Parse immutable runtime RMW and communication capabilities from runtime manifests.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal sealed class Ros2ForUnityRuntimeCommunicationMode
    {
        public Ros2ForUnityRuntimeCommunicationMode(
            string id,
            string displayName,
            string rmwImplementation,
            bool isDefault)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            RmwImplementation = rmwImplementation ?? string.Empty;
            IsDefault = isDefault;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string RmwImplementation { get; }
        public bool IsDefault { get; }
    }

    internal sealed class Ros2ForUnityRuntimeCapabilities
    {
        public Ros2ForUnityRuntimeCapabilities(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> communicationModes,
            string defaultRmwImplementation)
            : this(
                string.Empty,
                string.Empty,
                string.Empty,
                communicationModes,
                defaultRmwImplementation,
                isValid: true,
                diagnostic: string.Empty)
        {
        }

        public Ros2ForUnityRuntimeCapabilities(
            string runtimeId,
            string rosDistro,
            string platform,
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> communicationModes,
            string defaultRmwImplementation,
            bool isValid,
            string diagnostic)
        {
            var copy = communicationModes == null
                ? Array.Empty<Ros2ForUnityRuntimeCommunicationMode>()
                : Copy(communicationModes);
            CommunicationModes = new ReadOnlyCollection<Ros2ForUnityRuntimeCommunicationMode>(copy);
            RuntimeId = runtimeId ?? string.Empty;
            RosDistro = rosDistro ?? string.Empty;
            Platform = platform ?? string.Empty;
            DefaultRmwImplementation = defaultRmwImplementation ?? string.Empty;
            IsValid = isValid;
            Diagnostic = diagnostic ?? string.Empty;
            DefaultCommunicationMode = FindDefault(copy);
            SupportsZenoh = ContainsRmw(copy, Ros2ForUnityRuntimeCapabilityParser.ZenohRmwImplementation);
        }

        public IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> CommunicationModes { get; }
        public string RuntimeId { get; }
        public string RosDistro { get; }
        public string Platform { get; }
        public Ros2ForUnityRuntimeCommunicationMode DefaultCommunicationMode { get; }
        public string DefaultRmwImplementation { get; }
        public bool IsValid { get; }
        public string Diagnostic { get; }
        public bool SupportsZenoh { get; }

        public Ros2ForUnityRuntimeCommunicationMode FindMode(string id)
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

        private static Ros2ForUnityRuntimeCommunicationMode[] Copy(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> source)
        {
            var copy = new Ros2ForUnityRuntimeCommunicationMode[source.Count];
            for (var i = 0; i < source.Count; i++)
                copy[i] = source[i];
            return copy;
        }

        private static Ros2ForUnityRuntimeCommunicationMode FindDefault(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> modes)
        {
            for (var i = 0; i < modes.Count; i++)
            {
                if (modes[i].IsDefault)
                    return modes[i];
            }

            return modes.Count > 0 ? modes[0] : null;
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

    internal static class Ros2ForUnityRuntimeCapabilityParser
    {
        public const string FastDdsRmwImplementation = "rmw_fastrtps_cpp";
        public const string ZenohRmwImplementation = "rmw_zenoh_cpp";
        public const string FastDdsCommunicationMode = "fastdds";
        public const string ZenohCommunicationMode = "zenoh";

        public static Ros2ForUnityRuntimeCapabilities Parse(string manifestJson)
        {
            if (string.IsNullOrWhiteSpace(manifestJson))
                return Invalid(string.Empty, string.Empty, string.Empty, "Runtime manifest is empty.");

            try
            {
                return Parse(JObject.Parse(manifestJson));
            }
            catch
            {
                return Invalid(string.Empty, string.Empty, string.Empty, "Runtime manifest is not valid JSON.");
            }
        }

        private static Ros2ForUnityRuntimeCapabilities Parse(JObject manifest)
        {
            var legacyRmw = ReadString(manifest, "rmwImplementation");
            var explicitDefaultRmw = ReadString(manifest, "defaultRmwImplementation");
            var runtimeId = ReadString(manifest, "runtimeId");
            var rosDistro = ReadString(manifest, "rosDistro");
            var platform = ReadString(manifest, "platform");
            var explicitModesToken = manifest["communicationModes"];

            // Explicit communication modes are authoritative. Without them, two default
            // declarations must not silently choose one transport over the other.
            if (explicitModesToken == null
                && !string.IsNullOrWhiteSpace(legacyRmw)
                && !string.IsNullOrWhiteSpace(explicitDefaultRmw)
                && !string.Equals(legacyRmw, explicitDefaultRmw, StringComparison.Ordinal))
            {
                return Invalid(
                    runtimeId,
                    rosDistro,
                    platform,
                    "rmwImplementation conflicts with defaultRmwImplementation.");
            }

            var declaredDefaultRmw = string.IsNullOrWhiteSpace(explicitDefaultRmw)
                ? legacyRmw
                : explicitDefaultRmw;

            if (explicitModesToken != null)
            {
                var explicitModes = explicitModesToken as JArray;
                if (explicitModes == null || explicitModes.Count == 0)
                {
                    return Invalid(
                        runtimeId,
                        rosDistro,
                        platform,
                        "communicationModes must be a non-empty array when declared.");
                }

                if (!TryReadExplicitModes(explicitModes, out var modes, out var diagnostic))
                    return Invalid(runtimeId, rosDistro, platform, diagnostic);

                return CreateExplicitCapabilities(
                    modes,
                    declaredDefaultRmw,
                    runtimeId,
                    rosDistro,
                    platform);
            }

            var supportedRmwsToken = manifest["supportedRmwImplementations"];
            if (supportedRmwsToken != null)
            {
                var supportedRmws = supportedRmwsToken as JArray;
                if (supportedRmws == null || supportedRmws.Count == 0)
                {
                    return Invalid(
                        runtimeId,
                        rosDistro,
                        platform,
                        "supportedRmwImplementations must be a non-empty array when declared.");
                }

                if (!TryReadSupportedRmws(supportedRmws, out var implementations, out var diagnostic))
                    return Invalid(runtimeId, rosDistro, platform, diagnostic);

                if (string.IsNullOrWhiteSpace(declaredDefaultRmw))
                {
                    return Invalid(
                        runtimeId,
                        rosDistro,
                        platform,
                        "supportedRmwImplementations requires defaultRmwImplementation or legacy rmwImplementation.");
                }

                var synthesizedModes = new List<ModeInput>(implementations.Count);
                for (var i = 0; i < implementations.Count; i++)
                {
                    var rmw = implementations[i];
                    synthesizedModes.Add(new ModeInput(
                        ModeIdForRmw(rmw),
                        DisplayNameFor(rmw, string.Empty),
                        rmw,
                        isDefault: string.Equals(rmw, declaredDefaultRmw, StringComparison.Ordinal)));
                }

                var defaultIndex = FindIndexByRmw(synthesizedModes, declaredDefaultRmw);
                if (defaultIndex < 0)
                {
                    return Invalid(
                        runtimeId,
                        rosDistro,
                        platform,
                        "The declared default RMW implementation is not in supportedRmwImplementations.");
                }

                return CreateValidCapabilities(
                    synthesizedModes,
                    defaultIndex,
                    runtimeId,
                    rosDistro,
                    platform);
            }

            if (string.IsNullOrWhiteSpace(legacyRmw))
            {
                return Invalid(
                    runtimeId,
                    rosDistro,
                    platform,
                    "Runtime manifest must declare legacy rmwImplementation, supportedRmwImplementations, or communicationModes.");
            }

            return CreateValidCapabilities(
                new[]
                {
                    new ModeInput(
                        ModeIdForRmw(legacyRmw),
                        DisplayNameFor(legacyRmw, string.Empty),
                        legacyRmw,
                        isDefault: true)
                },
                selectedIndex: 0,
                runtimeId,
                rosDistro,
                platform);
        }

        private static Ros2ForUnityRuntimeCapabilities CreateExplicitCapabilities(
            IReadOnlyList<ModeInput> candidates,
            string declaredDefaultRmw,
            string runtimeId,
            string rosDistro,
            string platform)
        {
            if (candidates == null || candidates.Count == 0)
                return Invalid(runtimeId, rosDistro, platform, "communicationModes has no usable entries.");

            var selectedIndex = -1;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].IsDefault)
                    continue;

                if (selectedIndex >= 0)
                {
                    return Invalid(
                        runtimeId,
                        rosDistro,
                        platform,
                        "communicationModes declares more than one default mode.");
                }

                selectedIndex = i;
            }

            if (selectedIndex >= 0)
            {
                if (!string.IsNullOrWhiteSpace(declaredDefaultRmw)
                    && !string.Equals(
                        candidates[selectedIndex].RmwImplementation,
                        declaredDefaultRmw,
                        StringComparison.Ordinal))
                {
                    return Invalid(
                        runtimeId,
                        rosDistro,
                        platform,
                        "communicationModes default conflicts with the declared default RMW implementation.");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(declaredDefaultRmw))
                {
                    return Invalid(
                        runtimeId,
                        rosDistro,
                        platform,
                        "communicationModes requires exactly one default mode or a declared default RMW implementation.");
                }

                selectedIndex = FindIndexByRmw(candidates, declaredDefaultRmw);
                if (selectedIndex < 0)
                {
                    return Invalid(
                        runtimeId,
                        rosDistro,
                        platform,
                        "The declared default RMW implementation is not represented by communicationModes.");
                }
            }

            return CreateValidCapabilities(candidates, selectedIndex, runtimeId, rosDistro, platform);
        }

        private static Ros2ForUnityRuntimeCapabilities CreateValidCapabilities(
            IReadOnlyList<ModeInput> candidates,
            int selectedIndex,
            string runtimeId,
            string rosDistro,
            string platform)
        {
            if (candidates == null || candidates.Count == 0 || selectedIndex < 0 || selectedIndex >= candidates.Count)
                return Invalid(runtimeId, rosDistro, platform, "Runtime manifest does not select a supported communication mode.");

            var normalized = new Ros2ForUnityRuntimeCommunicationMode[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                normalized[i] = new Ros2ForUnityRuntimeCommunicationMode(
                    candidate.Id,
                    candidate.DisplayName,
                    candidate.RmwImplementation,
                    i == selectedIndex);
            }

            return new Ros2ForUnityRuntimeCapabilities(
                runtimeId,
                rosDistro,
                platform,
                normalized,
                normalized[selectedIndex].RmwImplementation,
                isValid: true,
                diagnostic: string.Empty);
        }

        private static int FindIndexByRmw(
            IReadOnlyList<ModeInput> candidates,
            string rmwImplementation)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(rmwImplementation))
                return -1;

            for (var i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i].RmwImplementation, rmwImplementation, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private static bool TryReadExplicitModes(
            JArray modes,
            out List<ModeInput> result,
            out string diagnostic)
        {
            result = new List<ModeInput>();
            diagnostic = string.Empty;
            if (modes == null)
            {
                diagnostic = "communicationModes must be an array.";
                return false;
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenRmws = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in modes)
            {
                var mode = token as JObject;
                if (mode == null)
                {
                    diagnostic = "communicationModes entries must be objects.";
                    return false;
                }

                var id = ReadString(mode, "id");
                var rmw = ReadString(mode, "rmwImplementation");
                if (string.IsNullOrWhiteSpace(id)
                    || string.IsNullOrWhiteSpace(rmw))
                {
                    diagnostic = "communicationModes entries require non-empty id and rmwImplementation values.";
                    return false;
                }

                if (!seenIds.Add(id))
                {
                    diagnostic = "communicationModes contains a duplicate id: " + id;
                    return false;
                }

                if (!seenRmws.Add(rmw))
                {
                    diagnostic = "communicationModes contains a duplicate RMW implementation: " + rmw;
                    return false;
                }

                var defaultToken = mode["default"];
                if (defaultToken != null && defaultToken.Type != JTokenType.Boolean)
                {
                    diagnostic = "communicationModes default values must be booleans.";
                    return false;
                }

                result.Add(new ModeInput(
                    id,
                    DisplayNameFor(rmw, ReadString(mode, "displayName")),
                    rmw,
                    defaultToken != null && defaultToken.Value<bool>()));
            }

            return true;
        }

        private static bool TryReadSupportedRmws(
            JArray supportedRmws,
            out List<string> result,
            out string diagnostic)
        {
            result = new List<string>();
            diagnostic = string.Empty;
            if (supportedRmws == null)
            {
                diagnostic = "supportedRmwImplementations must be an array.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in supportedRmws)
            {
                if (token == null || token.Type != JTokenType.String)
                {
                    diagnostic = "supportedRmwImplementations entries must be strings.";
                    return false;
                }

                var rmw = token.Value<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(rmw))
                {
                    diagnostic = "supportedRmwImplementations entries must be non-empty strings.";
                    return false;
                }

                if (!seen.Add(rmw))
                {
                    diagnostic = "supportedRmwImplementations contains a duplicate RMW implementation: " + rmw;
                    return false;
                }

                result.Add(rmw);
            }

            return true;
        }

        private static string ReadString(JObject manifest, string propertyName)
            => manifest?.Value<string>(propertyName)?.Trim() ?? string.Empty;

        private static string ModeIdForRmw(string rmwImplementation)
        {
            if (string.Equals(rmwImplementation, FastDdsRmwImplementation, StringComparison.Ordinal))
                return FastDdsCommunicationMode;
            if (string.Equals(rmwImplementation, ZenohRmwImplementation, StringComparison.Ordinal))
                return ZenohCommunicationMode;
            return rmwImplementation ?? string.Empty;
        }

        private static string DisplayNameFor(string rmwImplementation, string manifestDisplayName)
        {
            if (!string.IsNullOrWhiteSpace(manifestDisplayName))
                return manifestDisplayName;
            if (string.Equals(rmwImplementation, FastDdsRmwImplementation, StringComparison.Ordinal))
                return "FastDDS (default)";
            if (string.Equals(rmwImplementation, ZenohRmwImplementation, StringComparison.Ordinal))
                return "Zenoh (rmw_zenoh_cpp)";
            return rmwImplementation ?? string.Empty;
        }

        private static Ros2ForUnityRuntimeCapabilities Invalid(
            string runtimeId,
            string rosDistro,
            string platform,
            string diagnostic)
            => new Ros2ForUnityRuntimeCapabilities(
                runtimeId,
                rosDistro,
                platform,
                Array.Empty<Ros2ForUnityRuntimeCommunicationMode>(),
                string.Empty,
                isValid: false,
                diagnostic: diagnostic);

        private sealed class ModeInput
        {
            public ModeInput(string id, string displayName, string rmwImplementation, bool isDefault)
            {
                Id = id;
                DisplayName = displayName;
                RmwImplementation = rmwImplementation;
                IsDefault = isDefault;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public string RmwImplementation { get; }
            public bool IsDefault { get; }
        }
    }
}
