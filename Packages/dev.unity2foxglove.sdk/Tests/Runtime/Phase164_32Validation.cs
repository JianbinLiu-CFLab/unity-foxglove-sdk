using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_32Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-32 Tests ---");
            _passed = 0;

            VerifyRuntimeSelectionCachesZenohPayloadDiagnostics();
            VerifyZenohSmokeReadsLogsIncrementally();
            VerifyLocalZenohPlaySetupThrottlesFallbackSearch();
            VerifyLyricalValidationFastPaths();
            VerifyPhase146BValidationCachesRepoText();
            VerifyRegistry();

            Console.WriteLine("Phase 164-32: " + _passed + " checks passed.\n");
        }

        private static void VerifyRuntimeSelectionCachesZenohPayloadDiagnostics()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var getter = Slice(source, "private static string GetZenohPayloadDiagnostic", "\n\n        private static string ComputeZenohPayloadDiagnostic");
            var compute = Slice(source, "private static string ComputeZenohPayloadDiagnostic", "\n\n        private static bool HasNativeLibrary");
            var invalidate = PhaseValidationSourceHelpers.SourceMethod(source, "public static void InvalidateStatusCache");

            Check(source.Contains("private static readonly Dictionary<string, string> ZenohPayloadDiagnostics", StringComparison.Ordinal)
                  && getter.Contains("ZenohPayloadDiagnostics.TryGetValue(cacheKey, out var cached)", StringComparison.Ordinal)
                  && getter.Contains("ComputeZenohPayloadDiagnostic(packageDirectory)", StringComparison.Ordinal)
                  && compute.Contains("EnumerateDirectories(pluginsRoot", StringComparison.Ordinal),
                "164-32A-1: runtime selector caches Zenoh payload diagnostics outside the descriptor hot path");
            Check(invalidate.Contains("ZenohPayloadDiagnostics.Clear();", StringComparison.Ordinal),
                "164-32A-2: runtime selector invalidates cached Zenoh payload diagnostics with runtime status");
        }

        private static void VerifyZenohSmokeReadsLogsIncrementally()
        {
            var source = Read("Scripts/smoke/ros2/phase162_lyrical_zenoh_player_smoke.py");
            var wait = Slice(source, "def wait_for_marker", "\n\ndef launch_to_log");

            Check(wait.Contains("last_position = 0", StringComparison.Ordinal)
                  && wait.Contains("stream.seek(last_position)", StringComparison.Ordinal)
                  && wait.Contains("last_position = stream.tell()", StringComparison.Ordinal)
                  && wait.Contains("tail = combined[-max(len(marker) - 1, 0):]", StringComparison.Ordinal)
                  && !wait.Contains("path.read_text", StringComparison.Ordinal),
                "164-32B-1: Zenoh smoke marker wait reads only new log bytes");
        }

        private static void VerifyLocalZenohPlaySetupThrottlesFallbackSearch()
        {
            var source = Read("Unity2Foxglove/Assets/Editor/Phase162LocalZenohPlaySetup.cs");
            var drive = PhaseValidationSourceHelpers.SourceMethod(source, "private static void DriveVehicleDuringPlay");

            Check(source.Contains("MotionTargetSearchIntervalSeconds", StringComparison.Ordinal)
                  && source.Contains("private static double nextMotionTargetSearchAt", StringComparison.Ordinal)
                  && drive.Contains("EditorApplication.timeSinceStartup >= nextMotionTargetSearchAt", StringComparison.Ordinal)
                  && drive.Contains("nextMotionTargetSearchAt = EditorApplication.timeSinceStartup + MotionTargetSearchIntervalSeconds", StringComparison.Ordinal)
                  && drive.Contains("GameObject.Find(\"Vehicle\")", StringComparison.Ordinal),
                "164-32C-1: local Lyrical Zenoh play setup throttles fallback Vehicle lookup");
        }

        private static void VerifyLyricalValidationFastPaths()
        {
            var source = Read("Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package.py");
            var inventory = Slice(source, "def check_inventory", "\n\ndef check_runtime_files");
            var boundaries = Slice(source, "def check_package_boundaries", "\n\ndef core_runtime_has_forbidden_tokens");
            var coreScan = Slice(source, "def core_runtime_has_forbidden_tokens", "\n\ndef run_checks");
            var parseArgs = Slice(source, "def parse_args", "\n\ndef main");
            var main = Slice(source, "def main", "\n\nif __name__");

            Check(inventory.Contains("should_hash_dlls = release_gate or not skip_dll_hash", StringComparison.Ordinal)
                  && inventory.Contains("if should_hash_dlls and expected_hash and file_sha256(package_path) != expected_hash", StringComparison.Ordinal)
                  && inventory.Contains("skipped by fast validation; use --release-gate", StringComparison.Ordinal),
                "164-32D-1: Lyrical runtime validator can skip per-DLL hash reads outside release gate");
            Check(parseArgs.Contains("\"--fast\"", StringComparison.Ordinal)
                  && parseArgs.Contains("\"--skip-dll-hash\"", StringComparison.Ordinal)
                  && main.Contains("and not args.release_gate", StringComparison.Ordinal),
                "164-32D-2: Lyrical DLL hash skipping is explicit and release gate keeps full hashing");
            Check(boundaries.Contains("not core_runtime_has_forbidden_tokens()", StringComparison.Ordinal)
                  && coreScan.Contains("for path in iter_files(CORE_PACKAGE / \"Runtime\"):", StringComparison.Ordinal)
                  && coreScan.Contains("if any(token in text for token in tokens):", StringComparison.Ordinal)
                  && !boundaries.Contains("\"\\n\".join", StringComparison.Ordinal),
                "164-32D-3: Lyrical core SDK runtime boundary scan uses early exit instead of concatenating all files");
        }

        private static void VerifyPhase146BValidationCachesRepoText()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuLyricalRuntimePackageValidation.cs");
            var readRepoText = PhaseValidationSourceHelpers.SourceMethod(source, "private static string ReadRepoText");

            Check(source.Contains("private static readonly Dictionary<string, string> FileTextCache", StringComparison.Ordinal)
                  && readRepoText.Contains("FileTextCache.TryGetValue(path, out var cached)", StringComparison.Ordinal)
                  && readRepoText.Contains("FileTextCache[path] = text;", StringComparison.Ordinal),
                "164-32E-1: Phase146B/162 Lyrical validation caches repository text reads within the process");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-32\"", StringComparison.Ordinal), "164-32F-1: validation registry exposes Phase164-32");
            Check(project.Contains("Phase164_32Validation.cs", StringComparison.Ordinal), "164-32F-2: runtime validation project compiles Phase164-32");
        }

        private static string Slice(string source, string startMarker, string endMarker)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            return end < 0 ? source.Substring(start) : source.Substring(start, end - start);
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
