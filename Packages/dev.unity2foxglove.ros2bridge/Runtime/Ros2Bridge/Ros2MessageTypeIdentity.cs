// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Strict ROS 2 message-name validation owned by the Bridge provider.

using System;

namespace Unity2Foxglove.Ros2Bridge
{
    internal static class Ros2MessageTypeIdentity
    {
        internal static bool IsValidCanonicalMessageType(string canonicalType)
        {
            if (string.IsNullOrEmpty(canonicalType))
                return false;

            var firstSlash = canonicalType.IndexOf('/');
            var secondSlash = firstSlash < 0
                ? -1
                : canonicalType.IndexOf('/', firstSlash + 1);
            if (firstSlash <= 0
                || secondSlash != firstSlash + 4
                || canonicalType.LastIndexOf('/') != secondSlash
                || canonicalType.Length <= firstSlash + 5
                || !string.Equals(
                    canonicalType.Substring(firstSlash + 1, 3),
                    "msg",
                    StringComparison.Ordinal))
            {
                return false;
            }

            return IsValidPackageName(canonicalType.Substring(0, firstSlash))
                   && IsValidMessageName(canonicalType.Substring(firstSlash + 5));
        }

        internal static bool IsValidPackageName(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)
                || packageName.Length < 2
                || packageName.Length > 255
                || !IsLowerAsciiLetter(packageName[0])
                || packageName[packageName.Length - 1] == '_')
            {
                return false;
            }

            for (var index = 1; index < packageName.Length; index++)
            {
                var character = packageName[index];
                if (character == '_' && packageName[index - 1] == '_')
                    return false;
                if (!IsLowerAsciiLetter(character)
                    && character != '_'
                    && (character < '0' || character > '9'))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool IsValidMessageName(string messageName)
        {
            if (string.IsNullOrEmpty(messageName)
                || messageName.Length > 255
                || !IsUpperAsciiLetter(messageName[0]))
            {
                return false;
            }

            for (var index = 1; index < messageName.Length; index++)
            {
                var character = messageName[index];
                if (!IsUpperAsciiLetter(character)
                    && !IsLowerAsciiLetter(character)
                    && (character < '0' || character > '9'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsLowerAsciiLetter(char character)
            => character >= 'a' && character <= 'z';

        private static bool IsUpperAsciiLetter(char character)
            => character >= 'A' && character <= 'Z';
    }
}
