// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: SHA256 verification for explicit OpenH264 native artifact installs.

using System;
using System.IO;
using System.Security.Cryptography;

namespace Unity.FoxgloveSDK.Editor
{
    public static class OpenH264ArtifactHashVerifier
    {
        public static bool TryVerifySha256(
            string path,
            string expectedSha256,
            string artifactLabel,
            out string actualSha256,
            out string error)
        {
            actualSha256 = "";
            error = "";

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                error = $"{artifactLabel ?? "OpenH264 artifact"} was not found for SHA256 verification: {path}";
                return false;
            }

            if (!IsSha256Hex(expectedSha256))
            {
                error = $"{artifactLabel ?? "OpenH264 artifact"} expected SHA256 is not a valid 64-character hex value.";
                return false;
            }

            try
            {
                actualSha256 = ComputeSha256(path);
            }
            catch (Exception ex)
            {
                error = $"{artifactLabel ?? "OpenH264 artifact"} SHA256 verification failed: {ex.Message}";
                return false;
            }

            if (string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                return true;

            error = $"{artifactLabel ?? "OpenH264 artifact"} SHA256 mismatch. "
                    + $"Expected {expectedSha256.ToUpperInvariant()}, actual {actualSha256}.";
            return false;
        }

        public static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
                return ToUpperHex(sha256.ComputeHash(stream));
        }

        private static bool IsSha256Hex(string value)
        {
            if (value == null || value.Length != 64)
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9')
                      || (c >= 'a' && c <= 'f')
                      || (c >= 'A' && c <= 'F')))
                    return false;
            }

            return true;
        }

        private static string ToUpperHex(byte[] bytes)
        {
            const string hex = "0123456789ABCDEF";
            var chars = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                chars[i * 2] = hex[b >> 4];
                chars[(i * 2) + 1] = hex[b & 0x0F];
            }

            return new string(chars);
        }
    }

    internal static class OpenH264ArtifactPairPublisher
    {
        internal static void Publish(
            string stagedDllPath,
            string stagedHelperPath,
            string finalDllPath,
            string finalHelperPath,
            Action<string, string> moveFile = null,
            Action<string> deleteFile = null,
            Func<bool> validateDestination = null)
        {
            if (moveFile == null)
                moveFile = File.Move;
            if (deleteFile == null)
                deleteFile = File.Delete;

            var dllBackupPath = finalDllPath + ".bak";
            var helperBackupPath = finalHelperPath + ".bak";
            var originalDllMoved = false;
            var originalHelperMoved = false;
            var installedDll = false;
            var installedHelper = false;

            ValidateDestination(validateDestination);
            DeleteFileIfPresent(dllBackupPath, deleteFile);
            DeleteFileIfPresent(helperBackupPath, deleteFile);
            try
            {
                ValidateDestination(validateDestination);
                MoveOriginalIfPresent(
                    finalDllPath,
                    dllBackupPath,
                    moveFile,
                    validateDestination,
                    ref originalDllMoved);
                ValidateDestination(validateDestination);
                MoveOriginalIfPresent(
                    finalHelperPath,
                    helperBackupPath,
                    moveFile,
                    validateDestination,
                    ref originalHelperMoved);

                ValidateDestination(validateDestination);
                moveFile(stagedDllPath, finalDllPath);
                installedDll = true;
                ValidateDestination(validateDestination);
                moveFile(stagedHelperPath, finalHelperPath);
                installedHelper = true;

                ValidateDestination(validateDestination);
                DeleteFileIfPresent(dllBackupPath, deleteFile);
                DeleteFileIfPresent(helperBackupPath, deleteFile);
            }
            catch
            {
                if (installedHelper)
                    DeleteFileIfPresent(finalHelperPath, deleteFile);
                if (installedDll)
                    DeleteFileIfPresent(finalDllPath, deleteFile);
                if (originalHelperMoved)
                    RestoreOriginal(
                        helperBackupPath,
                        finalHelperPath,
                        moveFile,
                        deleteFile);
                if (originalDllMoved)
                    RestoreOriginal(
                        dllBackupPath,
                        finalDllPath,
                        moveFile,
                        deleteFile);
                throw;
            }
        }

        private static void MoveOriginalIfPresent(
            string originalPath,
            string backupPath,
            Action<string, string> moveFile,
            Func<bool> validateDestination,
            ref bool moved)
        {
            if (!File.Exists(originalPath))
                return;

            ValidateDestination(validateDestination);
            moveFile(originalPath, backupPath);
            moved = true;
        }

        private static void ValidateDestination(Func<bool> validateDestination)
        {
            if (validateDestination != null && !validateDestination())
                throw new IOException("OpenH264 install destination changed during publication.");
        }

        private static void RestoreOriginal(
            string backupPath,
            string destinationPath,
            Action<string, string> moveFile,
            Action<string> deleteFile)
        {
            if (!File.Exists(backupPath))
                return;

            try
            {
                DeleteFileIfPresent(destinationPath, deleteFile);
                moveFile(backupPath, destinationPath);
            }
            catch
            {
            }
        }

        private static void DeleteFileIfPresent(
            string path,
            Action<string> deleteFile)
        {
            if (!File.Exists(path))
                return;

            try
            {
                deleteFile(path);
            }
            catch
            {
            }
        }
    }
}
