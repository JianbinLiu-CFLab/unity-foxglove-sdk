// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: Windows native handle mutation primitives for the OpenH264 safe-file layer.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Unity.FoxgloveSDK.Editor
{
    internal static partial class OpenH264WindowsNativeApi
    {
        /// <summary>Renames a file relative to an already opened destination directory.</summary>
        internal static bool TryRenameRelative(
            SafeFileHandle source,
            SafeFileHandle destinationDirectory,
            string name,
            bool replaceExisting,
            out string reason)
        {
            reason = "";
            if (!IsSimpleName(name, out reason))
                return false;

            var nameBytes = Encoding.Unicode.GetBytes(name);
            var rootOffset = IntPtr.Size == 8 ? 8 : 4;
            var lengthOffset = rootOffset + IntPtr.Size;
            var nameOffset = lengthOffset + 4;
            var bufferSize = nameOffset + nameBytes.Length;
            var buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                Marshal.WriteByte(buffer, 0, (byte)(replaceExisting ? 1 : 0));
                Marshal.WriteIntPtr(buffer, rootOffset, destinationDirectory.DangerousGetHandle());
                Marshal.WriteInt32(buffer, lengthOffset, nameBytes.Length);
                Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, nameOffset), nameBytes.Length);
                var status = NtSetInformationFile(
                    source,
                    out _,
                    buffer,
                    (uint)bufferSize,
                    FileRenameInformation);
                if (status >= 0)
                    return true;

                reason = $"Native relative rename failed with NTSTATUS 0x{status:X8}.";
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>Marks an opened file for deletion without resolving a path string.</summary>
        internal static bool TryDelete(SafeFileHandle file, out string reason)
        {
            reason = "";
            var disposition = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(disposition, 0, 1);
                if (SetFileInformationByHandle(file, FileDispositionInfo, disposition, 1))
                    return true;

                reason = "Handle delete failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(disposition);
            }
        }

        /// <summary>Writes and flushes bytes through an already opened file handle.</summary>
        internal static bool TryWriteAllBytes(
            SafeFileHandle file,
            byte[] bytes,
            out string reason)
        {
            reason = "";
            if (!SetFilePointerEx(file, 0, out _, FileBegin) || !SetEndOfFile(file))
            {
                reason = "Handle truncate failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }

            if (!WriteFile(file, bytes, (uint)bytes.Length, out var written, IntPtr.Zero)
                || written != bytes.Length)
            {
                reason = "Handle write failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }

            if (!FlushFileBuffers(file))
            {
                reason = "Handle flush failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }

            return true;
        }

        /// <summary>Copies a bounded stream into an opened file handle and flushes it.</summary>
        internal static bool TryWriteStream(
            SafeFileHandle file,
            Stream source,
            long maxBytes,
            CancellationToken cancellationToken,
            out long bytesWritten,
            out string reason)
        {
            bytesWritten = 0;
            reason = "";
            if (source == null || maxBytes < 0)
            {
                reason = "The safe-file stream input is invalid.";
                return false;
            }

            if (!SetFilePointerEx(file, 0, out _, FileBegin) || !SetEndOfFile(file))
            {
                reason = "Handle truncate failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }

            var buffer = new byte[81920];
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).GetAwaiter().GetResult();
                    if (read == 0)
                        break;
                    if (bytesWritten + read > maxBytes)
                    {
                        reason = "Safe-file stream input exceeded its configured bound.";
                        return false;
                    }

                    if (!WriteFile(file, buffer, (uint)read, out var written, IntPtr.Zero)
                        || written != read)
                    {
                        reason = "Handle write failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                        return false;
                    }

                    bytesWritten += written;
                }
            }
            catch (OperationCanceledException)
            {
                reason = "Safe-file stream input was cancelled.";
                return false;
            }

            if (!FlushFileBuffers(file))
            {
                reason = "Handle flush failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }

            return true;
        }

        /// <summary>Reads a bounded file handle into memory for a parent-owned process transfer.</summary>
        internal static bool TryReadAllBytes(
            SafeFileHandle file,
            int maxBytes,
            out byte[] bytes,
            out string reason)
        {
            bytes = Array.Empty<byte>();
            reason = "";
            if (maxBytes < 0)
            {
                reason = "The safe-file read bound is invalid.";
                return false;
            }

            if (!SetFilePointerEx(file, 0, out _, FileBegin))
            {
                reason = "Handle seek failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                return false;
            }

            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                while (true)
                {
                    if (!ReadFile(file, buffer, (uint)buffer.Length, out var read, IntPtr.Zero))
                    {
                        reason = "Handle read failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
                        return false;
                    }
                    if (read == 0)
                        break;
                    if (output.Length + read > maxBytes)
                    {
                        reason = "Safe-file read exceeded its configured bound.";
                        return false;
                    }

                    output.Write(buffer, 0, (int)read);
                }

                bytes = output.ToArray();
                return true;
            }
        }
    }
}
