// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Pure topic validation and normalization helpers for
// FoxgloveManager.

using System;

namespace Unity.FoxgloveSDK.Components
{
    internal static class TopicNameNormalizer
    {
        internal static bool IsValidPublishTopic(string topic)
            => !string.IsNullOrWhiteSpace(topic);

        internal static string NormalizeRosStyleTopic(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
                return string.Empty;

            var normalized = CollapseSlashes(topic.Trim());
            if (!normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = "/" + normalized;
            }

            if (normalized.Length > 1)
            {
                normalized = normalized.TrimEnd('/');
            }

            return normalized == "/" ? string.Empty : normalized;
        }

        private static string CollapseSlashes(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

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
    }
}
