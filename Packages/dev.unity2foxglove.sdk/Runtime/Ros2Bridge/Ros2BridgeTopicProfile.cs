// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Topic normalization helpers for the optional ROS2 Bridge output.

using System;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Pure helpers for resolving ROS2 Bridge topic namespaces and overrides.</summary>
    public static class Ros2BridgeTopicProfile
    {
        /// <summary>Normalize an optional manager namespace.</summary>
        public static bool TryNormalizeRos2BridgeNamespace(string value, out string normalized, out string error)
        {
            normalized = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
                return true;

            var candidate = CollapseSlashes(value.Trim());
            if (!candidate.StartsWith("/", StringComparison.Ordinal))
            {
                error = "ROS2 Bridge namespace must be empty or start with '/'.";
                return false;
            }

            if (candidate.Length > 1)
                candidate = candidate.TrimEnd('/');

            normalized = candidate == "/" ? string.Empty : candidate;
            return true;
        }

        /// <summary>Normalize an optional absolute publisher topic override.</summary>
        public static bool TryNormalizeRos2BridgeTopic(string value, out string normalized, out string error)
        {
            normalized = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
                return true;

            var candidate = CollapseSlashes(value.Trim());
            if (!candidate.StartsWith("/", StringComparison.Ordinal))
            {
                error = "ROS2 Bridge topic override must be empty or start with '/'.";
                return false;
            }

            if (candidate.Length > 1)
                candidate = candidate.TrimEnd('/');

            if (candidate.Length == 0 || candidate == "/")
            {
                error = "ROS2 Bridge topic override must resolve to a concrete topic.";
                return false;
            }

            normalized = candidate;
            return true;
        }

        /// <summary>Resolve a final bridge topic without mutating the WebSocket publisher topic.</summary>
        public static bool TryResolveRos2BridgeTopic(
            string bridgeNamespace,
            string publisherTopic,
            string overrideTopic,
            out string effectiveTopic,
            out string error)
        {
            effectiveTopic = string.Empty;
            error = string.Empty;

            if (!TryNormalizeRos2BridgeNamespace(bridgeNamespace, out var normalizedNamespace, out error))
                return false;

            if (!TryNormalizeRos2BridgeTopic(overrideTopic, out var normalizedOverride, out error))
                return false;

            if (!string.IsNullOrEmpty(normalizedOverride))
            {
                effectiveTopic = normalizedOverride;
                return true;
            }

            if (string.IsNullOrWhiteSpace(publisherTopic))
            {
                error = "ROS2 Bridge publisher topic is required.";
                return false;
            }

            var normalizedPublisherTopic = CollapseSlashes(publisherTopic.Trim());
            if (!normalizedPublisherTopic.StartsWith("/", StringComparison.Ordinal))
            {
                error = "ROS2 Bridge publisher topic must start with '/'.";
                return false;
            }

            if (normalizedPublisherTopic.Length > 1)
                normalizedPublisherTopic = normalizedPublisherTopic.TrimEnd('/');
            if (normalizedPublisherTopic == "/")
            {
                error = "ROS2 Bridge publisher topic must resolve to a concrete topic.";
                return false;
            }

            effectiveTopic = string.IsNullOrEmpty(normalizedNamespace)
                ? normalizedPublisherTopic
                : CollapseSlashes(normalizedNamespace + "/" + normalizedPublisherTopic.TrimStart('/'));
            return !string.IsNullOrEmpty(effectiveTopic);
        }

        private static string CollapseSlashes(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            while (value.Contains("//"))
                value = value.Replace("//", "/");
            return value;
        }
    }
}
