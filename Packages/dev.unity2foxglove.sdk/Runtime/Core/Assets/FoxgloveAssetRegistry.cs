// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Assets
// Purpose: Thread-safe registry mapping URI prefixes to local file system
// roots for the Foxglove fetchAsset capability. No UnityEngine dependency.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Thread-safe registry mapping URI prefixes to local file system roots for fetchAsset.
    /// No UnityEngine dependency — dotnet-testable.
    /// </summary>
    public class FoxgloveAssetRegistry
    {
        /// <summary>Map from URI prefix to asset root descriptor.</summary>
        private readonly Dictionary<string, AssetRoot> _roots = new();
        /// <summary>Lock guarding root map modifications and queries.</summary>
        private readonly object _lock = new();
        private static StringComparison FileSystemPathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        /// <summary>True if at least one asset root is registered.</summary>
        public bool HasRoots { get { lock (_lock) return _roots.Count > 0; } }

        /// <summary>
        /// Register a local file system root for a URI prefix.
        /// <para><c>maxBytes</c> caps the allowed file size for assets under this root.</para>
        /// </summary>
        public void RegisterRoot(string uriPrefix, string localRoot, long maxBytes = 16 * 1024 * 1024)
        {
            if (string.IsNullOrEmpty(uriPrefix)) throw new ArgumentException("uriPrefix is required", nameof(uriPrefix));
            if (string.IsNullOrWhiteSpace(localRoot)) throw new ArgumentException("localRoot is required", nameof(localRoot));
            var fullRoot = Path.GetFullPath(localRoot);
            lock (_lock) { _roots[uriPrefix] = new AssetRoot { LocalRoot = fullRoot, MaxBytes = Math.Max(0, maxBytes) }; }
        }

        /// <summary>
        /// Resolve a URI to a local file path.
        /// <para>Returns <c>true</c> if resolution succeeds; sets <c>path</c> and clears <c>error</c>.
        /// Returns <c>false</c> and sets <c>error</c> on path traversal, missing file, or size limit violations.</para>
        /// </summary>
        public bool TryResolve(string uri, out string path, out string error)
            => TryResolve(uri, out path, out _, out error);

        private bool TryResolve(string uri, out string path, out long maxBytes, out string error)
        {
            path = null; error = null;
            maxBytes = 0;
            if (string.IsNullOrWhiteSpace(uri))
            {
                error = "Asset URI is required";
                return false;
            }

            List<KeyValuePair<string, AssetRoot>> roots;
            lock (_lock)
            {
                roots = _roots.ToList();
            }

            foreach (var (prefix, root) in roots)
            {
                if (!uri.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                var relative = uri.Substring(prefix.Length);
                try
                {
                    relative = Uri.UnescapeDataString(relative);
                    relative = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    var resolved = Path.GetFullPath(Path.Combine(root.LocalRoot, relative));
                    var normalizedRoot = root.LocalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var comparison = FileSystemPathComparison;
                    var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
                    if (!resolved.StartsWith(rootPrefix, comparison) && !string.Equals(resolved, normalizedRoot, comparison))
                    { error = $"Path traversal denied: {uri}"; return false; }
                    if (Directory.Exists(resolved))
                    { error = $"Path is a directory: {uri}"; return false; }
                    if (!File.Exists(resolved))
                    { error = $"File not found: {uri}"; return false; }
                    var fi = new FileInfo(resolved);
                    if (fi.Length > root.MaxBytes)
                    { error = $"File exceeds size limit ({root.MaxBytes} bytes): {fi.Length}"; return false; }
                    path = resolved;
                    maxBytes = root.MaxBytes;
                    return true;
                }
                catch (Exception ex) when (IsAssetPathException(ex))
                {
                    error = $"Invalid asset URI: {ex.Message}";
                    return false;
                }
            }
            error = $"No asset root registered for URI: {uri}";
            return false;
        }

        /// <summary>
        /// Read the full content of an asset identified by URI.
        /// <para>Returns <c>null</c> and sets <c>error</c> on any resolution failure.</para>
        /// </summary>
        public byte[] ReadAsset(string uri, out string error)
        {
            return TryRead(uri, out var bytes, out error) ? bytes : null;
        }

        /// <summary>
        /// Try to read an asset into a byte array without throwing.
        /// <para>Returns <c>false</c> and sets <c>error</c> on failure.</para>
        /// </summary>
        public bool TryRead(string uri, out byte[] bytes, out string error)
        {
            bytes = null;
            if (!TryResolve(uri, out var path, out var maxBytes, out error))
                return false;

            return TryReadResolvedFile(path, maxBytes, out bytes, out error);
        }

        private static bool TryReadResolvedFile(string path, long maxBytes, out byte[] bytes, out string error)
        {
            bytes = null;
            error = null;

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > maxBytes)
                {
                    error = $"File exceeds size limit ({maxBytes} bytes): {stream.Length}";
                    return false;
                }

                using var output = new MemoryStream(stream.Length <= int.MaxValue ? (int)stream.Length : 0);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        error = $"File exceeds size limit ({maxBytes} bytes): {total}";
                        return false;
                    }

                    output.Write(buffer, 0, read);
                }

                bytes = output.ToArray();
                return true;
            }
            catch (Exception ex) when (IsAssetPathException(ex))
            {
                error = $"Failed to read asset: {ex.Message}";
                return false;
            }
        }

        private static bool IsAssetPathException(Exception ex) =>
            ex is ArgumentException
            || ex is IOException
            || ex is NotSupportedException
            || ex is PathTooLongException
            || ex is UnauthorizedAccessException
            || ex is UriFormatException;

        /// <summary>Descriptor for a registered asset root path and its size cap.</summary>
        private struct AssetRoot
        {
            /// <summary>Local file system path for this root.</summary>
            public string LocalRoot;
            /// <summary>Maximum allowed file size in bytes for assets under this root.</summary>
            public long MaxBytes;
        }
    }
}
