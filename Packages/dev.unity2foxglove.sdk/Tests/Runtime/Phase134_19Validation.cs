// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-19 validation for OpenH264 installer artifact hash pinning.

using System;
using System.IO;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_19Validation
    {
        private const string CompressedSha256 = "DAB5F2A872777F9A58B69BFA9FBCF20D9F82F2D6EC91383FD70BFF49BD34AC9F";
        private const string DllSha256 = "2076CB5675EC6C1A4C70E7A2A322552F547B6EEED649D6DFCD9E02A543B24691";
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifyHashVerifierRejectsMismatch();
            VerifyManifestPinsArtifactHashes();
            VerifyInstallerChecksHashesBeforeMovingDll();
            VerifyInspectorSurfacesPinnedHashes();
            VerifyOpenH264ProcessOutputIsDrainedBeforeWait();
            VerifyInstallerRunsOffInspectorGuiPath();
            VerifyCameraEditorIsNullSafeAndLabelChecked();
            VerifyEditorCleanupFindings();

            Console.WriteLine($"Phase134_19Validation: PASS ({_passed} checks)");
        }

        private static void VerifyHashVerifierRejectsMismatch()
        {
            var path = Path.Combine(Path.GetTempPath(), "u2fg_openh264_hash_" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllBytes(path, new byte[] { 0x01, 0x02, 0x03 });
                var actual = OpenH264ArtifactHashVerifier.ComputeSha256(path);
                Check(actual.Length == 64, "134-19-A1: OpenH264 hash verifier computes SHA256 hex");

                var accepted = OpenH264ArtifactHashVerifier.TryVerifySha256(
                    path,
                    actual,
                    "test artifact",
                    out var acceptedActual,
                    out var acceptedError);
                Check(accepted && acceptedActual == actual && string.IsNullOrEmpty(acceptedError),
                    "134-19-A2: OpenH264 hash verifier accepts matching hash");

                var rejected = OpenH264ArtifactHashVerifier.TryVerifySha256(
                    path,
                    new string('0', 64),
                    "test artifact",
                    out var rejectedActual,
                    out var rejectedError);
                Check(!rejected
                      && rejectedActual == actual
                      && rejectedError.Contains("SHA256 mismatch", StringComparison.Ordinal)
                      && rejectedError.Contains("Expected", StringComparison.Ordinal)
                      && rejectedError.Contains("actual", StringComparison.Ordinal),
                    "134-19-A3: OpenH264 hash verifier rejects mismatches with expected and actual hashes");
            }
            finally
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                }
            }
        }

        private static void VerifyManifestPinsArtifactHashes()
        {
            var manifest = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryManifest.cs");
            Check(manifest.Contains("CompressedAssetSha256", StringComparison.Ordinal)
                  && manifest.Contains(CompressedSha256, StringComparison.Ordinal),
                "134-19-B1: OpenH264 manifest pins compressed asset SHA256");
            Check(manifest.Contains("DllSha256", StringComparison.Ordinal)
                  && manifest.Contains(DllSha256, StringComparison.Ordinal),
                "134-19-B2: OpenH264 manifest pins decompressed DLL SHA256");
        }

        private static void VerifyInstallerChecksHashesBeforeMovingDll()
        {
            var installer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs");
            var downloadIndex = installer.IndexOf("DownloadFile(OpenH264OfficialBinaryManifest.DownloadUrl, compressedPath)", StringComparison.Ordinal);
            var compressedVerifyIndex = installer.IndexOf("OpenH264OfficialBinaryManifest.CompressedAssetSha256", StringComparison.Ordinal);
            var decompressIndex = installer.IndexOf("TryDecompressBZip2(compressedPath, tempDll", StringComparison.Ordinal);
            var dllVerifyIndex = installer.IndexOf("OpenH264OfficialBinaryManifest.DllSha256", StringComparison.Ordinal);
            var moveIndex = installer.IndexOf("File.Move(tempDll, finalDllPath)", StringComparison.Ordinal);

            Check(downloadIndex >= 0
                  && compressedVerifyIndex > downloadIndex
                  && compressedVerifyIndex < decompressIndex,
                "134-19-C1: OpenH264 installer verifies downloaded compressed asset before decompression");
            Check(decompressIndex >= 0
                  && dllVerifyIndex > decompressIndex
                  && dllVerifyIndex < moveIndex,
                "134-19-C2: OpenH264 installer verifies decompressed DLL before final move");
            Check(installer.Contains("TryDelete(tempDll)", StringComparison.Ordinal)
                  && installer.Contains("compare SHA256 before installing", StringComparison.Ordinal),
                "134-19-C3: OpenH264 installer fails closed and gives hash-auditable fallback guidance");
        }

        private static void VerifyInspectorSurfacesPinnedHashes()
        {
            var editor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            Check(editor.Contains("Compressed SHA256", StringComparison.Ordinal)
                  && editor.Contains("DLL SHA256", StringComparison.Ordinal),
                "134-19-D1: OpenH264 install UI surfaces pinned SHA256 values");
        }

        private static void VerifyOpenH264ProcessOutputIsDrainedBeforeWait()
        {
            var installer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs");
            Check(installer.Contains("FoxgloveEditorProcessRunner.Run", StringComparison.Ordinal)
                  && !installer.Contains("process.StandardOutput.ReadToEnd();", StringComparison.Ordinal),
                "134-19-E1: OpenH264 installer uses shared redirected process runner instead of post-wait pipe reads");
            Check(!installer.Contains("new WebClient()", StringComparison.Ordinal)
                  && installer.Contains("new HttpClient()", StringComparison.Ordinal),
                "134-19-E2: OpenH264 installer uses HttpClient instead of obsolete WebClient");

            var check = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264ExecutableCheck.cs");
            var stdoutTaskIndex = check.IndexOf("var stdoutTask = ReadAllBytesAsync", StringComparison.Ordinal);
            var waitIndex = check.IndexOf("process.WaitForExit", StringComparison.Ordinal);
            Check(stdoutTaskIndex >= 0 && waitIndex > stdoutTaskIndex,
                "134-19-E3: OpenH264 validation starts stdout/stderr drains before waiting for process exit");
        }

        private static void VerifyInstallerRunsOffInspectorGuiPath()
        {
            var editor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            Check(editor.Contains("Task<OpenH264InstallResult>", StringComparison.Ordinal)
                  && editor.Contains("Task.Run(() => OpenH264OfficialBinaryInstaller.Install", StringComparison.Ordinal)
                  && editor.Contains("EditorApplication.update += PollInstallTask", StringComparison.Ordinal),
                "134-19-F1: OpenH264 install window runs install work in a background task and polls completion on the editor thread");
            Check(editor.Contains("Installing...", StringComparison.Ordinal)
                  && editor.Contains("new EditorGUI.DisabledScope(IsInstalling)", StringComparison.Ordinal),
                "134-19-F2: OpenH264 install window disables duplicate install/cancel actions while work is running");
        }

        private static void VerifyCameraEditorIsNullSafeAndLabelChecked()
        {
            var editor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            Check(editor.Contains("publishRateSource == null", StringComparison.Ordinal)
                  && editor.Contains("if (publishRateHz != null)", StringComparison.Ordinal),
                "134-19-G1: camera publish-rate inspector tolerates missing serialized fields");
            Check(editor.Contains("CameraOutputModeLabels.Length != enumCount", StringComparison.Ordinal)
                  && editor.Contains("Debug.LogWarning", StringComparison.Ordinal),
                "134-19-G2: camera output mode labels are checked against the enum length");
            Check(editor.Contains("ResolveBrowseDefaultDirectory", StringComparison.Ordinal)
                  && editor.Contains("Directory.Exists(current)", StringComparison.Ordinal),
                "134-19-G3: camera executable browse dialogs prefer existing directories and fall back to project root");
        }

        private static void VerifyEditorCleanupFindings()
        {
            var installer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs");
            Check(!installer.Contains("\"2026\", \"2022\", \"2019\"", StringComparison.Ordinal)
                  && installer.Contains("TryDelete(compressedPath)", StringComparison.Ordinal),
                "134-19-H1: OpenH264 installer removes dead VS year probe and cleans successful download cache");

            var replayDrawer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/McapReplayPreflightDrawer.cs");
            Check(replayDrawer.Contains("var seen = new HashSet<string>()", StringComparison.Ordinal)
                  && replayDrawer.Contains("FoxgloveManagerEditor.ResolveProjectPath(path)", StringComparison.Ordinal),
                "134-19-H2: replay preflight deduplicates topics with a set and reuses manager editor path helpers");
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine(name);
        }
    }
}
