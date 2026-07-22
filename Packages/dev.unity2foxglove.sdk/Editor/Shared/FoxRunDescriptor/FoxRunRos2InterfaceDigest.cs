// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Cross-host deterministic digest framing for the static ROS2 interface package.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Editor
{
    public sealed class FoxRunRos2InterfaceDigestInput
    {
        public FoxRunRos2InterfaceDigestInput(string relativePath, byte[] bytes)
        {
            RelativePath = relativePath ?? string.Empty;
            Bytes = bytes == null ? Array.Empty<byte>() : bytes.ToArray();
        }

        public FoxRunRos2InterfaceDigestInput(string relativePath, string text)
            : this(relativePath, FoxRunRos2InterfaceDigest.EncodeText(text))
        {
        }

        public string RelativePath { get; }
        public byte[] Bytes { get; }
    }

    /// <summary>
    /// The framing is intentionally tiny and public so the Python helper can
    /// calculate the exact same SHA-256 without importing Unity or ros2cs:
    /// a domain frame, schema-version frame, then ordinal path/content frames.
    /// All lengths are unsigned 64-bit big-endian byte counts.
    /// </summary>
    public static class FoxRunRos2InterfaceDigest
    {
        private const string Domain = "unity2foxglove:foxrun-ros2-interface-digest:v1";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false, true);

        public static string Compute(int schemaVersion, IEnumerable<FoxRunRos2InterfaceDigestInput> inputs)
        {
            if (schemaVersion != FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion),
                    "The digest framing accepts only the current interface schema version.");
            }

            var normalized = NormalizeInputs(inputs);
            using (var stream = new MemoryStream())
            {
                AppendFrame(stream, Utf8NoBom.GetBytes(Domain));
                AppendFrame(stream, Utf8NoBom.GetBytes(schemaVersion.ToString(CultureInfo.InvariantCulture)));
                foreach (var input in normalized)
                {
                    AppendFrame(stream, Utf8NoBom.GetBytes(input.RelativePath));
                    AppendFrame(stream, input.Bytes);
                }

                using (var sha256 = SHA256.Create())
                {
                    return ToLowerHex(sha256.ComputeHash(stream.ToArray()));
                }
            }
        }

        public static byte[] EncodeText(string value)
        {
            value = (value ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            return Utf8NoBom.GetBytes(value);
        }

        public static string NormalizeRelativePath(string relativePath)
        {
            var normalized = (relativePath ?? string.Empty).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.StartsWith("/", StringComparison.Ordinal)
                || normalized.EndsWith("/", StringComparison.Ordinal)
                || normalized.Contains("//", StringComparison.Ordinal))
            {
                throw new ArgumentException("Digest paths must be normalized relative package paths.", nameof(relativePath));
            }

            var segments = normalized.Split('/');
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                                        || string.Equals(segment, ".", StringComparison.Ordinal)
                                        || string.Equals(segment, "..", StringComparison.Ordinal)))
            {
                throw new ArgumentException("Digest paths cannot escape the package root.", nameof(relativePath));
            }

            return normalized;
        }

        private static IReadOnlyList<FoxRunRos2InterfaceDigestInput> NormalizeInputs(
            IEnumerable<FoxRunRos2InterfaceDigestInput> inputs)
        {
            var source = (inputs ?? Array.Empty<FoxRunRos2InterfaceDigestInput>())
                .Select(input => input ?? throw new ArgumentException("Digest inputs cannot contain null.", nameof(inputs)))
                .Select(input => new FoxRunRos2InterfaceDigestInput(
                    NormalizeRelativePath(input.RelativePath),
                    input.Bytes))
                .OrderBy(input => input.RelativePath, StringComparer.Ordinal)
                .ToList();

            var caseInsensitivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var input in source)
            {
                if (!caseInsensitivePaths.Add(input.RelativePath))
                {
                    throw new ArgumentException(
                        "Digest inputs contain duplicate or case-colliding paths: " + input.RelativePath,
                        nameof(inputs));
                }
            }

            return source.AsReadOnly();
        }

        private static void AppendFrame(Stream stream, byte[] bytes)
        {
            bytes = bytes ?? Array.Empty<byte>();
            var length = unchecked((ulong)bytes.Length);
            for (var shift = 56; shift >= 0; shift -= 8)
                stream.WriteByte((byte)(length >> shift));
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder((bytes?.Length ?? 0) * 2);
            foreach (var value in bytes ?? Array.Empty<byte>())
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
