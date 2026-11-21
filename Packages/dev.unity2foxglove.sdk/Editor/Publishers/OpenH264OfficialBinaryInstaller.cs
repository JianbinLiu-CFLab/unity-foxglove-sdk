// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Explicit user-triggered Cisco OpenH264 binary downloader.

using System;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

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
            var versionName = OpenH264OfficialBinaryManifest.Version;
            var compressedDownloadPath = Path.Combine(
                versionDir,
                OpenH264OfficialBinaryManifest.AssetName + ".download");
            var finalDllPath = OpenH264InstallLocation.GetFinalDllPath(installRoot);
            var finalHelperPath = OpenH264InstallLocation.GetFinalHelperPath(installRoot);
            var tempDll = finalDllPath + ".tmp";
            var tempHelper = finalHelperPath + ".tmp";
            var compressedDownloadName = Path.GetFileName(compressedDownloadPath);
            var tempDllName = Path.GetFileName(tempDll);
            Func<bool> validateDestination = () =>
                OpenH264InstallLocation.IsAllowedInstallRoot(installRoot, out _);
            bool TryDelete(string path, bool warnOnFailure = false)
                => OpenH264OfficialBinaryInstaller.TryDelete(path, warnOnFailure, validateDestination);
            OpenH264WindowsSafeDirectory safeRoot = null;
            OpenH264WindowsSafeDirectory safeVersion = null;
            try
            {
                EnsureInstallRootStillAllowed(installRoot, InstallPhase.Prepare);
                Directory.CreateDirectory(versionDir);
                EnsureInstallRootStillAllowed(installRoot, InstallPhase.Prepare);
                if (!OpenH264WindowsSafeDirectory.TryOpenExisting(
                        installRoot,
                        out safeRoot,
                        out var safeRootError))
                    return Fail(safeRootError);
                if (!safeRoot.TryOpenLockedDirectory(versionName, out safeVersion, out var safeVersionError))
                    return Fail(safeVersionError);
                if (!safeVersion.TryDeleteFile(compressedDownloadName, out var cleanupError))
                    return Fail(cleanupError);
                if (!safeVersion.TryDeleteFile(tempDllName, out cleanupError))
                    return Fail(cleanupError);
                TryDelete(tempHelper);
                EnsureInstallRootStillAllowed(installRoot, InstallPhase.Download);
                if (!safeVersion.TryCreateFile(
                        compressedDownloadName,
                        out var safeCompressedDownload,
                        out var safeDownloadError))
                    return Fail(safeDownloadError);
                using (safeCompressedDownload)
                {
                    DownloadFile(
                        OpenH264OfficialBinaryManifest.DownloadUrl,
                        safeCompressedDownload,
                        validateDestination);
                    if (!OpenH264ArtifactHashVerifier.TryVerifySha256(
                            safeCompressedDownload,
                            OpenH264OfficialBinaryManifest.CompressedAssetSha256,
                            "OpenH264 compressed asset",
                            out _,
                            out var compressedHashError))
                    {
                        return Fail(
                            compressedHashError
                            + "\nManual fallback: open "
                            + OpenH264OfficialBinaryManifest.ReleasePageUrl
                            + ", download "
                            + OpenH264OfficialBinaryManifest.AssetName
                            + ", and compare SHA256 before installing.");
                    }
                }

                EnsureInstallRootStillAllowed(installRoot, InstallPhase.Decompress);
                if (!safeVersion.TryOpenFile(compressedDownloadName, out var safeCompressed, out var safeCompressedError))
                    return Fail(safeCompressedError);
                using (safeCompressed)
                {
                    if (!safeVersion.TryCreateFile(tempDllName, out var safeTempDll, out var safeTempError))
                        return Fail(safeTempError);
                    using (safeTempDll)
                    {
                        if (!TryDecompressBZip2(safeCompressed, safeTempDll, validateDestination, out var decompressError))
                        {
                            return Fail(
                                decompressError
                                + "\nManual fallback: download "
                                + OpenH264OfficialBinaryManifest.AssetName
                                + ", decompress it, then choose the resulting DLL with ...");
                        }

                        if (!OpenH264ArtifactHashVerifier.TryVerifySha256(
                                safeTempDll,
                                OpenH264OfficialBinaryManifest.DllSha256,
                                "OpenH264 DLL",
                                out _,
                                out var dllHashError))
                        {
                            return Fail(
                                dllHashError
                                + "\nManual fallback: open "
                                + OpenH264OfficialBinaryManifest.ReleasePageUrl
                                + ", download and decompress the pinned asset, and compare SHA256 before installing.");
                        }
                    }
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
                if (safeVersion != null)
                    safeVersion.TryDeleteFile(compressedDownloadName, out _);
                safeVersion?.Dispose();
                safeRoot?.Dispose();
            }
        }

        private static void EnsureInstallRootStillAllowed(string installRoot, InstallPhase phase)
        {
            if (OpenH264InstallLocation.IsAllowedInstallRoot(installRoot, out var reason))
                return;

            throw new IOException(
                "OpenH264 install destination changed during " + phase + ": " + reason);
        }









        internal static string GetPackageRoot()
        {
            try
            {
                var package = PackageInfo.FindForAssembly(typeof(OpenH264OfficialBinaryInstaller).Assembly);
                if (package != null && !string.IsNullOrWhiteSpace(package.resolvedPath))
                    return package.resolvedPath;
            }
            catch
            {
                // Fall through to repository-style layout.
            }

            var candidate = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "dev.unity2foxglove.sdk");
            return Directory.Exists(candidate) ? candidate : "";
        }





















        private static OpenH264InstallResult Fail(string message)
            => new OpenH264InstallResult(false, "", "", string.IsNullOrWhiteSpace(message) ? "OpenH264 install failed." : message);


    }
}
