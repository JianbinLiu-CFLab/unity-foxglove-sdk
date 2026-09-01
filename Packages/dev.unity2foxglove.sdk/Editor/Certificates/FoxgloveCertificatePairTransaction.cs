// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Certificates
// Purpose: Commit the PFX and public certificate as one recoverable pair.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Stages two certificate artifacts in the destination directory and commits
    /// them with a rollback journal held in memory. A failed commit restores the
    /// exact pair that existed before generation started.
    /// </summary>
    internal sealed class FoxgloveCertificatePairTransaction : IDisposable
    {
        private readonly string _pfxPath;
        private readonly string _rootCaPath;
        private readonly string _pfxTempPath;
        private readonly string _rootCaTempPath;
        private readonly string _pfxBackupPath;
        private readonly string _rootCaBackupPath;
        private bool _pfxOriginalMoved;
        private bool _rootCaOriginalMoved;
        private bool _pfxInstalled;
        private bool _rootCaInstalled;
        private bool _committed;
        private bool _disposed;

        private FoxgloveCertificatePairTransaction(string pfxPath, string rootCaPath)
        {
            _pfxPath = Path.GetFullPath(pfxPath ?? throw new ArgumentNullException(nameof(pfxPath)));
            _rootCaPath = Path.GetFullPath(rootCaPath ?? throw new ArgumentNullException(nameof(rootCaPath)));

            var pfxDirectory = Path.GetDirectoryName(_pfxPath);
            var rootDirectory = Path.GetDirectoryName(_rootCaPath);
            if (!string.Equals(pfxDirectory, rootDirectory, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Certificate pair destinations must share a directory.");

            var token = Guid.NewGuid().ToString("N");
            _pfxTempPath = _pfxPath + "." + token + ".tmp";
            _rootCaTempPath = _rootCaPath + "." + token + ".tmp";
            _pfxBackupPath = _pfxPath + "." + token + ".bak";
            _rootCaBackupPath = _rootCaPath + "." + token + ".bak";
        }

        public string PfxTempPath => _pfxTempPath;
        public string RootCaTempPath => _rootCaTempPath;

        public static FoxgloveCertificatePairTransaction Begin(string pfxPath, string rootCaPath)
        {
            var transaction = new FoxgloveCertificatePairTransaction(pfxPath, rootCaPath);
            var directory = Path.GetDirectoryName(transaction._pfxPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            return transaction;
        }

        public void Commit()
        {
            ThrowIfDisposed();
            ValidateStagedFile(_pfxTempPath, "PFX");
            ValidateStagedFile(_rootCaTempPath, "root certificate");

            try
            {
                MoveOriginalToBackup(_pfxPath, _pfxBackupPath, ref _pfxOriginalMoved);
                MoveOriginalToBackup(_rootCaPath, _rootCaBackupPath, ref _rootCaOriginalMoved);

                Install(_pfxTempPath, _pfxPath, ref _pfxInstalled);
                Install(_rootCaTempPath, _rootCaPath, ref _rootCaInstalled);
                _committed = true;

                TryDelete(_pfxBackupPath);
                TryDelete(_rootCaBackupPath);
            }
            catch
            {
                // RestoreOriginalPair is deliberately best effort. It records
                // incomplete state in the latches so Dispose can retry without
                // replacing the exception raised by the failed commit.
                try
                {
                    RestoreOriginalPair();
                }
                catch
                {
                    // Keep the commit failure as the authoritative diagnostic
                    // even if an unexpected filesystem implementation error
                    // escapes the best-effort restore helpers.
                    ReportCleanupFailure("restore");
                }
                throw;
            }
        }

        public void Dispose()
        {
            // A failed cleanup pass still owns recoverable artifacts. Keep the
            // public Dispose operation idempotent while allowing a later pass
            // to retry after a transient file-system lock is released.
            if (_disposed && !HasPendingCleanup())
                return;

            try
            {
                if (!_committed)
                {
                    try
                    {
                        RestoreOriginalPair();
                    }
                    catch
                    {
                        ReportCleanupFailure("restore");
                    }
                }

                TryDelete(_pfxTempPath);
                TryDelete(_rootCaTempPath);

                // A backup is still the only copy of an original while its
                // latch is set. Retain it when a transient filesystem failure
                // prevented restoration; deleting it would make recovery
                // impossible. Successful commits may always discard backups.
                if (_committed || !_pfxOriginalMoved)
                    TryDelete(_pfxBackupPath);
                if (_committed || !_rootCaOriginalMoved)
                    TryDelete(_rootCaBackupPath);
            }
            finally
            {
                _disposed = true;
            }
        }

        private bool HasPendingCleanup()
        {
            return _pfxInstalled
                   || _rootCaInstalled
                   || _pfxOriginalMoved
                   || _rootCaOriginalMoved
                   || File.Exists(_pfxTempPath)
                   || File.Exists(_rootCaTempPath)
                   || File.Exists(_pfxBackupPath)
                   || File.Exists(_rootCaBackupPath);
        }

        private void RestoreOriginalPair()
        {
            // Keep each latch set until its operation has actually completed.
            // This makes a subsequent cleanup pass safe after a transient
            // delete/move failure.
            if (_pfxInstalled && TryDelete(_pfxPath))
                _pfxInstalled = false;

            if (_rootCaInstalled && TryDelete(_rootCaPath))
                _rootCaInstalled = false;

            // Do not attempt to move the backup while an installed artifact is
            // still present.  TryRestoreBackup performs its own delete check,
            // but a transient failure in the first delete above must not be
            // followed by a successful second delete that leaves the
            // installed latch stale.
            if (!_pfxInstalled
                && _pfxOriginalMoved
                && TryRestoreBackup(_pfxBackupPath, _pfxPath))
                _pfxOriginalMoved = false;

            if (!_rootCaInstalled
                && _rootCaOriginalMoved
                && TryRestoreBackup(_rootCaBackupPath, _rootCaPath))
                _rootCaOriginalMoved = false;
        }

        private static bool TryRestoreBackup(string backupPath, string destinationPath)
        {
            if (!File.Exists(backupPath))
                return false;

            // File.Move does not replace an existing destination on the Unity
            // profiles supported by this package. Remove a partially installed
            // artifact first, and leave the latch set if that removal fails.
            if (!TryDelete(destinationPath))
                return false;

            try
            {
                File.Move(backupPath, destinationPath);
                return true;
            }
            catch
            {
                ReportCleanupFailure("move");
                return false;
            }
        }

        private static void MoveOriginalToBackup(string originalPath, string backupPath, ref bool moved)
        {
            if (!File.Exists(originalPath))
                return;

            File.Move(originalPath, backupPath);
            moved = true;
        }

        private static void Install(string stagedPath, string destinationPath, ref bool installed)
        {
            File.Move(stagedPath, destinationPath);
            installed = true;
        }

        private static void ValidateStagedFile(string path, string label)
        {
            if (!File.Exists(path))
                throw new InvalidOperationException($"Staged {label} artifact is missing: {path}");

            if (new FileInfo(path).Length == 0)
                throw new InvalidOperationException($"Staged {label} artifact is empty: {path}");
        }

        private static bool TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return true;

            try
            {
                File.Delete(path);
                return !File.Exists(path);
            }
            catch
            {
                ReportCleanupFailure("delete");
                return false;
            }
        }

        private static void ReportCleanupFailure(string operation)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogWarning("[Foxglove] Certificate transaction could not " + operation + " an artifact during cleanup.");
#else
            System.Diagnostics.Debug.WriteLine(
                "[Foxglove] Certificate transaction could not " + operation + " an artifact during cleanup.");
#endif
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FoxgloveCertificatePairTransaction));
        }
    }
}
