using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_52Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-52 Tests ---");
            _passed = 0;

            VerifyRuntimePackageValidatorsKeepFastMode();
            VerifyRealProjectSmokeUsesBoundedWaits();
            VerifyHumblePackageValidationCachesTextReads();
            VerifyJazzyPackageBuilderSkipsUnchangedTextWrites();
            VerifyManualSmokeCachesReflectionAndHandlesAllLocalDistroEntrypoints();
            VerifyRegistry();

            Console.WriteLine("Phase 164-52: " + _passed + " checks passed.\n");
        }

        private static void VerifyRuntimePackageValidatorsKeepFastMode()
        {
            foreach (var distro in new[] { "humble", "lyrical" })
            {
                var source = Read($"Scripts/ros2forunity/windows/{distro}/validate_r2fu_runtime_package.py");
                Check(source.Contains("\"--fast\"", StringComparison.Ordinal)
                      && source.Contains("\"--skip-dll-hash\"", StringComparison.Ordinal)
                      && source.Contains("should_hash_dlls = release_gate or not skip_dll_hash", StringComparison.Ordinal)
                      && source.Contains("core_runtime_has_forbidden_tokens()", StringComparison.Ordinal),
                    $"164-52A-{distro}: R2FU runtime validator keeps fast-mode and release-gated DLL hashing");
            }
        }

        private static void VerifyRealProjectSmokeUsesBoundedWaits()
        {
            var source = Read("Scripts/smoke/ros2/phase127_r2fu_real_project_acceptance.py");
            var waitForSubscription = SourceMethod(source, "def wait_for_subscription(");
            var echoUnityTick = SourceMethod(source, "def echo_unity_tick(");
            var parseArgs = SourceMethod(source, "def parse_args(argv: list[str]) -> argparse.Namespace:");

            Check(source.Contains("default=45.0", StringComparison.Ordinal)
                  && source.Contains("default=10.0", StringComparison.Ordinal)
                  && source.Contains("\"--echo-retry-sleep\"", StringComparison.Ordinal),
                "164-52B-1: real-project smoke defaults avoid long fixed waits");
            Check(source.Contains("remaining = deadline - time.monotonic()", StringComparison.Ordinal)
                  && source.Contains("time.sleep(min(2.0, remaining))", StringComparison.Ordinal)
                  && !waitForSubscription.Contains("time.sleep(2)", StringComparison.Ordinal),
                "164-52B-2: subscription wait sleeps only until the remaining deadline");
            Check(source.Contains("retry_sleep_seconds: float", StringComparison.Ordinal)
                  && source.Contains("retry_sleep_seconds > 0.0", StringComparison.Ordinal)
                  && source.Contains("time.sleep(retry_sleep_seconds)", StringComparison.Ordinal)
                  && !echoUnityTick.Contains("time.sleep(2)", StringComparison.Ordinal),
                "164-52B-3: echo retry sleep is configurable instead of hardcoded");
        }

        private static void VerifyHumblePackageValidationCachesTextReads()
        {
            var source = Read("Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py");
            var textBoundaries = SourceMethod(source, "def check_text_boundaries(results: list[CheckResult]) -> None:");

            Check(source.Contains("TEXT_CACHE: dict[Path, str] = {}", StringComparison.Ordinal)
                  && source.Contains("def read_text_cached(path: Path) -> str:", StringComparison.Ordinal)
                  && source.Contains("def any_text_contains(texts: Iterable[str], token: str) -> bool:", StringComparison.Ordinal),
                "164-52C-1: Humble package validation owns cached text helpers");
            Check(textBoundaries.Contains("read_text_cached(PACKAGE / \"README.md\")", StringComparison.Ordinal)
                  && textBoundaries.Contains("boundary_texts = (", StringComparison.Ordinal)
                  && textBoundaries.Contains("any_text_contains(boundary_texts, \"complete transitive inventory\")", StringComparison.Ordinal)
                  && textBoundaries.Contains("manifest_text = read_text_cached(MANIFEST)", StringComparison.Ordinal)
                  && !textBoundaries.Contains("combined =", StringComparison.Ordinal)
                  && !textBoundaries.Contains("MANIFEST.read_text", StringComparison.Ordinal),
                "164-52C-2: Humble text boundary checks avoid duplicate reads and concatenated scans");
        }

        private static void VerifyJazzyPackageBuilderSkipsUnchangedTextWrites()
        {
            var source = Read("Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py");
            var writeText = SourceMethod(source, "def write_text(path: Path, content: str) -> None:");

            Check(writeText.Contains("normalized = content.rstrip() + \"\\n\"", StringComparison.Ordinal)
                  && writeText.Contains("if path.exists():", StringComparison.Ordinal)
                  && writeText.Contains("existing = path.read_text(encoding=\"utf-8\", errors=\"replace\")", StringComparison.Ordinal)
                  && writeText.Contains("if existing == normalized:", StringComparison.Ordinal)
                  && writeText.Contains("return", StringComparison.Ordinal),
                "164-52D-1: Jazzy runtime package builder skips unchanged text writes");
        }

        private static void VerifyManualSmokeCachesReflectionAndHandlesAllLocalDistroEntrypoints()
        {
            var source = Read("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase127R2FURealProjectSmoke.cs");
            var batchRunner = SourceMethod(source, "private sealed class BatchRunner");
            var ensureExecutor = SourceMethod(batchRunner, "private void EnsureExecutorStarted()");

            Check(batchRunner.Contains("private readonly MethodInfo _startExecutor;", StringComparison.Ordinal)
                  && batchRunner.Contains("_startExecutor = typeof(ROS2UnityComponent).GetMethod(", StringComparison.Ordinal),
                "164-52E-1: Phase127 batch smoke caches StartExecutor reflection once");
            Check(ensureExecutor.Contains("if (_startExecutor == null)", StringComparison.Ordinal)
                  && ensureExecutor.Contains("_startExecutor.Invoke(_ros2Unity, null);", StringComparison.Ordinal)
                  && !ensureExecutor.Contains("typeof(ROS2UnityComponent).GetMethod", StringComparison.Ordinal),
                "164-52E-2: Phase127 batch smoke does not reflect in the editor update loop");
            Check(CountOccurrences(source, "normalized.Contains(\"ros2_humble\")") == 2
                  && CountOccurrences(source, "normalized.Contains(\"ros2_jazzy\")") == 2
                  && CountOccurrences(source, "normalized.Contains(\"ros2_lyrical\")") == 2,
                "164-52E-3: Phase127 PATH guard recognizes all local ROS2 distro entrypoints");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-52\"", StringComparison.Ordinal), "164-52F-1: validation registry exposes Phase164-52");
            Check(project.Contains("Phase164_52Validation.cs", StringComparison.Ordinal), "164-52F-2: runtime validation project compiles Phase164-52");
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string SourceMethod(string source, string signature)
            => PhaseValidationSourceHelpers.SourceMethod(source, signature);

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
