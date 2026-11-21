// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Safe per-user OpenH264 install root selection.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public static class OpenH264InstallLocation
    {
        private const string EditorPrefsKey = "Unity2Foxglove.OpenH264.InstallRoot";

        public static string GetPreferredInstallRoot()
        {
            var saved = EditorPrefs.GetString(EditorPrefsKey, "");
            if (!string.IsNullOrWhiteSpace(saved))
                return saved;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                localAppData = Application.temporaryCachePath;

            return Path.Combine(localAppData, "Unity2Foxglove", "OpenH264");
        }

        public static void SavePreferredInstallRoot(string root)
        {
            if (!string.IsNullOrWhiteSpace(root))
                EditorPrefs.SetString(EditorPrefsKey, root);
        }

        public static string GetFinalDllPath(string root)
            => Path.Combine(GetVersionedDirectory(root), OpenH264OfficialBinaryManifest.DllFileName);

        public static string GetFinalHelperPath(string root)
            => Path.Combine(GetVersionedDirectory(root), OpenH264OfficialBinaryManifest.HelperFileName);

        public static string GetVersionedDirectory(string root)
            => Path.Combine(string.IsNullOrWhiteSpace(root) ? GetPreferredInstallRoot() : root, OpenH264OfficialBinaryManifest.Version);

        internal static bool TryAcquireInstallRootLock(
            string root,
            out IDisposable directoryLock,
            out string reason)
        {
            directoryLock = null;
            reason = "";
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                reason = "OpenH264 install directory locking is supported only on Windows.";
                return false;
            }

            SafeFileHandle rootHandle = null;
            try
            {
                var fullRoot = Path.GetFullPath(root);
                if (ContainsReparsePoint(fullRoot))
                {
                    reason = "OpenH264 install directories changed or contain a symlink or junction.";
                    return false;
                }
                Directory.CreateDirectory(fullRoot);
                rootHandle = OpenDirectoryHandle(fullRoot);
                if (rootHandle == null || rootHandle.IsInvalid)
                {
                    reason = "Could not lock the OpenH264 install directories.";
                    return false;
                }

                if (HasReparsePoint(rootHandle) || !FinalPathEquals(rootHandle, fullRoot))
                {
                    reason = "OpenH264 install directories changed or contain a symlink or junction.";
                    return false;
                }

                directoryLock = new InstallDirectoryLock(rootHandle);
                rootHandle = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = "Could not lock the OpenH264 install directories: " + ex.Message;
                return false;
            }
            finally
            {
                rootHandle?.Dispose();
            }
        }

        public static bool IsAllowedInstallRoot(string root, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(root))
            {
                reason = "Choose an OpenH264 install location.";
                return false;
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
            }
            catch (Exception ex)
            {
                reason = "Install location is invalid: " + ex.Message;
                return false;
            }

            if (ContainsReparsePoint(fullRoot))
            {
                reason = "Choose an install location that does not traverse a symlink or junction.";
                return false;
            }

            if (ContainsReparsePointTree(GetVersionedDirectory(fullRoot)))
            {
                reason = "Choose an install location whose versioned directory has no symlink or junction.";
                return false;
            }

            foreach (var protectedFolder in new[]
            {
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.SystemX86,
                Environment.SpecialFolder.CommonApplicationData
            })
            {
                if (IsUnderSpecialFolder(fullRoot, protectedFolder))
                {
                    reason = "Choose a per-user folder instead of protected system or application folders.";
                    return false;
                }
            }

            var editorRoot = Path.GetDirectoryName(EditorApplication.applicationPath);
            if (IsSameOrChild(fullRoot, editorRoot))
            {
                reason = "Choose a per-user folder outside the Unity Editor installation.";
                return false;
            }

            var projectRoot = GetProjectRoot();
            if (IsSameOrChild(fullRoot, projectRoot))
            {
                reason = "Choose a cache folder outside the Unity project.";
                return false;
            }

            foreach (var projectChild in new[] { "Assets", "Packages", "ProjectSettings", ".git" })
            {
                if (IsSameOrChild(fullRoot, Path.Combine(projectRoot, projectChild)))
                {
                    reason = "Choose a cache folder outside the Unity project, not " + projectChild + ".";
                    return false;
                }
            }

            return true;
        }

        private static string GetProjectRoot()
        {
            var assets = Application.dataPath;
            return string.IsNullOrEmpty(assets) ? "" : Directory.GetParent(assets)?.FullName ?? "";
        }

        private static bool IsUnderSpecialFolder(string path, Environment.SpecialFolder folder)
        {
            var special = Environment.GetFolderPath(folder);
            return !string.IsNullOrWhiteSpace(special) && IsSameOrChild(path, special);
        }

        private static bool ContainsReparsePoint(string path)
        {
            try
            {
                for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
                {
                    if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                        return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private static bool ContainsReparsePointTree(string path)
        {
            if (ContainsReparsePoint(path))
                return true;

            try
            {
                var root = new DirectoryInfo(path);
                if (!root.Exists)
                    return false;

                var pending = new Stack<DirectoryInfo>();
                pending.Push(root);
                while (pending.Count > 0)
                {
                    foreach (var entry in pending.Pop().EnumerateFileSystemInfos())
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                            return true;
                        if (entry is DirectoryInfo directory)
                            pending.Push(directory);
                    }
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private static bool IsSameOrChild(string path, string parent)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parent))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var comparison = Application.platform == RuntimePlatform.WindowsEditor
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                return fullPath.Equals(fullParent, comparison)
                    || fullPath.StartsWith(fullParent + Path.DirectorySeparatorChar, comparison)
                    || fullPath.StartsWith(fullParent + Path.AltDirectorySeparatorChar, comparison);
            }
            catch
            {
                return false;
            }
        }

        private static SafeFileHandle OpenDirectoryHandle(string path)
        {
            return CreateFileW(
                path,
                GenericRead | DeleteAccess,
                FileShare.Read,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero);
        }

        private static bool HasReparsePoint(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandle(handle, out var information))
                return true;

            return (information.FileAttributes & FileAttributeReparsePoint) != 0;
        }

        private static bool FinalPathEquals(SafeFileHandle handle, string expected)
        {
            var capacity = 512;
            while (capacity <= 32768)
            {
                var buffer = new System.Text.StringBuilder(capacity);
                var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0)
                    return false;
                if (length < buffer.Capacity)
                {
                    var actual = buffer.ToString();
                    if (actual.StartsWith(@"\\?\", StringComparison.Ordinal))
                        actual = actual.Substring(4);
                    return string.Equals(
                        actual.TrimEnd('\\'),
                        expected.TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase);
                }

                capacity = checked((int)length + 1);
            }

            return false;
        }

        private sealed class InstallDirectoryLock : IDisposable
        {
            private SafeFileHandle _rootHandle;

            internal InstallDirectoryLock(SafeFileHandle rootHandle)
            {
                _rootHandle = rootHandle;
            }

            public void Dispose()
            {
                _rootHandle?.Dispose();
                _rootHandle = null;
            }
        }

        private const uint GenericRead = 0x80000000;
        private const uint DeleteAccess = 0x00010000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileAttributeReparsePoint = 0x0400;

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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation information);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle file,
            System.Text.StringBuilder filePath,
            uint filePathLength,
            uint flags);
    }
}
