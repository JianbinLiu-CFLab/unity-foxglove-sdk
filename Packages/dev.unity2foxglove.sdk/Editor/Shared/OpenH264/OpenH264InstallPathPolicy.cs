// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: Pure filesystem policy for OpenH264 install roots.

using System;
using System.Collections.Generic;
using System.IO;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class OpenH264InstallPathPolicy
    {
        private static readonly string[] ProjectChildren =
        {
            "Assets",
            "Packages",
            "ProjectSettings",
            ".git"
        };

        internal static bool IsAllowedInstallRoot(
            string root,
            string version,
            string projectRoot,
            string editorRoot,
            IReadOnlyList<string> protectedRoots,
            StringComparison pathComparison,
            out string reason,
            IReadOnlyList<string> projectChildren = null)
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

            if (ContainsReparsePointTree(Path.Combine(fullRoot, version)))
            {
                reason = "Choose an install location whose versioned directory has no symlink or junction.";
                return false;
            }

            if (protectedRoots != null)
            {
                foreach (var protectedRoot in protectedRoots)
                {
                    if (!string.IsNullOrWhiteSpace(protectedRoot)
                        && IsSameOrChild(fullRoot, protectedRoot, pathComparison))
                    {
                        reason = "Choose a per-user folder instead of protected system or application folders.";
                        return false;
                    }
                }
            }

            if (IsSameOrChild(fullRoot, editorRoot, pathComparison))
            {
                reason = "Choose a per-user folder outside the Unity Editor installation.";
                return false;
            }

            if (IsSameOrChild(fullRoot, projectRoot, pathComparison))
            {
                reason = "Choose a cache folder outside the Unity project.";
                return false;
            }

            foreach (var projectChild in projectChildren ?? ProjectChildren)
            {
                if (IsSameOrChild(fullRoot, Path.Combine(projectRoot, projectChild), pathComparison))
                {
                    reason = "Choose a cache folder outside the Unity project, not " + projectChild + ".";
                    return false;
                }
            }

            return true;
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

        private static bool IsSameOrChild(string path, string parent, StringComparison pathComparison)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parent))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return fullPath.Equals(fullParent, pathComparison)
                    || fullPath.StartsWith(fullParent + Path.DirectorySeparatorChar, pathComparison)
                    || fullPath.StartsWith(fullParent + Path.AltDirectorySeparatorChar, pathComparison);
            }
            catch
            {
                return false;
            }
        }
    }
}
