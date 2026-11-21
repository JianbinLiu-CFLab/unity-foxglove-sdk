// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: Windows native handle opening primitives for the OpenH264 safe-file layer.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Opens verified Windows handles for OpenH264 file transactions.</summary>
    internal static partial class OpenH264WindowsNativeApi
    {
        /// <summary>Opens an existing local install root without following its final reparse point.</summary>
        internal static bool TryOpenRoot(
            string path,
            out SafeFileHandle handle,
            out OpenH264WindowsFileIdentity identity,
            out string reason)
        {
            handle = null;
            identity = default;
            reason = "";
            if (!OperatingSystem.IsWindows())
            {
                reason = "Windows safe-file operations are unsupported on this platform.";
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                reason = "The install root is invalid: " + ex.Message;
                return false;
            }

            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            {
                reason = "Secure OpenH264 transactions are unsupported for network roots.";
                return false;
            }

            if (!TryOpen(
                    ToNtPath(fullPath),
                    IntPtr.Zero,
                    GenericRead | Delete | Synchronize | FileListDirectory | FileAddSubdirectory,
                    FileDirectoryFile | FileOpenReparsePoint | FileOpenForBackupIntent | FileSynchronousIoNonAlert,
                    FileOpen,
                    ShareReadWrite,
                    out handle,
                    out reason))
                return false;

            if (!TryGetIdentity(handle, out identity, out reason))
            {
                handle.Dispose();
                handle = null;
                return false;
            }

            if (identity.IsReparsePoint)
            {
                handle.Dispose();
                handle = null;
                reason = "The install root is a reparse point.";
                return false;
            }

            if (!TryGetFinalPath(handle, out var actualPath)
                || !PathsEqual(fullPath, actualPath))
            {
                handle.Dispose();
                handle = null;
                reason = "The install root resolves through a different path.";
                return false;
            }

            return true;
        }

        /// <summary>Creates or opens a child directory relative to an already opened directory.</summary>
        internal static bool TryOpenRelativeDirectory(
            SafeFileHandle parent,
            string name,
            bool createNew,
            out SafeFileHandle handle,
            out OpenH264WindowsFileIdentity identity,
            out string reason)
        {
            handle = null;
            identity = default;
            reason = "";
            if (!IsSimpleName(name, out reason))
                return false;

            var disposition = createNew ? FileCreate : FileOpen;
            if (!TryOpen(
                    name,
                    parent.DangerousGetHandle(),
                    GenericRead | Delete | Synchronize | FileListDirectory | FileAddSubdirectory,
                    FileDirectoryFile | FileOpenReparsePoint | FileOpenForBackupIntent | FileSynchronousIoNonAlert,
                    disposition,
                    ShareAll,
                    out handle,
                    out reason))
                return false;

            return VerifyOpenedObject(handle, "directory", out identity, out reason);
        }

        /// <summary>Opens a child directory with the minimal access needed for a relative rename.</summary>
        internal static bool TryOpenRelativeRenameDirectory(
            SafeFileHandle parent,
            string name,
            out SafeFileHandle handle,
            out OpenH264WindowsFileIdentity identity,
            out string reason)
        {
            handle = null;
            identity = default;
            reason = "";
            if (!IsSimpleName(name, out reason))
                return false;

            if (!TryOpen(
                    name,
                    parent.DangerousGetHandle(),
                    FileTraverse | FileReadAttributes | Synchronize,
                    FileDirectoryFile | FileOpenReparsePoint | FileOpenForBackupIntent | FileSynchronousIoNonAlert,
                    FileOpen,
                    ShareAll,
                    out handle,
                    out reason))
                return false;

            return VerifyOpenedObject(handle, "directory", out identity, out reason);
        }

        /// <summary>Creates or opens a child file relative to an already opened directory.</summary>
        internal static bool TryOpenRelativeFile(
            SafeFileHandle parent,
            string name,
            bool createNew,
            out SafeFileHandle handle,
            out OpenH264WindowsFileIdentity identity,
            out string reason)
        {
            handle = null;
            identity = default;
            reason = "";
            if (!IsSimpleName(name, out reason))
                return false;

            var disposition = createNew ? FileCreate : FileOpen;
            if (!TryOpen(
                    name,
                    parent.DangerousGetHandle(),
                    GenericRead | GenericWrite | Delete | Synchronize,
                    FileNonDirectoryFile | FileOpenReparsePoint | FileOpenForBackupIntent | FileSynchronousIoNonAlert,
                    disposition,
                    ShareAll,
                    out handle,
                    out reason))
                return false;

            return VerifyOpenedObject(handle, "file", out identity, out reason);
        }

        /// <summary>Checks identity and rejects a reparse point after a native open.</summary>
        private static bool VerifyOpenedObject(
            SafeFileHandle handle,
            string objectType,
            out OpenH264WindowsFileIdentity identity,
            out string reason)
        {
            if (!TryGetIdentity(handle, out identity, out reason))
            {
                handle.Dispose();
                handle = null;
                return false;
            }

            if (identity.IsReparsePoint)
            {
                handle.Dispose();
                handle = null;
                reason = "The opened " + objectType + " is a reparse point.";
                return false;
            }

            return true;
        }

        /// <summary>Validates that a child name cannot escape its parent handle.</summary>
        private static bool IsSimpleName(string name, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(name)
                || name == "."
                || name == ".."
                || name.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
            {
                reason = "Only a single relative file or directory name is allowed.";
                return false;
            }

            return true;
        }

        /// <summary>Converts a local Win32 path to an NT object-manager path.</summary>
        private static string ToNtPath(string path)
            => @"\??\" + path;

        /// <summary>Opens an object with NtCreateFile using an optional parent handle.</summary>
        private static bool TryOpen(
            string name,
            IntPtr rootDirectory,
            uint access,
            uint options,
            uint disposition,
            uint shareMode,
            out SafeFileHandle handle,
            out string reason)
        {
            handle = null;
            reason = "";
            using var nativeName = new NativeName(name);
            var attributes = new ObjectAttributes
            {
                Length = (uint)Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = rootDirectory,
                ObjectName = nativeName.Structure,
                Attributes = 0x00000040
            };
            var status = NtCreateFile(
                out var rawHandle,
                access,
                ref attributes,
                out _,
                IntPtr.Zero,
                0,
                shareMode,
                disposition,
                options,
                IntPtr.Zero,
                0);
            if (status < 0)
            {
                reason = $"Native open failed with NTSTATUS 0x{status:X8}.";
                return false;
            }

            handle = new SafeFileHandle(rawHandle, ownsHandle: true);
            return true;
        }

        /// <summary>Reads the volume and file index for an opened object.</summary>
        private static bool TryGetIdentity(
            SafeFileHandle handle,
            out OpenH264WindowsFileIdentity identity,
            out string reason)
        {
            reason = "";
            if (!GetFileInformationByHandle(handle, out var information))
            {
                identity = default;
                reason = "Could not read file identity. Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }

            identity = new OpenH264WindowsFileIdentity(
                information.VolumeSerialNumber,
                information.FileIndexHigh,
                information.FileIndexLow,
                (information.FileAttributes & FileAttributeReparsePoint) != 0);
            return true;
        }

        /// <summary>Reads the final path of an opened root for ancestor-reparse rejection.</summary>
        private static bool TryGetFinalPath(SafeFileHandle handle, out string path)
        {
            path = "";
            var capacity = 512;
            while (capacity <= 32768)
            {
                var buffer = new StringBuilder(capacity);
                var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0)
                    return false;
                if (length < buffer.Capacity)
                {
                    path = NormalizeFinalPath(buffer.ToString());
                    return true;
                }

                capacity = checked((int)length + 1);
            }

            return false;
        }

        /// <summary>Compares local paths after removing Windows final-path prefixes.</summary>
        private static bool PathsEqual(string expected, string actual)
            => string.Equals(
                NormalizeFinalPath(expected).TrimEnd('\\'),
                NormalizeFinalPath(actual).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);

        /// <summary>Normalizes a Win32 final path into the ordinary local-path form.</summary>
        private static string NormalizeFinalPath(string path)
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
                return path.Substring(4);
            return path;
        }
    }
}
