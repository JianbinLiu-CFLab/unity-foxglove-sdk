// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Explicit user-triggered Cisco OpenH264 binary downloader.

using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using UnityEditor.PackageManager;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Unity.FoxgloveSDK.Editor
{
    public readonly struct OpenH264InstallResult
    {
        public OpenH264InstallResult(bool success, string helperPath, string dllPath, string errorMessage)
        {
            Success = success;
            HelperPath = helperPath ?? "";
            DllPath = dllPath ?? "";
            ErrorMessage = errorMessage ?? "";
        }

        public bool Success { get; }
        public string HelperPath { get; }
        public string DllPath { get; }
        public string ErrorMessage { get; }
    }

    public static partial class OpenH264OfficialBinaryInstaller
    {
        /// <remarks>Phase192 source sections: Download.cs, Helper.cs, Process.cs</remarks>
        private const int DownloadBodyTimeoutMs = 60000;

        private enum InstallPhase
        {
            Prepare,
            Download,
            Decompress,
            BuildHelper,
            Publish
        }

        public static bool IsAutomaticInstallSupported => IsWindowsEditor;

        public static OpenH264InstallResult Install(string installRoot)
            => Install(installRoot, GetPackageRoot());

        internal static OpenH264InstallResult Install(string installRoot, string packageRoot)
        {
            if (!IsWindowsEditor)
                return Fail("Automatic OpenH264 installation is supported only in the Windows Unity Editor.");

            if (!OpenH264InstallLocation.IsAllowedInstallRoot(installRoot, out var reason))
                return Fail(reason);

            var versionDir = OpenH264InstallLocation.GetVersionedDirectory(installRoot);
            var compressedPath = Path.Combine(versionDir, OpenH264OfficialBinaryManifest.AssetName);
            var compressedDownloadPath = compressedPath + ".download";
            var finalDllPath = OpenH264InstallLocation.GetFinalDllPath(installRoot);
            var finalHelperPath = OpenH264InstallLocation.GetFinalHelperPath(installRoot);
            var tempDll = finalDllPath + ".tmp";
            var tempHelper = finalHelperPath + ".tmp";
            Func<bool> validateDestination = () =>
                OpenH264InstallLocation.IsAllowedInstallRoot(installRoot, out _);
            bool TryDelete(string path, bool warnOnFailure = false)
                => OpenH264OfficialBinaryInstaller.TryDelete(path, warnOnFailure, validateDestination);
            IDisposable installRootLock = null;
            try
            {
                EnsureInstallRootStillAllowed(installRoot, InstallPhase.Prepare);
                if (!OpenH264InstallLocation.TryAcquireInstallRootLock(
                        installRoot,
                        out installRootLock,
                        out var rootLockError))
                    return Fail(rootLockError);
                Directory.CreateDirectory(versionDir);
                EnsureInstallRootStillAllowed(installRoot, InstallPhase.Prepare);
                TryDelete(compressedDownloadPath);
                TryDelete(tempDll);
                TryDelete(tempHelper);
                EnsureInstallRootStillAllowed(installRoot, InstallPhase.Download);
                DownloadFile(OpenH264OfficialBinaryManifest.DownloadUrl, compressedDownloadPath);
                if (!OpenH264ArtifactHashVerifier.TryVerifySha256(
                        compressedDownloadPath,
                        OpenH264OfficialBinaryManifest.CompressedAssetSha256,
                        "OpenH264 compressed asset",
                        out _,
                        out var compressedHashError))
                {
                    TryDelete(compressedDownloadPath);
                    return Fail(
                        compressedHashError
                        + "\nManual fallback: open "
                        + OpenH264OfficialBinaryManifest.ReleasePageUrl
                        + ", download "
                        + OpenH264OfficialBinaryManifest.AssetName
                        + ", and compare SHA256 before installing.");
                }

                EnsureInstallRootStillAllowed(installRoot, InstallPhase.Decompress);
                if (File.Exists(compressedPath))
                {
                    EnsureInstallRootStillAllowed(installRoot, InstallPhase.Decompress);
                    File.Delete(compressedPath);
                }

                EnsureInstallRootStillAllowed(installRoot, InstallPhase.Decompress);
                File.Move(compressedDownloadPath, compressedPath);

                if (!TryDecompressBZip2(compressedPath, tempDll, validateDestination, out var decompressError))
                {
                    return Fail(
                        decompressError
                        + "\nManual fallback: download "
                        + OpenH264OfficialBinaryManifest.AssetName
                        + ", decompress it, then choose the resulting DLL with ...");
                }

                if (!OpenH264ArtifactHashVerifier.TryVerifySha256(
                        tempDll,
                        OpenH264OfficialBinaryManifest.DllSha256,
                        "OpenH264 DLL",
                        out _,
                        out var dllHashError))
                {
                    TryDelete(tempDll);
                    return Fail(
                        dllHashError
                        + "\nManual fallback: open "
                        + OpenH264OfficialBinaryManifest.ReleasePageUrl
                        + ", download and decompress the pinned asset, and compare SHA256 before installing.");
                }

                EnsureInstallRootStillAllowed(installRoot, InstallPhase.BuildHelper);
                if (!BuildHelperExecutable(
                        versionDir,
                        tempHelper,
                        packageRoot,
                        validateDestination,
                        out var buildError))
                {
                    TryDelete(tempDll);
                    TryDelete(tempHelper);
                    return Fail(buildError);
                }

                EnsureInstallRootStillAllowed(installRoot, InstallPhase.Publish);
                OpenH264ArtifactPairPublisher.Publish(
                    tempDll,
                    tempHelper,
                    finalDllPath,
                    finalHelperPath,
                    validateDestination: validateDestination);

                TryDelete(compressedPath, warnOnFailure: true);
                return new OpenH264InstallResult(true, finalHelperPath, finalDllPath, "");
            }
            catch (Exception ex)
            {
                TryDelete(compressedDownloadPath);
                TryDelete(tempDll);
                TryDelete(tempHelper);
                return Fail(
                    ex.Message
                    + "\nManual fallback: open "
                    + OpenH264OfficialBinaryManifest.ReleasePageUrl
                    + ", download the pinned "
                    + OpenH264OfficialBinaryManifest.AssetName
                    + ", then choose the DLL and a locally built helper with ....");
            }
            finally
            {
                installRootLock?.Dispose();
            }
        }
    }
}
