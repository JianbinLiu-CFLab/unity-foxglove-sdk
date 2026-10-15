using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_40Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-40 Tests ---");
            _passed = 0;

            VerifyPackageValidatorReusesCollectedPathsAndCombinedScan();
            VerifySourceGeneratorValidatorUsesHashComparison();
            VerifyPackageCiValidatorsRunConcurrently();
            VerifyLocalEntrypointValidatorUsesGitGrep();
            VerifyVersionBumpReadsPackageJsonOnceInRun();
            VerifyRegistry();

            Console.WriteLine("Phase 164-40: " + _passed + " checks passed.\n");
        }

        private static void VerifyPackageValidatorReusesCollectedPathsAndCombinedScan()
        {
            var source = Read("Scripts/package/validate_unity_package.py");
            var main = PythonFunction(source, "def main() -> int:");
            var publicContent = PythonFunction(source, "def check_forbidden_public_content(");
            var buildArtifacts = PythonFunction(source, "def check_package_build_artifacts(");

            Check(source.Contains("FORBIDDEN_PUBLIC_SCAN_PATTERN", StringComparison.Ordinal)
                  && publicContent.Contains("FORBIDDEN_PUBLIC_SCAN_PATTERN.finditer(text)", StringComparison.Ordinal),
                "164-40A-1: package validator uses one combined forbidden-content scan per file");
            Check(main.Contains("package_entries = list(PACKAGE.rglob(\"*\"))", StringComparison.Ordinal)
                  && main.Contains("samples_entries = [path for path in package_entries", StringComparison.Ordinal)
                  && main.Contains("docs_files = [path for path in package_entries", StringComparison.Ordinal),
                "164-40A-2: package validator precollects package paths once for samples and docs");
            Check(buildArtifacts.Contains("package_entries = package_entries if package_entries is not None", StringComparison.Ordinal)
                  && !buildArtifacts.Contains("for path in PACKAGE.rglob(\"*\")", StringComparison.Ordinal),
                "164-40A-3: package build-artifact check reuses the precollected package tree");
            Check(source.Contains("samples_files: list[Path] | None = None", StringComparison.Ordinal)
                  && source.Contains("samples_entries: list[Path] | None = None", StringComparison.Ordinal),
                "164-40A-4: package validator keeps direct regression-test helper calls compatible");
        }

        private static void VerifySourceGeneratorValidatorUsesHashComparison()
        {
            var source = Read("Scripts/package/validate_source_generator_dll.py");
            var validate = PythonFunction(source, "def validate_or_update(");

            Check(validate.Contains("built_hash = sha256(built_dll)", StringComparison.Ordinal)
                  && validate.Contains("checked_hash = sha256(CHECKED_IN_DLL)", StringComparison.Ordinal)
                  && validate.Contains("if built_hash != checked_hash:", StringComparison.Ordinal)
                  && !validate.Contains("read_bytes() != CHECKED_IN_DLL.read_bytes()", StringComparison.Ordinal),
                "164-40B-1: source generator freshness compares existing SHA-256 values instead of rereading DLLs");
            Check(source.Contains("def run_build(build_output_dir: Path = BUILD_OUTPUT_DIR", StringComparison.Ordinal),
                "164-40B-2: source generator validator keeps default run_build arguments for regression tests");
        }

        private static void VerifyPackageCiValidatorsRunConcurrently()
        {
            var source = Read("Scripts/release/run_ci.py");
            var parallel = PythonFunction(source, "def run_parallel(");
            var main = PythonFunction(source, "def main() -> int:");

            Check(source.Contains("from concurrent.futures import ThreadPoolExecutor, as_completed", StringComparison.Ordinal)
                  && parallel.Contains("ThreadPoolExecutor(max_workers=len(commands))", StringComparison.Ordinal)
                  && parallel.Contains("as_completed(futures)", StringComparison.Ordinal),
                "164-40C-1: run_ci can execute independent package validators concurrently");
            Check(main.Contains("package_results = run_parallel([", StringComparison.Ordinal)
                  && main.Contains("validate_unity_package.py", StringComparison.Ordinal)
                  && main.Contains("validate_ros2forunity_package.py", StringComparison.Ordinal),
                "164-40C-2: run_ci package suite dispatches validators through the parallel runner");
            Check(parallel.Contains("for label, _ in commands:", StringComparison.Ordinal)
                  && parallel.Contains("ordered_results[label] = ok", StringComparison.Ordinal),
                "164-40C-3: run_ci replays parallel validator output in declaration order");
        }

        private static void VerifyLocalEntrypointValidatorUsesGitGrep()
        {
            var source = Read("Scripts/package/validate_local_entrypoints.py");

            Check(source.Contains("def git_grep_failures", StringComparison.Ordinal)
                  && source.Contains("\"git\",", StringComparison.Ordinal)
                  && source.Contains("\"grep\",", StringComparison.Ordinal)
                  && source.Contains("\":(glob)Scripts/**/*.py\"", StringComparison.Ordinal),
                "164-40D-1: local entrypoint validator uses git grep over tracked script pathspecs");
            Check(!source.Contains("path.read_text", StringComparison.Ordinal)
                  && !source.Contains("git\", \"ls-files\"", StringComparison.Ordinal),
                "164-40D-2: local entrypoint validator avoids Python-side tracked-script file reads");
        }

        private static void VerifyVersionBumpReadsPackageJsonOnceInRun()
        {
            var source = Read("Scripts/release/bump_version.py");
            var run = PythonFunction(source, "def run(self) -> int:");

            Check(source.Contains("def package_json_path(self) -> Path:", StringComparison.Ordinal)
                  && source.Contains("def package_version(self, text: str | None = None", StringComparison.Ordinal)
                  && source.Contains("def replace_version_property(self, old_version: str, text: str | None = None", StringComparison.Ordinal),
                "164-40E-1: bump_version helpers can reuse an already-read package.json text buffer");
            Check(run.Contains("package_json_text = self.read(package_json)", StringComparison.Ordinal)
                  && run.Contains("old_version = self.package_version(package_json_text, package_json)", StringComparison.Ordinal)
                  && run.Contains("self.replace_version_property(old_version, package_json_text, package_json)", StringComparison.Ordinal),
                "164-40E-2: bump_version run reads package.json once for version extraction and replacement");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-40\"", StringComparison.Ordinal), "164-40F-1: validation registry exposes Phase164-40");
            Check(project.Contains("Phase164_40Validation.cs", StringComparison.Ordinal), "164-40F-2: runtime validation project compiles Phase164-40");
        }

        private static string PythonFunction(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] missing Python function: " + signature);
            var next = source.IndexOf("\ndef ", start + signature.Length, StringComparison.Ordinal);
            return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
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
