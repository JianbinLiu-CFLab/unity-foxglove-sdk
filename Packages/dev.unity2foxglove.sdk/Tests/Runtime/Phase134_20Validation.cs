// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-20 validation for safe native editor process execution.

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_20Validation
    {
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifyRunnerDrainsLargeStderr();
            VerifyRunnerTimeoutKillsProcess();
            VerifyOpenSslToolingUsesSafeRunner();
            VerifyOpenSslCompatibilityAndSanitization();
            VerifyRos2BridgeHealthCancellation();
            VerifyOpenH264HelperBoundary();
            VerifyEditorSchemaAndPrefsHygiene();

            Console.WriteLine($"Phase134_20Validation: PASS ({_passed} checks)");
        }

        private static void VerifyRunnerDrainsLargeStderr()
        {
            var result = FoxgloveEditorProcessRunner.Run(CreateStderrFloodStartInfo(), 10000);
            Check(!result.TimedOut, "134-20-A1: safe process runner does not time out on large stderr output");
            Check(result.ExitCode == 0, "134-20-A2: safe process runner preserves child exit code");
            Check(result.Stderr.Length > 100000, "134-20-A3: safe process runner drains redirected stderr concurrently");
        }

        private static void VerifyRunnerTimeoutKillsProcess()
        {
            var result = FoxgloveEditorProcessRunner.Run(CreateSlowProcessStartInfo(), 250);
            Check(result.TimedOut, "134-20-B1: safe process runner reports timeout for hung native tool");
            Check(result.ExitCode == -1, "134-20-B2: safe process runner uses sentinel exit code for killed process");
        }

        private static void VerifyOpenSslToolingUsesSafeRunner()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Certificates/FoxgloveLocalDevCertificateGenerator.cs");
            Check(source.Contains("OpenSslToolTimeoutMs", StringComparison.Ordinal)
                  && source.Contains("FoxgloveEditorProcessRunner.Run", StringComparison.Ordinal),
                "134-20-C1: OpenSSL certificate backend uses bounded safe process runner");
            Check(!source.Contains("StandardOutput.ReadToEnd", StringComparison.Ordinal)
                  && !source.Contains("StandardError.ReadToEnd", StringComparison.Ordinal),
                "134-20-C2: OpenSSL certificate backend no longer reads redirected streams synchronously");
            Check(source.Contains("timed out after", StringComparison.Ordinal)
                  && source.Contains("result.Stderr", StringComparison.Ordinal)
                  && source.Contains("result.Stdout", StringComparison.Ordinal),
                "134-20-C3: OpenSSL certificate backend reports timeout diagnostics from both streams");
        }

        private static void VerifyOpenSslCompatibilityAndSanitization()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Certificates/FoxgloveLocalDevCertificateGenerator.cs");
            Check(source.Contains("RequiresLegacyPkcs12Option", StringComparison.Ordinal)
                  && source.Contains("Arguments = \"version\"", StringComparison.Ordinal)
                  && source.Contains("\" -legacy\"", StringComparison.Ordinal),
                "134-20-D1: OpenSSL backend detects OpenSSL 3 legacy PKCS#12 needs");
            Check(source.Contains("IsSafeDnsName", StringComparison.Ordinal)
                  && source.Contains("letters, digits, dots, or hyphens", StringComparison.Ordinal),
                "134-20-D2: certificate DNS SAN values are validated before writing OpenSSL config");
            Check(source.Contains("ParametersMatch(method.GetParameters(), args)", StringComparison.Ordinal)
                  && source.Contains("private static bool ParametersMatch", StringComparison.Ordinal),
                "134-20-D3: built-in certificate reflection matches method parameter types");
        }

        private static void VerifyRos2BridgeHealthCancellation()
        {
            var options = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeHealthOptions.cs");
            var runner = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeHealthRunner.cs");
            var commandRunner = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/IRos2BridgeCommandRunner.cs");
            var probe = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeU2R2HealthProbe.cs");
            var drawer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Ros2Bridge/Ros2BridgeHealthDrawer.cs");

            Check(options.Contains("CancellationToken CancellationToken", StringComparison.Ordinal)
                  && runner.Contains("options.CancellationToken", StringComparison.Ordinal),
                "134-20-E1: ROS2 health options and runner carry cancellation tokens");
            Check(commandRunner.Contains("CancellationToken cancellationToken", StringComparison.Ordinal)
                  && commandRunner.Contains("WaitForExitOrCancellation", StringComparison.Ordinal)
                  && probe.Contains("WaitOrCancel", StringComparison.Ordinal),
                "134-20-E2: ROS2 command runner and U2R2 probe honor cancellation");
            Check(drawer.Contains("Cancel ROS2 Bridge Check", StringComparison.Ordinal)
                  && drawer.Contains("AssemblyReloadEvents.beforeAssemblyReload", StringComparison.Ordinal)
                  && drawer.Contains("AssemblyReloadEvents.beforeAssemblyReload -=", StringComparison.Ordinal)
                  && drawer.Contains("CancelHealthCheck", StringComparison.Ordinal),
                "134-20-E3: ROS2 health drawer exposes cancel flow and cancels on assembly reload");
        }

        private static void VerifyOpenH264HelperBoundary()
        {
            const string packageSource = "Packages/dev.unity2foxglove.sdk/Editor/Native/OpenH264/openh264_probe_encoder.cpp";
            const string scriptSource = "Scripts/native/openh264_probe/openh264_probe_encoder.cpp";
            var package = ReadRepoText(packageSource);
            Check(Sha256Hex(ReadRepoBytes(packageSource)) == Sha256Hex(ReadRepoBytes(scriptSource)),
                "134-20-F1: package and manual OpenH264 helper sources stay byte-identical");
            Check(package.Contains("#include \"codec_ver.h\"", StringComparison.Ordinal)
                  && package.Contains("WelsGetCodecVersion", StringComparison.Ordinal)
                  && package.Contains("OPENH264_MAJOR", StringComparison.Ordinal),
                "134-20-F2: OpenH264 helper validates runtime library version");
            Check(package.Contains("std::numeric_limits<int>::max()", StringComparison.Ordinal)
                  && package.Contains("ERANGE", StringComparison.Ordinal)
                  && package.Contains("std::numeric_limits<uint32_t>::max()", StringComparison.Ordinal),
                "134-20-F3: OpenH264 helper bounds numeric arguments and access-unit length");

            var readme = ReadRepoText("Scripts/native/openh264_probe/README.md");
            Check(readme.Contains(@"Packages\dev.unity2foxglove.sdk\Editor\Native\OpenH264\v2.6.0\include\wels", StringComparison.Ordinal)
                  && readme.Contains("non-Windows", StringComparison.Ordinal)
                  && readme.Contains("OpenH264 2.6.0", StringComparison.Ordinal),
                "134-20-F4: OpenH264 manual build docs use pinned headers and document platform contract");

            var provenance = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Native/OpenH264/v2.6.0/HEADER_PROVENANCE.md");
            Check(provenance.Contains("21f29b20c24f7c7946f2e243d0bc2532fb3542f6c28af338209477e70d9036c9", StringComparison.Ordinal)
                  && provenance.Contains("9a241e20b7c9221a5786cccd9eae3afed91afba3525b5b9b16c2101976516f94", StringComparison.Ordinal),
                "134-20-F5: OpenH264 header provenance records upstream snapshot hashes");
        }

        private static void VerifyEditorSchemaAndPrefsHygiene()
        {
            var paths = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");
            var generator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SchemaManifest/Unity2FoxgloveSchemaManifestGenerator.cs");
            var settings = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidenceSettings.cs");
            var prefs = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Ros2Bridge/Ros2BridgeEditorPrefs.cs");

            Check(paths.Contains("StringComparison.OrdinalIgnoreCase", StringComparison.Ordinal)
                  && paths.Contains("\"Assets\" + candidate.Substring", StringComparison.Ordinal),
                "134-20-G1: schema evidence paths canonicalize Assets casing");
            Check(generator.Contains("internal static class Unity2FoxgloveSchemaManifestGenerator", StringComparison.Ordinal),
                "134-20-G2: schema manifest generator remains internal editor API");
            Check(settings.Contains("Undo.RecordObject", StringComparison.Ordinal)
                  && settings.Contains("ApplyModifiedProperties()", StringComparison.Ordinal)
                  && !settings.Contains("ApplyModifiedPropertiesWithoutUndo", StringComparison.Ordinal),
                "134-20-G3: schema evidence sync records undoable manager changes");
            Check(prefs.Contains("LegacyRos2ExecutablePathKeys", StringComparison.Ordinal)
                  && prefs.Contains("MigrateLegacyRos2ExecutablePath", StringComparison.Ordinal)
                  && prefs.Contains("EditorPrefs.DeleteKey(key)", StringComparison.Ordinal),
                "134-20-G4: ROS2 Bridge EditorPrefs include migration and cleanup hooks");
        }

        private static ProcessStartInfo CreateStderrFloodStartInfo()
        {
            var payload = new string('x', 640);

            if (IsWindows)
            {
                return new ProcessStartInfo(
                    "cmd.exe",
                    $"/c for /L %i in (1,1,200) do @echo stderr-line-{payload} 1>&2");
            }

            return new ProcessStartInfo(
                "/bin/sh",
                $"-c \"i=0; while [ $i -lt 200 ]; do echo stderr-line-{payload} >&2; i=$((i+1)); done\"");
        }

        private static ProcessStartInfo CreateSlowProcessStartInfo()
        {
            if (IsWindows)
                return new ProcessStartInfo("cmd.exe", "/c ping -n 6 127.0.0.1 > nul");

            return new ProcessStartInfo("/bin/sh", "-c \"sleep 5\"");
        }

        private static bool IsWindows
            => Path.DirectorySeparatorChar == '\\';

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static byte[] ReadRepoBytes(string relativePath)
            => File.ReadAllBytes(RepoPath(relativePath));

        private static string Sha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

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
