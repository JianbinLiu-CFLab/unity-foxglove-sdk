// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Stable ROS2-safe naming for generated custom interface fields.

using System;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunRos2CustomNamingPolicy
    {
        public const string FrameworkPrefix = "foxrun_";

        public static string ToRosFieldName(string value)
        {
            value = value ?? string.Empty;
            var builder = new StringBuilder(value.Length + 8);
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (char.IsLetterOrDigit(current))
                {
                    var previous = index > 0 ? value[index - 1] : '\0';
                    var next = index + 1 < value.Length ? value[index + 1] : '\0';
                    var needsSeparator = index > 0
                        && char.IsUpper(current)
                        && (char.IsLower(previous) || char.IsDigit(previous)
                            || (char.IsUpper(previous) && char.IsLower(next)));
                    if (needsSeparator && builder.Length > 0 && builder[builder.Length - 1] != '_')
                        builder.Append('_');
                    builder.Append(char.ToLowerInvariant(current));
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            return builder.ToString().Trim('_');
        }

        public static string ToPascalIdentifier(string value)
        {
            value = value ?? string.Empty;
            var builder = new StringBuilder(value.Length);
            var capitalize = true;
            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    capitalize = true;
                    continue;
                }

                if (builder.Length == 0 && char.IsDigit(character))
                    builder.Append('N');
                builder.Append(capitalize ? char.ToUpperInvariant(character) : character);
                capitalize = false;
            }

            return builder.ToString();
        }

        public static bool IsReservedUserField(string rosFieldName)
            => (rosFieldName ?? string.Empty).StartsWith(FrameworkPrefix, StringComparison.Ordinal);

        public static string PresenceFieldName(string rosFieldName)
            => FrameworkPrefix + "has_" + (rosFieldName ?? string.Empty);
    }
}
