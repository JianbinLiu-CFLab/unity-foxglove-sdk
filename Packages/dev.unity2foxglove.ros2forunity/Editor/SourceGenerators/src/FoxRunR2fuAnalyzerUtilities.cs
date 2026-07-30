// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: R2FU FoxRun source generator
// Purpose: Minimal deterministic helpers required by R2FU emitters.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class ConditionEmitter
    {
        internal static string ConditionAccess(
            string conditionName,
            FoxRunConditionMemberKind memberKind)
        {
            var name = (conditionName ?? string.Empty).Trim();
            var access = IdentifierUtils.EscapeIdentifier(name);
            return memberKind == FoxRunConditionMemberKind.Method
                ? access + "()"
                : access;
        }
    }

    internal static class TopicMetadataEmitter
    {
        internal static string Sha256Hex(string value)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(
                    Encoding.UTF8.GetBytes(
                        value ?? string.Empty));
                var output = new StringBuilder(hash.Length * 2);
                foreach (var valueByte in hash)
                {
                    output.Append(
                        valueByte.ToString(
                            "x2",
                            CultureInfo.InvariantCulture));
                }

                return output.ToString();
            }
        }
    }
}
