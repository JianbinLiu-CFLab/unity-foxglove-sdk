// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Stable, ROS-free identity rules for the static FoxRun interface source package.

using System;
using System.Globalization;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Owns only stable source-package identities. It deliberately contains no
    /// ROS2, RMW, UnityEditor, or filesystem dependency so descriptors, editor
    /// commands, and later add-ons all speak the same immutable identity.
    /// </summary>
    public static class FoxRunRos2InterfaceIdentity
    {
        public const string UnityPackageId = "dev.unity2foxglove.foxrun.ros2.interfaces";
        public const string DefaultRosPackageName = "unity2foxglove_foxrun_interfaces_v1";
        public const int LockSchemaVersion = 1;
        public const int InterfaceSchemaVersion = 1;
        // ROS 2 message identifiers use the UpperCamelCase grammar and cannot
        // contain the underscore delimiter that an early static-package draft
        // used before its first real rosidl build.
        public const int NamingPolicyVersion = 2;

        public static string BuildRosPackageName(int revision)
        {
            if (revision < 1)
                throw new ArgumentOutOfRangeException(nameof(revision), "Interface revision must be at least one.");

            return "unity2foxglove_foxrun_interfaces_v" + revision.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Advances a previously selected package stem without allowing a later
        /// revision to silently switch namespace. Operators may choose the
        /// initial <c>*_v1</c> name before first generation; the lock then owns
        /// the stem for every explicit subsequent revision.
        /// </summary>
        public static string BuildRosPackageName(string currentPackageName, int revision)
        {
            if (!TryParseRosPackageRevision(currentPackageName, out _))
                throw new ArgumentException("The current ROS package name must use the _vN revision grammar.", nameof(currentPackageName));
            if (revision < 1)
                throw new ArgumentOutOfRangeException(nameof(revision), "Interface revision must be at least one.");

            var marker = currentPackageName.LastIndexOf("_v", StringComparison.Ordinal);
            return currentPackageName.Substring(0, marker) + "_v" + revision.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParseRosPackageRevision(string packageName, out int revision)
        {
            revision = 0;
            if (!IsValidRosPackageName(packageName))
            {
                return false;
            }

            var marker = packageName.LastIndexOf("_v", StringComparison.Ordinal);
            if (marker <= 0 || marker + 2 >= packageName.Length)
                return false;

            var stem = packageName.Substring(0, marker);
            var suffix = packageName.Substring(marker + 2);
            return !stem.EndsWith("_", StringComparison.Ordinal)
                   && int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out revision)
                   && revision >= 1
                   && string.Equals(stem + "_v" + revision.ToString(CultureInfo.InvariantCulture), packageName, StringComparison.Ordinal);
        }

        public static bool IsValidRosPackageName(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)
                || packageName.Length < 2
                || packageName.Length > 255)
                return false;
            if (!IsLowerAsciiLetter(packageName[0]) || packageName[packageName.Length - 1] == '_')
                return false;

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

        /// <summary>
        /// Validate an exact ROS 2 message identity in
        /// <c>package/msg/Message</c> form without loading ROS assemblies.
        /// </summary>
        internal static bool IsValidCanonicalRosMessageType(string canonicalType)
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

            var packageName = canonicalType.Substring(0, firstSlash);
            var messageName = canonicalType.Substring(firstSlash + 5);
            return IsValidRosPackageName(packageName)
                   && IsValidRosMessageName(messageName);
        }

        internal static bool IsValidRosMessageName(string messageName)
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

        public static string BuildEnvelopeMessageName(string payloadMessageName)
        {
            if (string.IsNullOrWhiteSpace(payloadMessageName))
                throw new ArgumentException("Payload message name is required.", nameof(payloadMessageName));

            return payloadMessageName + "Envelope";
        }

        private static bool IsLowerAsciiLetter(char character)
            => character >= 'a' && character <= 'z';

        private static bool IsUpperAsciiLetter(char character)
            => character >= 'A' && character <= 'Z';
    }
}
