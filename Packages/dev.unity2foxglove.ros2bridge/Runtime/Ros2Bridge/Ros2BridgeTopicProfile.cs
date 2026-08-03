// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Topic normalization helpers for the optional ROS2 Bridge output.

using System;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
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
            if (ContainsNewline(candidate))
            {
                error = "ROS2 Bridge namespace must not contain newline characters.";
                return false;
            }
            if (!candidate.StartsWith("/", StringComparison.Ordinal))
            {
                error = "ROS2 Bridge namespace must be empty or start with '/'.";
                return false;
            }

            if (candidate.Length > 1)
                candidate = candidate.TrimEnd('/');

            if (candidate != "/" && !IsValidRos2TopicName(candidate))
            {
                error = "ROS2 Bridge namespace contains invalid ROS 2 topic characters.";
                return false;
            }

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
            if (ContainsNewline(candidate))
            {
                error = "ROS2 Bridge topic override must not contain newline characters.";
                return false;
            }
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
            if (!IsValidRos2TopicName(candidate))
            {
                error = "ROS2 Bridge topic override contains invalid ROS 2 topic characters.";
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
            if (ContainsNewline(normalizedPublisherTopic))
            {
                error = "ROS2 Bridge publisher topic must not contain newline characters.";
                return false;
            }
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
            if (!IsValidRos2TopicName(normalizedPublisherTopic))
            {
                error = "ROS2 Bridge publisher topic contains invalid ROS 2 topic characters.";
                return false;
            }

            effectiveTopic = string.IsNullOrEmpty(normalizedNamespace)
                ? normalizedPublisherTopic
                : CollapseSlashes(normalizedNamespace + "/" + normalizedPublisherTopic.TrimStart('/'));
            if (!IsValidRos2TopicName(effectiveTopic))
            {
                effectiveTopic = string.Empty;
                error = "ROS2 Bridge effective topic must not exceed 255 characters.";
                return false;
            }
            return true;
        }

        public static bool IsValidRos2TopicName(string value)
        {
            if (string.IsNullOrEmpty(value)
                || value.Length > U2R2ProtocolLimits.MaximumRosTopicNameLength
                || value[0] != '/')
                return false;

            var tokenHasCharacters = false;
            var tokenStart = true;
            for (var i = 1; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch == '/')
                {
                    if (!tokenHasCharacters)
                        return false;
                    tokenHasCharacters = false;
                    tokenStart = true;
                    continue;
                }

                if (!IsRos2TopicTokenCharacter(ch))
                    return false;
                if (tokenStart && !IsRos2TopicTokenStartCharacter(ch))
                    return false;
                tokenHasCharacters = true;
                tokenStart = false;
            }

            return tokenHasCharacters;
        }

        private static string CollapseSlashes(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (!ContainsConsecutiveSlashes(value))
                return value;

            var chars = new char[value.Length];
            var write = 0;
            var lastWasSlash = false;
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch == '/')
                {
                    if (lastWasSlash)
                        continue;
                    lastWasSlash = true;
                }
                else
                {
                    lastWasSlash = false;
                }

                chars[write++] = ch;
            }

            return write == value.Length ? value : new string(chars, 0, write);
        }

        private static bool ContainsConsecutiveSlashes(string value)
        {
            for (var i = 1; i < value.Length; i++)
            {
                if (value[i] == '/' && value[i - 1] == '/')
                    return true;
            }

            return false;
        }

        private static bool ContainsNewline(string value)
            => value != null && (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0);

        private static bool IsRos2TopicTokenCharacter(char ch)
            => ch == '_' || (ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');

        private static bool IsRos2TopicTokenStartCharacter(char ch)
            => ch == '_' || (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
    }
}
