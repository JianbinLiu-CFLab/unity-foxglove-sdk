// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunManifest
// Purpose: SHA-256 fingerprint helpers for FoxRun canonical JSON.

using System.Security.Cryptography;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunManifestHasher
    {
        public static string Sha256Hex(string canonicalJson)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(canonicalJson ?? string.Empty);
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static bool IsLowercaseSha256Hex(string value)
        {
            if (value == null || value.Length != 64)
                return false;

            foreach (var ch in value)
            {
                var digit = ch >= '0' && ch <= '9';
                var lowerHex = ch >= 'a' && ch <= 'f';
                if (!digit && !lowerHex)
                    return false;
            }

            return true;
        }
    }
}
