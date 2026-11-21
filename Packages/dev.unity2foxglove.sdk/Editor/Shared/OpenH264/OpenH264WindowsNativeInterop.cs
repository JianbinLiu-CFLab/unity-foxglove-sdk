// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: Windows native interop declarations for the OpenH264 safe-file layer.

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Unity.FoxgloveSDK.Editor
{
    internal static partial class OpenH264WindowsNativeApi
    {
        internal const int FileRenameInformation = 10;
        private const int FileDispositionInfo = 4;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint Delete = 0x00010000;
        private const uint Synchronize = 0x00100000;
        private const uint FileListDirectory = 0x00000001;
        private const uint FileAddSubdirectory = 0x00000004;
        private const uint FileTraverse = 0x00000020;
        private const uint FileReadAttributes = 0x00000080;
        private const uint FileDirectoryFile = 0x00000001;
        private const uint FileNonDirectoryFile = 0x00000040;
        private const uint FileOpenReparsePoint = 0x00200000;
        private const uint FileOpenForBackupIntent = 0x00004000;
        private const uint FileSynchronousIoNonAlert = 0x00000020;
        private const uint FileOpen = 0x00000001;
        private const uint FileCreate = 0x00000002;
        private const uint ShareReadWrite = 0x00000003;
        private const uint ShareAll = 0x00000007;
        private const uint FileAttributeReparsePoint = 0x0400;

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            internal ushort Length;
            internal ushort MaximumLength;
            internal IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ObjectAttributes
        {
            internal uint Length;
            internal IntPtr RootDirectory;
            internal IntPtr ObjectName;
            internal uint Attributes;
            internal IntPtr SecurityDescriptor;
            internal IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoStatusBlock
        {
            internal IntPtr Status;
            internal IntPtr Information;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            internal uint FileAttributes;
            internal uint CreationTimeLow;
            internal uint CreationTimeHigh;
            internal uint LastAccessTimeLow;
            internal uint LastAccessTimeHigh;
            internal uint LastWriteTimeLow;
            internal uint LastWriteTimeHigh;
            internal uint VolumeSerialNumber;
            internal uint FileSizeHigh;
            internal uint FileSizeLow;
            internal uint NumberOfLinks;
            internal uint FileIndexHigh;
            internal uint FileIndexLow;
        }

        /// <summary>Owns unmanaged UTF-16 storage for an NT object name.</summary>
        private sealed class NativeName : IDisposable
        {
            private readonly IntPtr _text;
            internal IntPtr Structure { get; }

            /// <summary>Allocates the native name structure used by NtCreateFile.</summary>
            internal NativeName(string value)
            {
                _text = Marshal.StringToHGlobalUni(value);
                Structure = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
                Marshal.StructureToPtr(
                    new UnicodeString
                    {
                        Length = checked((ushort)(value.Length * 2)),
                        MaximumLength = checked((ushort)(value.Length * 2 + 2)),
                        Buffer = _text
                    },
                    Structure,
                    false);
            }

            /// <summary>Releases the unmanaged name buffers.</summary>
            public void Dispose()
            {
                Marshal.FreeHGlobal(Structure);
                Marshal.FreeHGlobal(_text);
            }
        }

        [DllImport("ntdll.dll")]
        private static extern int NtCreateFile(
            out IntPtr fileHandle,
            uint desiredAccess,
            ref ObjectAttributes objectAttributes,
            out IoStatusBlock ioStatusBlock,
            IntPtr allocationSize,
            uint fileAttributes,
            uint shareAccess,
            uint createDisposition,
            uint createOptions,
            IntPtr eaBuffer,
            uint eaLength);

        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationFile(
            SafeFileHandle file,
            out IoStatusBlock ioStatusBlock,
            IntPtr fileInformation,
            uint length,
            int fileInformationClass);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation information);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle file,
            StringBuilder filePath,
            uint filePathLength,
            uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle file,
            int fileInformationClass,
            IntPtr fileInformation,
            uint bufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(
            SafeFileHandle file,
            byte[] buffer,
            uint numberOfBytesToWrite,
            out uint numberOfBytesWritten,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFileBuffers(SafeFileHandle file);
    }
}
