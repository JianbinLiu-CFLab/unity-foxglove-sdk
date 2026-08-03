// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Transactional source-package writer for the Phase181 static ROS2 interface package.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Editor
{
    public sealed class FoxRunRos2InterfaceRevisionRequiredException : InvalidOperationException
    {
        public FoxRunRos2InterfaceRevisionRequiredException(string message)
            : base(message)
        {
        }
    }

    public sealed class FoxRunRos2InterfaceInvalidLockException : InvalidOperationException
    {
        public FoxRunRos2InterfaceInvalidLockException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    public sealed class FoxRunRos2InterfacePackageWriteResult
    {
        internal FoxRunRos2InterfacePackageWriteResult(bool changed, FoxRunRos2InterfaceLock @lock)
        {
            Changed = changed;
            Lock = @lock;
        }

        public bool Changed { get; }
        public FoxRunRos2InterfaceLock Lock { get; }
    }

    /// <summary>
    /// Writes only generated source-package files. Native builds, typesupport,
    /// ros2cs output, Unity metadata generation, and runtime loading remain
    /// outside this writer by design.
    /// </summary>
    public static class FoxRunRos2InterfacePackageWriter
    {
        private const string LockRelativePath = "RuntimeSupport/foxrun-ros2-interface-lock.json";

        public static FoxRunRos2InterfacePackageWriteResult Generate(
            string repoRoot,
            string packageRoot,
            FoxRunGenerationModel model,
            string nextRevision = null,
            Func<bool> isCancellationRequested = null,
            Action beforeCommit = null)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
                throw new ArgumentException("Repository root is required.", nameof(repoRoot));
            if (string.IsNullOrWhiteSpace(packageRoot))
                throw new ArgumentException("Source package root is required.", nameof(packageRoot));
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ThrowIfCancelled(isCancellationRequested);
            var currentLock = ReadCurrentLock(packageRoot);
            var requestedRevision = NormalizeRequestedRevision(nextRevision, currentLock);
            var currentPackageName = currentLock?.RosPackageName
                                     ?? FoxRunRos2InterfaceIdentity.DefaultRosPackageName;
            var currentRender = FoxRunRos2InterfacePackageRenderer.Render(model, currentPackageName);
            if (!currentRender.HasCustomContracts)
                throw new FoxRunRos2InterfaceRenderException("No custom FoxRun ROS2 DTO contracts require a static interface package.");

            FoxRunRos2InterfaceRenderedPackage render;
            if (currentLock == null)
            {
                render = requestedRevision == null
                    ? currentRender
                    : FoxRunRos2InterfacePackageRenderer.Render(model, requestedRevision);
            }
            else if (requestedRevision == null)
            {
                if (!string.Equals(currentLock.InterfaceDigest, currentRender.InterfaceDigest, StringComparison.Ordinal))
                {
                    throw new FoxRunRos2InterfaceRevisionRequiredException(
                        "Custom DTO schema changed under the locked ROS package identity. Generate an explicit next revision before replacing any source file.");
                }
                render = currentRender;
            }
            else
            {
                if (string.Equals(currentLock.InterfaceDigest, currentRender.InterfaceDigest, StringComparison.Ordinal))
                {
                    throw new FoxRunRos2InterfaceRevisionRequiredException(
                        "An explicit next interface revision is allowed only for a wire-changing custom DTO schema.");
                }
                render = FoxRunRos2InterfacePackageRenderer.Render(model, requestedRevision);
            }

            ValidateRender(render);
            ThrowIfCancelled(isCancellationRequested);
            var scratchRoot = Path.Combine(
                Path.GetFullPath(repoRoot),
                "build",
                "phase181",
                "interface-generation",
                Guid.NewGuid().ToString("N"));
            try
            {
                StageRender(scratchRoot, render);
                ThrowIfCancelled(isCancellationRequested);
                beforeCommit?.Invoke();
                ThrowIfCancelled(isCancellationRequested);
                var changed = CommitAtomically(packageRoot, render, isCancellationRequested);
                return new FoxRunRos2InterfacePackageWriteResult(changed, render.Lock);
            }
            finally
            {
                TryDeleteDirectory(scratchRoot);
            }
        }

        private static string NormalizeRequestedRevision(string nextRevision, FoxRunRos2InterfaceLock currentLock)
        {
            if (string.IsNullOrWhiteSpace(nextRevision))
                return null;
            if (!FoxRunRos2InterfaceIdentity.TryParseRosPackageRevision(nextRevision, out var requestedNumber))
            {
                throw new FoxRunRos2InterfaceRevisionRequiredException(
                    "The explicit next revision must use unity2foxglove_foxrun_interfaces_vN.");
            }

            if (currentLock == null)
            {
                if (requestedNumber != 1)
                    throw new FoxRunRos2InterfaceRevisionRequiredException("The first generated interface package must be revision v1.");
                return nextRevision;
            }

            if (requestedNumber != currentLock.InterfaceRevision + 1
                || !string.Equals(
                    nextRevision,
                    FoxRunRos2InterfaceIdentity.BuildRosPackageName(currentLock.RosPackageName, requestedNumber),
                    StringComparison.Ordinal))
            {
                throw new FoxRunRos2InterfaceRevisionRequiredException(
                    "The next interface revision must preserve the locked package stem and be exactly "
                    + FoxRunRos2InterfaceIdentity.BuildRosPackageName(currentLock.RosPackageName, currentLock.InterfaceRevision + 1) + ".");
            }
            return nextRevision;
        }

        private static FoxRunRos2InterfaceLock ReadCurrentLock(string packageRoot)
        {
            if (!Directory.Exists(packageRoot))
                return null;

            var lockPath = GetPath(packageRoot, LockRelativePath);
            if (!File.Exists(lockPath))
            {
                if (Directory.EnumerateFileSystemEntries(packageRoot).Any())
                {
                    throw new FoxRunRos2InterfaceInvalidLockException(
                        "The existing static interface package has no lock and cannot be overwritten.");
                }
                return null;
            }

            try
            {
                return FoxRunRos2InterfaceLock.Parse(File.ReadAllText(lockPath));
            }
            catch (Exception exception) when (exception is FormatException || exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new FoxRunRos2InterfaceInvalidLockException(
                    "The existing static interface package lock is malformed; generation stopped before replacement.",
                    exception);
            }
        }

        private static void ValidateRender(FoxRunRos2InterfaceRenderedPackage render)
        {
            if (render == null || !render.HasCustomContracts || render.Lock == null)
                throw new FoxRunRos2InterfaceRenderException("A non-empty custom interface render is required.");

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in render.Files)
            {
                if (file == null || !unique.Add(file.RelativePath))
                    throw new FoxRunRos2InterfaceRenderException("Rendered interface package contains duplicate paths.");
                if (file.Bytes.Length >= 3 && file.Bytes[0] == 0xef && file.Bytes[1] == 0xbb && file.Bytes[2] == 0xbf)
                    throw new FoxRunRos2InterfaceRenderException("Rendered source files must use UTF-8 without a BOM.");
            }

            var lockFile = render.Files.SingleOrDefault(file => string.Equals(file.RelativePath, LockRelativePath, StringComparison.Ordinal));
            if (lockFile == null || !string.Equals(
                    FoxRunRos2InterfaceLock.Parse(lockFile.Text).InterfaceDigest,
                    render.InterfaceDigest,
                    StringComparison.Ordinal))
            {
                throw new FoxRunRos2InterfaceRenderException("Rendered lock does not match the interface digest.");
            }
        }

        private static void StageRender(string scratchRoot, FoxRunRos2InterfaceRenderedPackage render)
        {
            foreach (var file in render.Files)
                WriteBytes(GetPath(scratchRoot, file.RelativePath), file.Bytes);
        }

        private static bool CommitAtomically(
            string packageRoot,
            FoxRunRos2InterfaceRenderedPackage render,
            Func<bool> isCancellationRequested)
        {
            var newFiles = render.Files.ToDictionary(file => file.RelativePath, file => file.Bytes, StringComparer.Ordinal);
            var touched = new HashSet<string>(newFiles.Keys, StringComparer.Ordinal);
            if (Directory.Exists(Path.Combine(packageRoot, "Ros2Package~", "msg")))
            {
                foreach (var oldMessage in Directory.GetFiles(Path.Combine(packageRoot, "Ros2Package~", "msg"), "*.msg", SearchOption.TopDirectoryOnly))
                    touched.Add("Ros2Package~/msg/" + Path.GetFileName(oldMessage));
            }

            var backup = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var relativePath in touched)
            {
                var path = GetPath(packageRoot, relativePath);
                if (File.Exists(path))
                    backup[relativePath] = File.ReadAllBytes(path);
            }

            var hasChanges = touched.Any(relativePath => !FileMatches(GetPath(packageRoot, relativePath), newFiles.TryGetValue(relativePath, out var expected) ? expected : null));
            if (!hasChanges)
                return false;

            try
            {
                foreach (var file in render.Files)
                {
                    ThrowIfCancelled(isCancellationRequested);
                    WriteBytesAtomically(GetPath(packageRoot, file.RelativePath), file.Bytes);
                }
                foreach (var obsolete in touched.Where(path => !newFiles.ContainsKey(path)))
                {
                    ThrowIfCancelled(isCancellationRequested);
                    var path = GetPath(packageRoot, obsolete);
                    if (File.Exists(path))
                        File.Delete(path);
                }
                return true;
            }
            catch
            {
                RestoreBackup(packageRoot, touched, backup);
                throw;
            }
        }

        private static void RestoreBackup(
            string packageRoot,
            IEnumerable<string> touched,
            IReadOnlyDictionary<string, byte[]> backup)
        {
            foreach (var relativePath in touched)
            {
                var path = GetPath(packageRoot, relativePath);
                if (backup.TryGetValue(relativePath, out var bytes))
                {
                    WriteBytesAtomically(path, bytes);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static bool FileMatches(string path, byte[] expected)
        {
            if (expected == null)
                return !File.Exists(path);
            if (!File.Exists(path))
                return false;
            var actual = File.ReadAllBytes(path);
            return actual.SequenceEqual(expected);
        }

        private static string GetPath(string root, string relativePath)
            => Path.Combine(root, FoxRunRos2InterfaceDigest.NormalizeRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar));

        private static void WriteBytes(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes ?? Array.Empty<byte>());
        }

        private static void WriteBytesAtomically(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tempPath = path + ".phase181-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, bytes ?? Array.Empty<byte>());
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(tempPath, path, overwrite: true);
                    }
                    catch (IOException)
                    {
                        File.Copy(tempPath, path, overwrite: true);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void ThrowIfCancelled(Func<bool> isCancellationRequested)
        {
            if (isCancellationRequested != null && isCancellationRequested())
                throw new OperationCanceledException("FoxRun ROS2 interface generation was cancelled before replacement.");
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
