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
                RestoreOriginalPair();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            if (!_committed)
                RestoreOriginalPair();

            TryDelete(_pfxTempPath);
            TryDelete(_rootCaTempPath);
            TryDelete(_pfxBackupPath);
            TryDelete(_rootCaBackupPath);
            _disposed = true;
        }

        private void RestoreOriginalPair()
        {
            if (_pfxInstalled)
            {
                TryDelete(_pfxPath);
                _pfxInstalled = false;
            }

            if (_rootCaInstalled)
            {
                TryDelete(_rootCaPath);
                _rootCaInstalled = false;
            }

            if (_pfxOriginalMoved && File.Exists(_pfxBackupPath))
            {
                File.Move(_pfxBackupPath, _pfxPath);
                _pfxOriginalMoved = false;
            }

            if (_rootCaOriginalMoved && File.Exists(_rootCaBackupPath))
            {
                File.Move(_rootCaBackupPath, _rootCaPath);
                _rootCaOriginalMoved = false;
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

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Cleanup is best effort; the original operation's exception is authoritative.
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FoxgloveCertificatePairTransaction));
        }
    }
}
