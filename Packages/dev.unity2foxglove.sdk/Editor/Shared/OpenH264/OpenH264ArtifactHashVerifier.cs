// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: SHA256 verification for explicit OpenH264 native artifact installs.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class OpenH264ArtifactHashVerifier
    {
        public static bool TryVerifySha256(
            string path,
            string expectedSha256,
            string artifactLabel,
            out string actualSha256,
            out string error)
        {
            actualSha256 = "";
            error = "";

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                error = $"{artifactLabel ?? "OpenH264 artifact"} was not found for SHA256 verification: {path}";
                return false;
            }

            if (!IsSha256Hex(expectedSha256))
            {
                error = $"{artifactLabel ?? "OpenH264 artifact"} expected SHA256 is not a valid 64-character hex value.";
                return false;
            }

            try
            {
                actualSha256 = ComputeSha256(path);
            }
            catch (Exception ex)
            {
                error = $"{artifactLabel ?? "OpenH264 artifact"} SHA256 verification failed: {ex.Message}";
                return false;
            }

            if (string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                return true;

            error = $"{artifactLabel ?? "OpenH264 artifact"} SHA256 mismatch. "
                    + $"Expected {expectedSha256.ToUpperInvariant()}, actual {actualSha256}.";
            return false;
        }

        public static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
                return ToUpperHex(sha256.ComputeHash(stream));
        }

        private static bool IsSha256Hex(string value)
        {
            if (value == null || value.Length != 64)
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9')
                      || (c >= 'a' && c <= 'f')
                      || (c >= 'A' && c <= 'F')))
                    return false;
            }

            return true;
        }

        private static string ToUpperHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}
