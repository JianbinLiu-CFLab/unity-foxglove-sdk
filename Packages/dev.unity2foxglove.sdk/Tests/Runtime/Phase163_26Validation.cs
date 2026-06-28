// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-26 validation for editor native helper, certificate,
// settings, and installer boundaries.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_26Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-26: Editor Native Helpers and Installers ===");
            _passed = 0;

            CertificateGeneratorKeepsKeysInIgnoredUserSettings();
            OpenH264InstallerVerifiesPinnedArtifacts();
            OpenH264PathsHaveSingleInstallAuthority();
            ZenohPlaySetupIsExplicitCommandLineOnly();
            Ros2ForUnitySettingsAvoidMachineLocalPaths();
            EditorProcessesHaveTimeoutCleanup();
            PackageValidatorsAreWiredIntoCi();
            DiagnosticsInspectorUsesTypedStatsOnly();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-26: {_passed} checks passed.");
        }

        private static void CertificateGeneratorKeepsKeysInIgnoredUserSettings()
        {
            var generator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Certificates/FoxgloveLocalDevCertificateGenerator.cs");
            var gitignore = ReadRepoText(".gitignore");

            Check(generator.Contains("RelativeCertificateDirectory = \"UserSettings/Unity2Foxglove/Certificates\"", StringComparison.Ordinal)
                  && generator.Contains("Path.Combine(ProjectRoot, RelativeCertificateDirectory)", StringComparison.Ordinal),
                "163-26A-1: local certificate generator writes under project UserSettings, not Assets or StreamingAssets");
            Check(gitignore.Contains("UserSettings/", StringComparison.Ordinal),
                "163-26A-2: generated local certificate material is covered by repository gitignore");
            Check(generator.Contains("TryDelete(keyPath);", StringComparison.Ordinal)
                  && generator.Contains("TryDelete(configPath);", StringComparison.Ordinal),
                "163-26A-3: OpenSSL backend deletes temporary private key and config files");
        }

        private static void OpenH264InstallerVerifiesPinnedArtifacts()
        {
            var manifest = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryManifest.cs");
            var installer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs");
            var verifier = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/OpenH264/OpenH264ArtifactHashVerifier.cs");

            Check(manifest.Contains("DownloadUrl = \"https://ciscobinary.openh264.org/", StringComparison.Ordinal)
                  && manifest.Contains("CompressedAssetSha256", StringComparison.Ordinal)
                  && manifest.Contains("DllSha256", StringComparison.Ordinal),
                "163-26B-1: OpenH264 manifest pins HTTPS source and SHA256 digests");
            Check(installer.Contains("TryVerifySha256", StringComparison.Ordinal)
                  && CheckOrdered(installer, "DownloadFile(OpenH264OfficialBinaryManifest.DownloadUrl, compressedDownloadPath);", "OpenH264OfficialBinaryManifest.CompressedAssetSha256")
                  && CheckOrdered(installer, "TryDecompressBZip2(compressedPath, tempDll", "OpenH264OfficialBinaryManifest.DllSha256"),
                "163-26B-2: installer verifies archive before final move and DLL after decompression");
            Check(verifier.Contains("expectedSha256", StringComparison.Ordinal)
                  && verifier.Contains("IsSha256Hex", StringComparison.Ordinal)
                  && verifier.Contains("StringComparison.OrdinalIgnoreCase", StringComparison.Ordinal),
                "163-26B-3: OpenH264 hash verifier validates SHA256 shape and compares digest exactly");
        }

        private static void OpenH264PathsHaveSingleInstallAuthority()
        {
            var location = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264InstallLocation.cs");
            var installer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs");

            Check(location.Contains("GetFinalDllPath", StringComparison.Ordinal)
                  && location.Contains("GetFinalHelperPath", StringComparison.Ordinal)
                  && location.Contains("IsAllowedInstallRoot", StringComparison.Ordinal),
                "163-26C-1: OpenH264 install location is centralized in OpenH264InstallLocation");
            Check(installer.Contains("OpenH264InstallLocation.GetFinalDllPath", StringComparison.Ordinal)
                  && installer.Contains("OpenH264InstallLocation.GetFinalHelperPath", StringComparison.Ordinal)
                  && installer.Contains("OpenH264InstallLocation.IsAllowedInstallRoot", StringComparison.Ordinal),
                "163-26C-2: OpenH264 installer delegates final paths and root policy to the shared location helper");
        }

        private static void ZenohPlaySetupIsExplicitCommandLineOnly()
        {
            var setup = ReadRepoText("Unity2Foxglove/Assets/Editor/Phase162LocalZenohPlaySetup.cs");

            Check(setup.Contains("[InitializeOnLoadMethod]", StringComparison.Ordinal)
                  && setup.Contains("Environment.GetCommandLineArgs()", StringComparison.Ordinal)
                  && setup.Contains("Phase162LocalZenohPlaySetup.ConfigureAndPlay", StringComparison.Ordinal)
                  && setup.Contains("return;", StringComparison.Ordinal),
                "163-26D-1: Phase162 Zenoh play setup auto-runs only with explicit command-line token");
            Check(setup.Contains("SessionState.GetBool(PlayRequestedKey, false)", StringComparison.Ordinal)
                  && setup.Contains("SessionState.SetBool(PlayRequestedKey, true)", StringComparison.Ordinal),
                "163-26D-2: Phase162 Zenoh play setup is bounded by editor-session state");
        }

        private static void Ros2ForUnitySettingsAvoidMachineLocalPaths()
        {
            var settings = ReadRepoText("Unity2Foxglove/ProjectSettings/Unity2FoxgloveRos2ForUnitySettings.json");

            Check(settings.Contains("\"activeRuntimePackage\"", StringComparison.Ordinal)
                  && !settings.Contains("C:\\", StringComparison.OrdinalIgnoreCase)
                  && !settings.Contains("D:\\", StringComparison.OrdinalIgnoreCase)
                  && !settings.Contains("/Users/", StringComparison.Ordinal)
                  && !settings.Contains("/home/", StringComparison.Ordinal),
                "163-26E-1: committed R2FU project settings contain package identity, not machine-local paths");
        }

        private static void EditorProcessesHaveTimeoutCleanup()
        {
            var runner = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/Process/FoxgloveEditorProcessRunner.cs");
            var openH264Check = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264ExecutableCheck.cs");

            Check(runner.Contains("if (!process.WaitForExit(Math.Max(1, timeoutMs)))", StringComparison.Ordinal)
                  && runner.Contains("TryKill(process);", StringComparison.Ordinal)
                  && runner.Contains("using (var process = new Process())", StringComparison.Ordinal),
                "163-26F-1: shared editor process runner bounds subprocess lifetime and disposes process handles");
            Check(openH264Check.Contains("if (!process.WaitForExit(Math.Max(500, timeoutMs)))", StringComparison.Ordinal)
                  && openH264Check.Contains("TryKill(process);", StringComparison.Ordinal)
                  && openH264Check.Contains("using (var process = new Process())", StringComparison.Ordinal),
                "163-26F-2: OpenH264 executable probe bounds helper lifetime and disposes process handles");
        }

        private static void PackageValidatorsAreWiredIntoCi()
        {
            var packageWorkflow = ReadRepoText(".github/workflows/package-check.yml");
            var dotnetWorkflow = ReadRepoText(".github/workflows/dotnet-tests.yml");
            var runCi = ReadRepoText("Scripts/release/run_ci.py");

            Check(packageWorkflow.Contains("Scripts/package/validate_unity_package.py", StringComparison.Ordinal)
                  && packageWorkflow.Contains("Scripts/package/validate_local_entrypoints.py", StringComparison.Ordinal),
                "163-26G-1: package workflow runs release package and local entrypoint validators");
            Check(dotnetWorkflow.Contains("Scripts/package/validate_source_generator_dll.py", StringComparison.Ordinal),
                "163-26G-2: dotnet workflow directly runs the source generator DLL freshness validator");
            Check(runCi.Contains("Scripts/package/validate_unity_package.py", StringComparison.Ordinal)
                  && runCi.Contains("Scripts/package/validate_local_entrypoints.py", StringComparison.Ordinal)
                  && runCi.Contains("Scripts/package/validate_source_generator_dll.py", StringComparison.Ordinal),
                "163-26G-3: local run_ci mirrors package and analyzer validator coverage");
        }

        private static void DiagnosticsInspectorUsesTypedStatsOnly()
        {
            var diagnostics = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Diagnostics.cs");

            Check(diagnostics.Contains("manager.GetTransportStatsSnapshot()", StringComparison.Ordinal)
                  && diagnostics.Contains("EditorGUILayout.LongField(\"Queued Bytes\"", StringComparison.Ordinal)
                  && diagnostics.Contains("EditorGUILayout.LabelField(", StringComparison.Ordinal)
                  && !diagnostics.Contains("string.Format", StringComparison.Ordinal),
                "163-26H-1: manager diagnostics inspector renders typed transport stats without string-format log injection");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_26Validation.cs", StringComparison.Ordinal),
                "163-26I-1: runtime test project compiles Phase163_26Validation");
            Check(registry.Contains("--phase163-26", StringComparison.Ordinal)
                  && registry.Contains("Phase163_26Validation.Validate", StringComparison.Ordinal),
                "163-26I-2: validation registry exposes --phase163-26");
        }

        private static bool CheckOrdered(string text, string first, string second)
        {
            var firstIndex = text.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = text.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
        {
            var root = AppContext.BaseDirectory;
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
                if (File.Exists(candidate))
                    return candidate;
                var parent = Directory.GetParent(root);
                if (parent == null)
                    break;
                root = parent.FullName;
            }

            throw new FileNotFoundException("Could not locate repository file: " + relativePath);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException(label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
