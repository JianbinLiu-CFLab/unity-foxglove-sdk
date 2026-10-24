// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase184
// Purpose: Case-isolated Unity evidence surface for Phase184 profile acceptance.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Unity2Foxglove.Ros2Bridge;
using Debug = UnityEngine.Debug;

namespace Unity2Foxglove.ManualAcceptance
{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    internal static class Phase184AcceptanceText
    {
        internal static bool IsSafeToken(string token)
        {
            if (string.IsNullOrEmpty(token)
                || token.Length < 18
                || token.Length > 70
                || !token.StartsWith("p184g_", StringComparison.Ordinal))
            {
                return false;
            }
            for (var index = 6; index < token.Length; index++)
            {
                var character = token[index];
                if (!(character >= 'a' && character <= 'z')
                    && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9'))
                {
                    return false;
                }
            }
            return true;
        }

        internal static string SafeMarker(string value)
        {
            value = Bound(value, 512);
            var characters = value.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                var character = characters[index];
                if (character == ' '
                    || character == '\r'
                    || character == '\n'
                    || character == '\t'
                    || character == '=')
                {
                    characters[index] = '_';
                }
            }
            return new string(characters);
        }

        internal static string FormatTransportIds(
            IEnumerable<FoxRunTransportId> transportIds)
        {
            var names = new List<string>();
            if (transportIds != null)
            {
                foreach (var id in transportIds)
                    names.Add(id.Value);
            }
            names.Sort(StringComparer.Ordinal);
            return names.Count == 0
                ? "None"
                : string.Join(",", names);
        }

        internal static string FormatTransportIds(
            IEnumerable<string> transportIds)
        {
            var names = new List<string>();
            if (transportIds != null)
            {
                foreach (var id in transportIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                        names.Add(id);
                }
            }
            names.Sort(StringComparer.Ordinal);
            return names.Count == 0
                ? "None"
                : string.Join(",", names);
        }

        internal static string Bound(string value, int maximum)
        {
            value ??= string.Empty;
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }

        internal static string TokenDigestPrefix(string token)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(token ?? string.Empty);
            var digest = sha.ComputeHash(bytes);
            var text = BitConverter.ToString(digest).Replace("-", string.Empty);
            return text.Substring(0, 12).ToLowerInvariant();
        }
    }
}
