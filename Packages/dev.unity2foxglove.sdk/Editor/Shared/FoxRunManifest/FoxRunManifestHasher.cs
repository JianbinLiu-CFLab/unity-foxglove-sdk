// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunManifest
// Purpose: SHA-256 fingerprint helpers for FoxRun canonical JSON.

using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunManifestHasher
    {
        private const string LowerHex = "0123456789abcdef";
        private static readonly ThreadLocal<SHA256> Sha256 = new ThreadLocal<SHA256>(SHA256.Create);

        public static string Sha256Hex(string canonicalJson)
        {
            var bytes = Encoding.UTF8.GetBytes(canonicalJson ?? string.Empty);
            var sha = Sha256.Value;
            var hash = sha.ComputeHash(bytes);
            var chars = new char[hash.Length * 2];
            for (var i = 0; i < hash.Length; i++)
            {
                var value = hash[i];
                var offset = i * 2;
                chars[offset] = LowerHex[value >> 4];
                chars[offset + 1] = LowerHex[value & 0x0F];
            }

            return new string(chars);
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
