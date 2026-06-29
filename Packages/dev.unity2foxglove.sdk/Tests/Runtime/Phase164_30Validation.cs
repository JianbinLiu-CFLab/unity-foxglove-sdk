using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_30Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-30 Tests ---");
            _passed = 0;

            VerifyHumbleRuntimeValidatorFastPaths();
            VerifyHumbleBuildAndInspectFastPaths();
            VerifyHumbleAdapterAndSmokeFastPaths();
            VerifyPhase160ValidationCachesRepoText();
            VerifyRegistry();

            Console.WriteLine("Phase 164-30: " + _passed + " checks passed.\n");
        }

        private static void VerifyHumbleRuntimeValidatorFastPaths()
        {
            var source = Read("Scripts/ros2forunity/windows/humble/validate_r2fu_runtime_package.py");
            var inventory = Slice(source, "def check_inventory", "\n\ndef check_runtime_files");
            var boundaries = Slice(source, "def check_package_boundaries", "\n\ndef core_runtime_has_forbidden_tokens");
            var coreScan = Slice(source, "def core_runtime_has_forbidden_tokens", "\n\ndef run_checks");
            var parseArgs = Slice(source, "def parse_args", "\n\ndef main");
            var main = Slice(source, "def main", "\n\nif __name__");

            Check(inventory.Contains("should_hash_dlls = release_gate or not skip_dll_hash", StringComparison.Ordinal)
                  && inventory.Contains("if should_hash_dlls and expected_hash and file_sha256(package_path) != expected_hash", StringComparison.Ordinal)
                  && inventory.Contains("skipped by fast validation; use --release-gate", StringComparison.Ordinal),
                "164-30A-1: Humble runtime validator can skip per-DLL hash reads outside release gate");
            Check(parseArgs.Contains("\"--fast\"", StringComparison.Ordinal)
                  && parseArgs.Contains("\"--skip-dll-hash\"", StringComparison.Ordinal)
                  && main.Contains("and not args.release_gate", StringComparison.Ordinal),
                "164-30A-2: fast DLL hash skipping is an explicit flag and release gate keeps full hashing");
            Check(boundaries.Contains("not core_runtime_has_forbidden_tokens()", StringComparison.Ordinal)
                  && coreScan.Contains("for path in iter_files(CORE_PACKAGE / \"Runtime\"):", StringComparison.Ordinal)
                  && coreScan.Contains("if any(token in text for token in tokens):", StringComparison.Ordinal)
                  && !boundaries.Contains("\"\\n\".join", StringComparison.Ordinal),
                "164-30A-3: core SDK runtime boundary scan uses early exit instead of concatenating all files");
        }

        private static void VerifyHumbleBuildAndInspectFastPaths()
        {
            var builder = Read("Scripts/ros2forunity/windows/humble/build_r2fu_runtime_package.py");
            var inspect = Read("Scripts/ros2forunity/windows/humble/inspect_r2fu_runtime_artifact.py");
            var ensureMeta = Slice(builder, "def ensure_generated_meta", "\n\ndef write_generated_metas");
            var writeMetas = Slice(builder, "def write_generated_metas", "\n\ndef package_json");
            var inspectZip = Slice(inspect, "def inspect_zip", "\n\ndef read_cached_inventory");
            var cachedInventory = Slice(inspect, "def read_cached_inventory", "\n\ndef write_inventory");

            Check(ensureMeta.Contains("existing_paths: set[str]", StringComparison.Ordinal)
                  && ensureMeta.Contains("if meta_key in existing_paths:", StringComparison.Ordinal)
                  && !ensureMeta.Contains("path_exists(meta)", StringComparison.Ordinal)
                  && writeMetas.Contains("existing_paths = {path.as_posix() for path in paths}", StringComparison.Ordinal),
                "164-30B-1: Humble meta generation reuses rglob results instead of statting each meta path");
            Check(inspect.Contains("force: bool = False", StringComparison.Ordinal)
                  && inspect.Contains("parser.add_argument(\"--force\"", StringComparison.Ordinal)
                  && inspectZip.Contains("read_cached_inventory(paths.output, artifact_hash)", StringComparison.Ordinal)
                  && cachedInventory.Contains("data.get(\"sha256\") == artifact_hash", StringComparison.Ordinal),
                "164-30B-2: Humble artifact inspect reuses an existing inventory when the artifact hash is unchanged");
        }

        private static void VerifyHumbleAdapterAndSmokeFastPaths()
        {
            var adapterValidator = Read("Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py");
            var smokeEnv = Read("Scripts/smoke/ros2/_ros2_windows_env.py");
            var phase160Smoke = Read("Scripts/smoke/ros2/phase160_humble_lidar_deskew_acceptance.py");
            var textBoundaries = Slice(adapterValidator, "def check_text_boundaries", "\n\ndef check_sample_assets");
            var launch = Slice(smokeEnv, "def launch_rviz", "\n\ndef cached_qt_plugin_path");
            var qtCache = Slice(smokeEnv, "def cached_qt_plugin_path", "\n\ndef ");
            var writeProbe = Slice(phase160Smoke, "def write_probe_script", "\n\ndef run_probe");

            Check(!textBoundaries.Contains("runtime_inventory = RUNTIME_INVENTORY.read_text", StringComparison.Ordinal)
                  && !textBoundaries.Contains("+ \"\\n\" + runtime_inventory", StringComparison.Ordinal)
                  && textBoundaries.Contains("runtime_notices = RUNTIME_NOTICES.read_text", StringComparison.Ordinal),
                "164-30C-1: Humble adapter public-doc text scan excludes generated runtime inventory");
            Check(smokeEnv.Contains("_QT_PLUGIN_PATH_CACHE: dict[pathlib.Path, pathlib.Path | None] = {}", StringComparison.Ordinal)
                  && launch.Contains("cached_qt_plugin_path(ros2_root, qt_plugin_candidates)", StringComparison.Ordinal)
                  && qtCache.Contains("_QT_PLUGIN_PATH_CACHE[key] = next(", StringComparison.Ordinal),
                "164-30C-2: shared RViz launcher caches Qt plugin path resolution per ROS2 root");
            Check(writeProbe.Contains("text = probe_script_text()", StringComparison.Ordinal)
                  && writeProbe.Contains("if not path.exists() or path.read_text", StringComparison.Ordinal)
                  && writeProbe.Contains("path.write_text(text, encoding=\"utf-8\")", StringComparison.Ordinal),
                "164-30C-3: Phase160 probe script writes only when content changed");
        }

        private static void VerifyPhase160ValidationCachesRepoText()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuHumbleRuntimePackageValidation.cs");
            var readRepoText = PhaseValidationSourceHelpers.SourceMethod(source, "private static string ReadRepoText");

            Check(source.Contains("private static readonly Dictionary<string, string> FileTextCache", StringComparison.Ordinal)
                  && readRepoText.Contains("FileTextCache.TryGetValue(path, out var cached)", StringComparison.Ordinal)
                  && readRepoText.Contains("FileTextCache[path] = text;", StringComparison.Ordinal),
                "164-30D-1: Phase160 Humble validation caches repository text reads within the process");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-30\"", StringComparison.Ordinal), "164-30E-1: validation registry exposes Phase164-30");
            Check(project.Contains("Phase164_30Validation.cs", StringComparison.Ordinal), "164-30E-2: runtime validation project compiles Phase164-30");
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
