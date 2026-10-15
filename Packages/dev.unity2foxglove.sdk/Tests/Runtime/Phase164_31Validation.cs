using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_31Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-31 Tests ---");
            _passed = 0;

            VerifyJazzyRuntimePathCaches();
            VerifyJazzyRuntimeValidatorFastPaths();
            VerifyJazzyBuildFastPaths();
            VerifyPhase161ValidationCachesRepoText();
            VerifyRegistry();

            Console.WriteLine("Phase 164-31: " + _passed + " checks passed.\n");
        }

        private static void VerifyJazzyRuntimePathCaches()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var getRosPath = Slice(source, "public static string GetRos2ForUnityPath()", "\n\n    private static string ComputeRos2ForUnityPath");
            var getPluginPath = Slice(source, "public static string GetPluginPath()", "\n\n    private static string ComputePluginPath");
            var normalize = Slice(source, "private static string NormalizeEnvPathEntry", "\n\n    public bool IsStandalone");

            Check(source.Contains("private static readonly Lazy<string> ros2ForUnityPath = new Lazy<string>(ComputeRos2ForUnityPath);", StringComparison.Ordinal)
                  && source.Contains("private static readonly Lazy<string> pluginPath = new Lazy<string>(ComputePluginPath);", StringComparison.Ordinal)
                  && getRosPath.Contains("return ros2ForUnityPath.Value;", StringComparison.Ordinal)
                  && getPluginPath.Contains("return pluginPath.Value;", StringComparison.Ordinal),
                "164-31A-1: Jazzy runtime caches resolved runtime and plugin paths");
            Check(source.Contains("private static string ComputeRos2ForUnityPath()", StringComparison.Ordinal)
                  && source.Contains("private static string ComputePluginPath()", StringComparison.Ordinal)
                  && source.Contains("PackageInfo.FindForAssetPath", StringComparison.Ordinal),
                "164-31A-2: Jazzy package path resolution remains package-manager aware behind the cache");
            Check(normalize.Contains("LooksNormalizedEnvPathEntry(fastNormalized)", StringComparison.Ordinal)
                  && normalize.Contains("Path.GetFullPath(trimmed)", StringComparison.Ordinal)
                  && normalize.Contains("private static bool LooksNormalizedEnvPathEntry", StringComparison.Ordinal),
                "164-31A-3: Jazzy PATH normalization has an absolute-path fast path before GetFullPath");
        }

        private static void VerifyJazzyRuntimeValidatorFastPaths()
        {
            var source = Read("Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py");
            var inventory = Slice(source, "def check_inventory", "\n\ndef check_runtime_files");
            var boundaries = Slice(source, "def check_package_boundaries", "\n\ndef core_runtime_has_forbidden_tokens");
            var coreScan = Slice(source, "def core_runtime_has_forbidden_tokens", "\n\ndef run_checks");
            var parseArgs = Slice(source, "def parse_args", "\n\ndef main");
            var main = Slice(source, "def main", "\n\nif __name__");

            Check(inventory.Contains("should_hash_dlls = release_gate or not skip_dll_hash", StringComparison.Ordinal)
                  && inventory.Contains("if should_hash_dlls and expected_hash and file_sha256(package_path) != expected_hash", StringComparison.Ordinal)
                  && inventory.Contains("skipped by fast validation; use --release-gate", StringComparison.Ordinal),
                "164-31B-1: Jazzy runtime validator can skip per-DLL hash reads outside release gate");
            Check(parseArgs.Contains("\"--fast\"", StringComparison.Ordinal)
                  && parseArgs.Contains("\"--skip-dll-hash\"", StringComparison.Ordinal)
                  && main.Contains("and not args.release_gate", StringComparison.Ordinal),
                "164-31B-2: Jazzy DLL hash skipping is explicit and release gate keeps full hashing");
            Check(boundaries.Contains("not core_runtime_has_forbidden_tokens()", StringComparison.Ordinal)
                  && coreScan.Contains("for path in iter_files(CORE_PACKAGE / \"Runtime\"):", StringComparison.Ordinal)
                  && coreScan.Contains("if any(token in text for token in tokens):", StringComparison.Ordinal)
                  && !boundaries.Contains("\"\\n\".join", StringComparison.Ordinal),
                "164-31B-3: Jazzy core SDK runtime boundary scan uses early exit instead of concatenating all files");
        }

        private static void VerifyJazzyBuildFastPaths()
        {
            var builder = Read("Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py");
            var ensureMeta = Slice(builder, "def ensure_generated_meta", "\n\ndef write_generated_metas");
            var writeMetas = Slice(builder, "def write_generated_metas", "\n\ndef package_json");

            Check(ensureMeta.Contains("existing_paths: set[str]", StringComparison.Ordinal)
                  && ensureMeta.Contains("if meta_key in existing_paths:", StringComparison.Ordinal)
                  && !ensureMeta.Contains("path_exists(meta)", StringComparison.Ordinal)
                  && writeMetas.Contains("keyed_paths = sorted((path.as_posix(), path) for path in package.rglob(\"*\"))", StringComparison.Ordinal)
                  && writeMetas.Contains("existing_paths = {key for key, _ in keyed_paths}", StringComparison.Ordinal),
                "164-31C-1: Jazzy meta generation reuses rglob and precomputed path keys");
        }

        private static void VerifyPhase161ValidationCachesRepoText()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuJazzyRuntimeRefreshValidation.cs");
            var readRepoText = PhaseValidationSourceHelpers.SourceMethod(source, "private static string ReadRepoText");

            Check(source.Contains("private static readonly Dictionary<string, string> FileTextCache", StringComparison.Ordinal)
                  && readRepoText.Contains("FileTextCache.TryGetValue(path, out var cached)", StringComparison.Ordinal)
                  && readRepoText.Contains("FileTextCache[path] = text;", StringComparison.Ordinal),
                "164-31D-1: Phase161 Jazzy validation caches repository text reads within the process");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-31\"", StringComparison.Ordinal), "164-31E-1: validation registry exposes Phase164-31");
            Check(project.Contains("Phase164_31Validation.cs", StringComparison.Ordinal), "164-31E-2: runtime validation project compiles Phase164-31");
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
