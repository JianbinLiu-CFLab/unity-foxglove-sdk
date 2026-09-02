// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Assets
// Purpose: Thread-safe registry mapping URI prefixes to local file system
// roots for the Foxglove fetchAsset capability. No UnityEngine dependency.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

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
        private readonly Func<bool> _mutationAllowed;
        private static StringComparison FileSystemPathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        public FoxgloveAssetRegistry(Func<bool> mutationAllowed = null)
        {
            _mutationAllowed = mutationAllowed;
        }

        /// <summary>True if at least one asset root is registered.</summary>
        public bool HasRoots { get { lock (_lock) return _roots.Count > 0; } }

        /// <summary>
        /// Register a local file system root for a URI prefix.
        /// Prefixes are normalized to end in '/' and longest matching prefix wins.
        /// <para><c>maxBytes</c> caps the allowed file size for assets under this root.</para>
        /// </summary>
        public void RegisterRoot(string uriPrefix, string localRoot, long maxBytes = 16 * 1024 * 1024)
        {
            ThrowIfMutationBlocked();
            if (string.IsNullOrEmpty(uriPrefix)) throw new ArgumentException("uriPrefix is required", nameof(uriPrefix));
            if (string.IsNullOrWhiteSpace(localRoot)) throw new ArgumentException("localRoot is required", nameof(localRoot));
            var normalizedPrefix = NormalizeUriPrefix(uriPrefix);
            var fullRoot = Path.GetFullPath(localRoot);
            lock (_lock) { _roots[normalizedPrefix] = new AssetRoot { LocalRoot = fullRoot, MaxBytes = Math.Max(0, maxBytes) }; }
        }

        private void ThrowIfMutationBlocked()
        {
            if (_mutationAllowed != null && !_mutationAllowed())
                throw new InvalidOperationException(
                    "Asset registry mutations are unavailable while session cleanup is pending.");
        }

        /// <summary>
        /// Resolve a URI to a local file path.
        /// <para>Returns <c>true</c> if resolution succeeds; sets <c>path</c> and clears <c>error</c>.
        /// Returns <c>false</c> and sets <c>error</c> on path traversal, missing file, or size limit violations.</para>
        /// </summary>
        public bool TryResolve(string uri, out string path, out string error)
            => TryResolve(uri, out path, out _, out _, out error);

        private bool TryResolve(
            string uri,
            out string path,
            out string registeredRoot,
            out long maxBytes,
            out string error)
        {
            path = null; error = null;
            registeredRoot = null;
            maxBytes = 0;
            if (string.IsNullOrWhiteSpace(uri))
            {
                error = "Asset URI is required";
                return false;
            }

            string bestPrefix = null;
            var bestPrefixLength = -1;
            AssetRoot bestRoot = default;
            lock (_lock)
            {
                foreach (var (prefix, root) in _roots)
                {
                    if (prefix.Length <= bestPrefixLength || !uri.StartsWith(prefix, StringComparison.Ordinal))
                        continue;

                    bestPrefix = prefix;
                    bestPrefixLength = prefix.Length;
                    bestRoot = root;
                }
            }

            if (bestPrefix == null)
            {
                error = $"No asset root registered for URI: {uri}";
                return false;
            }

            var relative = uri.Substring(bestPrefix.Length);
            try
            {
                relative = Uri.UnescapeDataString(relative);
                relative = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var resolved = Path.GetFullPath(Path.Combine(bestRoot.LocalRoot, relative));
                var normalizedRoot = bestRoot.LocalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var comparison = FileSystemPathComparison;
                var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
                if (!resolved.StartsWith(rootPrefix, comparison) && !string.Equals(resolved, normalizedRoot, comparison))
                { error = $"Path traversal denied: {uri}"; return false; }
                if (Directory.Exists(resolved))
                { error = $"Path is a directory: {uri}"; return false; }
                if (!File.Exists(resolved))
                { error = $"File not found: {uri}"; return false; }
                if (ContainsReparsePoint(normalizedRoot, resolved))
                { error = $"Asset path contains a reparse point: {uri}"; return false; }
                var fi = new FileInfo(resolved);
                if (fi.Length > bestRoot.MaxBytes)
                { error = $"File exceeds size limit ({bestRoot.MaxBytes} bytes): {fi.Length}"; return false; }
                path = resolved;
                registeredRoot = normalizedRoot;
                maxBytes = bestRoot.MaxBytes;
                return true;
            }
            catch (Exception ex) when (IsAssetPathException(ex))
            {
                error = $"Invalid asset URI: {ex.Message}";
                return false;
            }
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
            if (!TryResolve(uri, out var path, out var registeredRoot, out var maxBytes, out error))
                return false;

            return TryReadResolvedFile(path, registeredRoot, maxBytes, out bytes, out error);
        }

        private static bool TryReadResolvedFile(
            string path,
            string registeredRoot,
            long maxBytes,
            out byte[] bytes,
            out string error)
        {
            bytes = null;
            error = null;

            try
            {
                if (!TryOpenAssetRootHandle(registeredRoot, out var rootHandle, out error))
                    return false;
                using (rootHandle)
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (!TryRequireOpenedFileWithinRoot(rootHandle, stream.SafeFileHandle, out error))
                        return false;
                    if (!TryRequireSingleFileLink(stream, out error))
                        return false;
                    if (stream.Length > maxBytes)
                    {
                        error = $"File exceeds size limit ({maxBytes} bytes): {stream.Length}";
                        return false;
                    }

                    var buffer = ArrayPool<byte>.Shared.Rent(81920);
                    try
                    {
                        using var output = new MemoryStream(stream.Length <= int.MaxValue ? (int)stream.Length : 0);
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
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
            catch (Exception ex) when (IsAssetPathException(ex))
            {
                error = $"Failed to read asset: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Open and pin the registered root directory for one asset read.
        /// </summary>
        internal static bool TryOpenAssetRootHandle(
            string registeredRoot,
            out SafeFileHandle handle,
            out string error)
        {
            handle = null;
            error = null;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var opened = CreateFileW(
                        registeredRoot,
                        0,
                        FileShare.ReadWrite | FileShare.Delete,
                        IntPtr.Zero,
                        FileMode.Open,
                        FileFlagBackupSemantics,
                        IntPtr.Zero);
                    if (opened.IsInvalid)
                    {
                        error = $"Could not open the registered asset root (native error {Marshal.GetLastWin32Error()}).";
                        opened.Dispose();
                        return false;
                    }

                    handle = opened;
                    return true;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var descriptor = Open(registeredRoot, OpenReadOnly);
                    if (descriptor < 0)
                    {
                        error = $"Could not open the registered asset root (native error {Marshal.GetLastWin32Error()}).";
                        return false;
                    }

                    handle = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
                    return true;
                }

                error = "The current platform does not expose a supported asset-root handle API.";
                return false;
            }
            catch (Exception ex) when (
                ex is DllNotFoundException
                || ex is EntryPointNotFoundException
                || ex is BadImageFormatException)
            {
                error = $"Native asset-root inspection is unavailable ({ex.GetType().Name}).";
                return false;
            }
        }

        /// <summary>
        /// Verify final handle-resolved containment before reading any asset bytes.
        /// </summary>
        internal static bool TryRequireOpenedFileWithinRoot(
            SafeFileHandle rootHandle,
            SafeFileHandle fileHandle,
            out string error)
        {
            error = null;
            if (!TryGetFinalPath(rootHandle, out var finalRoot, out var rootError))
            {
                error = $"Could not verify the registered asset root: {rootError}";
                return false;
            }

            if (!TryGetFinalPath(fileHandle, out var finalFile, out var fileError))
            {
                error = $"Could not verify the opened asset path: {fileError}";
                return false;
            }

            var normalizedRoot = finalRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (finalFile.StartsWith(rootPrefix, FileSystemPathComparison))
                return true;

            error = "The opened asset is outside the registered root.";
            return false;
        }

        private static bool TryGetFinalPath(
            SafeFileHandle handle,
            out string path,
            out string error)
        {
            path = null;
            error = null;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var capacity = 512;
                    while (capacity <= 32768)
                    {
                        var buffer = new StringBuilder(capacity);
                        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                        if (length == 0)
                        {
                            error = $"GetFinalPathNameByHandleW failed with native error {Marshal.GetLastWin32Error()}.";
                            return false;
                        }

                        if (length < buffer.Capacity)
                        {
                            path = buffer.ToString();
                            return true;
                        }

                        capacity = checked((int)length + 1);
                    }

                    error = "The resolved asset path exceeds the supported native path length.";
                    return false;
                }

                var descriptor = checked((int)handle.DangerousGetHandle().ToInt64());
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var buffer = new byte[4096];
                    var length = ReadLink(
                        "/proc/self/fd/" + descriptor,
                        buffer,
                        new UIntPtr((uint)buffer.Length)).ToInt64();
                    if (length < 0)
                    {
                        error = $"readlink failed with native error {Marshal.GetLastWin32Error()}.";
                        return false;
                    }

                    if (length >= buffer.Length)
                    {
                        error = "The resolved asset path exceeds the supported native path length.";
                        return false;
                    }

                    path = Encoding.UTF8.GetString(buffer, 0, checked((int)length));
                    return true;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var buffer = new byte[DarwinMaxPathLength];
                    if (Fcntl(descriptor, DarwinGetPath, buffer) != 0)
                    {
                        error = $"fcntl(F_GETPATH) failed with native error {Marshal.GetLastWin32Error()}.";
                        return false;
                    }

                    var length = Array.IndexOf(buffer, (byte)0);
                    if (length < 0)
                        length = buffer.Length;
                    path = Encoding.UTF8.GetString(buffer, 0, length);
                    return true;
                }

                error = "The current platform does not expose a supported final-path inspection API.";
                return false;
            }
            catch (Exception ex) when (
                ex is DllNotFoundException
                || ex is EntryPointNotFoundException
                || ex is BadImageFormatException
                || ex is OverflowException)
            {
                error = $"Native final-path inspection is unavailable ({ex.GetType().Name}).";
                return false;
            }
        }

        private static bool IsAssetPathException(Exception ex) =>
            ex is ArgumentException
            || ex is IOException
            || ex is NotSupportedException
            || ex is PathTooLongException
            || ex is UnauthorizedAccessException
            || ex is System.Security.SecurityException
            || ex is UriFormatException;

        private static bool ContainsReparsePoint(string normalizedRoot, string resolved)
        {
            var current = normalizedRoot;
            var relative = resolved.Substring(normalizedRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var component in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return true;
            }

            return false;
        }

        private static bool TryRequireSingleFileLink(
            FileStream stream,
            out string error)
        {
            error = null;
            if (!TryGetFileLinkCount(stream.SafeFileHandle, out var linkCount, out var nativeError))
            {
                error = $"Could not verify the asset hard-link count: {nativeError}";
                return false;
            }

            if (linkCount != 1)
            {
                error = $"Asset file has {linkCount} hard links; only single-link files may be served.";
                return false;
            }

            return true;
        }

        private static bool TryGetFileLinkCount(
            SafeFileHandle handle,
            out uint linkCount,
            out string error)
        {
            linkCount = 0;
            error = null;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (!GetFileInformationByHandle(handle, out var information))
                    {
                        error = $"GetFileInformationByHandle failed with native error {Marshal.GetLastWin32Error()}.";
                        return false;
                    }

                    linkCount = information.NumberOfLinks;
                    return true;
                }

                var descriptor = checked((int)handle.DangerousGetHandle().ToInt64());
                var buffer = Marshal.AllocHGlobal(256);
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        if (Statx(descriptor, string.Empty, AtEmptyPath, StatxNlink, buffer) != 0)
                        {
                            error = $"statx failed with native error {Marshal.GetLastWin32Error()}.";
                            return false;
                        }

                        linkCount = unchecked((uint)Marshal.ReadInt32(buffer, StatxNlinkOffset));
                        return true;
                    }

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        if (Fstat(descriptor, buffer) != 0)
                        {
                            error = $"fstat failed with native error {Marshal.GetLastWin32Error()}.";
                            return false;
                        }

                        linkCount = unchecked((ushort)Marshal.ReadInt16(buffer, DarwinNlinkOffset));
                        return true;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                error = "The current platform does not expose a supported file-link inspection API.";
                return false;
            }
            catch (Exception ex) when (
                ex is DllNotFoundException
                || ex is EntryPointNotFoundException
                || ex is BadImageFormatException
                || ex is OverflowException)
            {
                error = $"Native file-link inspection is unavailable ({ex.GetType().Name}).";
                return false;
            }
        }

        private const int AtEmptyPath = 0x1000;
        private const int OpenReadOnly = 0;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint StatxNlink = 0x0004;
        private const int StatxNlinkOffset = 16;
        private const int DarwinGetPath = 50;
        private const int DarwinMaxPathLength = 1024;
        private const int DarwinNlinkOffset = 6;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileTime
        {
            internal uint Low;
            internal uint High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            internal uint FileAttributes;
            internal NativeFileTime CreationTime;
            internal NativeFileTime LastAccessTime;
            internal NativeFileTime LastWriteTime;
            internal uint VolumeSerialNumber;
            internal uint FileSizeHigh;
            internal uint FileSizeLow;
            internal uint NumberOfLinks;
            internal uint FileIndexHigh;
            internal uint FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation information);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle file,
            StringBuilder filePath,
            uint filePathLength,
            uint flags);

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int Open(string path, int flags);

        [DllImport("libc", EntryPoint = "readlink", SetLastError = true)]
        private static extern IntPtr ReadLink(string path, byte[] buffer, UIntPtr bufferSize);

        [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
        private static extern int Fcntl(int fileDescriptor, int command, byte[] buffer);

        [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
        private static extern int Statx(
            int directoryFileDescriptor,
            string path,
            int flags,
            uint mask,
            IntPtr buffer);

        [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
        private static extern int Fstat(int fileDescriptor, IntPtr buffer);

        private static string NormalizeUriPrefix(string uriPrefix)
        {
            var normalized = uriPrefix.Trim().Replace('\\', '/');
            return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
        }

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
