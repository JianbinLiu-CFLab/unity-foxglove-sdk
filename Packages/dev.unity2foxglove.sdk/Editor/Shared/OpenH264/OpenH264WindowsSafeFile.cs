// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: Verified Windows file handles for OpenH264 transactions.

using System;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Owns a verified local Windows file handle.</summary>
    internal sealed class OpenH264WindowsSafeFile : IDisposable
    {
        private SafeFileHandle _handle;

        /// <summary>Wraps an already verified file handle.</summary>
        internal OpenH264WindowsSafeFile(
            SafeFileHandle handle,
            OpenH264WindowsFileIdentity identity)
        {
            _handle = handle;
            Identity = identity;
        }

        internal OpenH264WindowsFileIdentity Identity { get; }

        /// <summary>Writes bytes through the verified handle.</summary>
        internal bool WriteAllBytes(byte[] bytes, out string reason)
            => OpenH264WindowsNativeApi.TryWriteAllBytes(_handle, bytes, out reason);

        /// <summary>Copies a bounded stream into this verified file.</summary>
        internal bool WriteFromStream(Stream source, long maxBytes, out long bytesWritten, out string reason)
            => WriteFromStream(source, maxBytes, CancellationToken.None, out bytesWritten, out reason);

        /// <summary>Copies a bounded cancellable stream into this verified file.</summary>
        internal bool WriteFromStream(
            Stream source,
            long maxBytes,
            CancellationToken cancellationToken,
            out long bytesWritten,
            out string reason)
            => OpenH264WindowsNativeApi.TryWriteStream(
                _handle,
                source,
                maxBytes,
                cancellationToken,
                out bytesWritten,
                out reason);

        /// <summary>Reads this verified file into a bounded parent-owned buffer.</summary>
        internal bool ReadAllBytes(int maxBytes, out byte[] bytes, out string reason)
            => OpenH264WindowsNativeApi.TryReadAllBytes(_handle, maxBytes, out bytes, out reason);

        /// <summary>Renames this file through the verified destination directory handle.</summary>
        internal bool RenameTo(
            OpenH264WindowsSafeDirectory destination,
            string name,
            out string reason)
            => RenameTo(destination, name, replaceExisting: true, out reason);

        /// <summary>Renames this file with an explicit destination replacement policy.</summary>
        internal bool RenameTo(
            OpenH264WindowsSafeDirectory destination,
            string name,
            bool replaceExisting,
            out string reason)
            => OpenH264WindowsNativeApi.TryRenameRelative(
                _handle,
                destination.HandleForRename,
                name,
                replaceExisting,
                out reason);

        /// <summary>Deletes this file through its verified handle.</summary>
        internal bool Delete(out string reason)
            => OpenH264WindowsNativeApi.TryDelete(_handle, out reason);

        /// <summary>Releases the file handle.</summary>
        public void Dispose()
        {
            _handle?.Dispose();
            _handle = null;
        }
    }
}
