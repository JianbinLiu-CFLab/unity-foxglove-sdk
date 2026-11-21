// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: Verified Windows directory handles for OpenH264 transactions.

using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Owns a verified local Windows directory handle.</summary>
    internal sealed class OpenH264WindowsSafeDirectory : IDisposable
    {
        private SafeFileHandle _handle;

        /// <summary>Wraps an already verified directory handle.</summary>
        private OpenH264WindowsSafeDirectory(
            SafeFileHandle handle,
            OpenH264WindowsFileIdentity identity)
        {
            _handle = handle;
            Identity = identity;
        }

        internal OpenH264WindowsFileIdentity Identity { get; }

        internal SafeFileHandle HandleForRename => _handle;

        /// <summary>Opens an existing install root for a safe transaction.</summary>
        internal static bool TryOpenExisting(
            string path,
            out OpenH264WindowsSafeDirectory directory,
            out string reason)
        {
            directory = null;
            if (!OpenH264WindowsNativeApi.TryOpenRoot(
                    path,
                    out var handle,
                    out var identity,
                    out reason))
                return false;

            directory = new OpenH264WindowsSafeDirectory(handle, identity);
            return true;
        }

        /// <summary>Creates a verified child directory.</summary>
        internal bool TryCreateDirectory(
            string name,
            out OpenH264WindowsSafeDirectory directory,
            out string reason)
        {
            directory = null;
            if (!OpenH264WindowsNativeApi.TryOpenRelativeDirectory(
                    _handle,
                    name,
                    createNew: true,
                    out var handle,
                    out var identity,
                    out reason))
                return false;

            directory = new OpenH264WindowsSafeDirectory(handle, identity);
            return true;
        }

        /// <summary>Opens a verified child directory.</summary>
        internal bool TryOpenDirectory(
            string name,
            out OpenH264WindowsSafeDirectory directory,
            out string reason)
        {
            directory = null;
            if (!OpenH264WindowsNativeApi.TryOpenRelativeDirectory(
                    _handle,
                    name,
                    createNew: false,
                    out var handle,
                    out var identity,
                    out reason))
                return false;

            directory = new OpenH264WindowsSafeDirectory(handle, identity);
            return true;
        }

        /// <summary>Opens a child directory and retains a handle that blocks replacement.</summary>
        internal bool TryOpenLockedDirectory(
            string name,
            out OpenH264WindowsSafeDirectory directory,
            out string reason)
        {
            directory = null;
            if (!OpenH264WindowsNativeApi.TryOpenRelativeLockedDirectory(
                    _handle,
                    name,
                    out var handle,
                    out var identity,
                    out reason))
                return false;

            directory = new OpenH264WindowsSafeDirectory(handle, identity);
            return true;
        }

        /// <summary>Opens a child directory for a handle-relative rename.</summary>
        internal bool TryOpenRenameDirectory(
            string name,
            out OpenH264WindowsSafeDirectory directory,
            out string reason)
        {
            directory = null;
            if (!OpenH264WindowsNativeApi.TryOpenRelativeRenameDirectory(
                    _handle,
                    name,
                    out var handle,
                    out var identity,
                    out reason))
                return false;

            directory = new OpenH264WindowsSafeDirectory(handle, identity);
            return true;
        }

        /// <summary>Creates a verified child file.</summary>
        internal bool TryCreateFile(
            string name,
            out OpenH264WindowsSafeFile file,
            out string reason)
        {
            file = null;
            if (!OpenH264WindowsNativeApi.TryOpenRelativeFile(
                    _handle,
                    name,
                    createNew: true,
                    out var handle,
                    out var identity,
                    out reason))
                return false;

            file = new OpenH264WindowsSafeFile(handle, identity);
            return true;
        }

        /// <summary>Opens an existing direct child file without resolving a path string.</summary>
        internal bool TryOpenFile(
            string name,
            out OpenH264WindowsSafeFile file,
            out string reason)
        {
            file = null;
            if (!OpenH264WindowsNativeApi.TryOpenRelativeFile(
                    _handle,
                    name,
                    createNew: false,
                    out var handle,
                    out var identity,
                    out reason))
                return false;

            file = new OpenH264WindowsSafeFile(handle, identity);
            return true;
        }

        /// <summary>Deletes an existing direct child file and reports missing files as success.</summary>
        internal bool TryDeleteFile(string name, out string reason)
        {
            reason = "";
            if (!TryOpenFile(name, out var file, out reason))
            {
                if (reason.IndexOf("0xC0000034", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    reason = "";
                    return true;
                }

                return false;
            }

            using (file)
                return file.Delete(out reason);
        }

        /// <summary>Opens a file below a verified child directory.</summary>
        internal bool TryOpenFile(
            string directoryName,
            string fileName,
            out OpenH264WindowsSafeFile file,
            out string reason)
        {
            file = null;
            if (!TryOpenDirectory(directoryName, out var directory, out reason))
                return false;

            using (directory)
            {
                if (!OpenH264WindowsNativeApi.TryOpenRelativeFile(
                        directory._handle,
                        fileName,
                        createNew: false,
                        out var handle,
                        out var identity,
                        out reason))
                    return false;

                file = new OpenH264WindowsSafeFile(handle, identity);
                return true;
            }
        }

        /// <summary>Releases the directory handle.</summary>
        public void Dispose()
        {
            _handle?.Dispose();
            _handle = null;
        }
    }
}
