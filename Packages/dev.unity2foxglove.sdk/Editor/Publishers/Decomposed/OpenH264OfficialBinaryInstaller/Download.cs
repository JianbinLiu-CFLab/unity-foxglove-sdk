// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: OpenH264 download and bzip2 decompression.

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
    public static partial class OpenH264OfficialBinaryInstaller
    {
        private static void EnsureInstallRootStillAllowed(string installRoot, InstallPhase phase)
        {
            if (OpenH264InstallLocation.IsAllowedInstallRoot(installRoot, out var reason))
                return;

            throw new IOException(
                "OpenH264 install destination changed during " + phase + ": " + reason);
        }

        private static void DownloadFile(string url, string destination)
        {
            var tempDestination = destination + ".partial";
            var versionDirectory = Path.GetDirectoryName(destination) ?? "";
            var installRoot = Directory.GetParent(versionDirectory)?.FullName ?? "";
            Func<bool> validateDestination = () =>
                OpenH264InstallLocation.IsAllowedInstallRoot(installRoot, out _);
            bool TryDelete(string path, bool warnOnFailure = false)
                => OpenH264OfficialBinaryInstaller.TryDelete(path, warnOnFailure, validateDestination);
            EnsureDestinationAllowed(validateDestination);
            TryDelete(tempDestination);
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Unity2Foxglove-OpenH264-Installer/1.0");
                try
                {
                    using (var cts = new CancellationTokenSource(DownloadBodyTimeoutMs))
                    using (var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult())
                    {
                        response.EnsureSuccessStatusCode();
                        using (var source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                        {
                            EnsureDestinationAllowed(validateDestination);
                            using (var destinationStream = File.Create(tempDestination))
                            {
                                source.CopyToAsync(destinationStream, 81920, cts.Token).GetAwaiter().GetResult();
                            }
                        }
                    }

                    EnsureDestinationAllowed(validateDestination);
                    if (File.Exists(destination))
                    {
                        EnsureDestinationAllowed(validateDestination);
                        File.Delete(destination);
                    }

                    EnsureDestinationAllowed(validateDestination);
                    File.Move(tempDestination, destination);
                }
                catch
                {
                    TryDelete(tempDestination);
                    throw;
                }
            }
        }

        private static bool TryDecompressBZip2(
            string compressedPath,
            string outputPath,
            Func<bool> validateDestination,
            out string error)
        {
            error = "";
            EnsureDestinationAllowed(validateDestination);
            // The official Cisco asset is .bz2. We intentionally do not bundle
            // SharpZipLib or another MIT BZip2 decompressor here; if that changes,
            // update THIRD_PARTY_NOTICES before shipping.
            if (TryDecompressWithBZip2Tool(
                    compressedPath,
                    outputPath,
                    validateDestination,
                    out var bzip2Error))
                return true;

            if (TryDecompressWithPythonBZip2(
                    compressedPath,
                    outputPath,
                    validateDestination,
                    out var pythonError))
                return true;

            error = CombineDecompressErrors(bzip2Error, pythonError);
            if (string.IsNullOrEmpty(error))
                error = "No BZip2 decompressor was found. Install bzip2 or Python, or decompress the DLL manually.";
            return false;
        }

        private static bool TryDecompressWithBZip2Tool(
            string compressedPath,
            string outputPath,
            Func<bool> validateDestination,
            out string error)
        {
            error = "";
            var bzip2 = FindExecutable("bzip2");
            if (string.IsNullOrEmpty(bzip2))
            {
                error = "bzip2 executable was not found on PATH.";
                return false;
            }

            var workDir = Path.GetDirectoryName(outputPath) ?? "";
            var tempCompressed = outputPath + ".bz2";
            var tempDecompressed = outputPath;
            try
            {
                EnsureDestinationAllowed(validateDestination);
                File.Copy(compressedPath, tempCompressed, true);
                EnsureDestinationAllowed(validateDestination);
                if (File.Exists(tempDecompressed))
                {
                    EnsureDestinationAllowed(validateDestination);
                    File.Delete(tempDecompressed);
                }

                var result = RunProcess(
                    bzip2,
                    "-dkf " + QuoteArgument(tempCompressed),
                    workDir,
                    15000,
                    out var stderr);
                EnsureDestinationAllowed(validateDestination);
                if (!result || !File.Exists(tempDecompressed))
                {
                    error = string.IsNullOrWhiteSpace(stderr)
                        ? "bzip2 did not produce the OpenH264 DLL."
                        : stderr.Trim();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                TryDelete(tempCompressed, validateDestination: validateDestination);
            }
        }
    }
}
