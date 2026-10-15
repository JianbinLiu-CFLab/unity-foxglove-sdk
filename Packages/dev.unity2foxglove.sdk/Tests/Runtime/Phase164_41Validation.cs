using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_41Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-41 Tests ---");
            _passed = 0;

            VerifyCoreSmokeModuleAndSourceCaches();
            VerifyPhase110ImportUsesSharedLoader();
            VerifyRegistry();

            Console.WriteLine("Phase 164-41: " + _passed + " checks passed.\n");
        }

        private static void VerifyCoreSmokeModuleAndSourceCaches()
        {
            var source = Read("Scripts/smoke/test_core_smoke_scripts.py");
            var loader = PythonFunction(source, "def load_smoke_module(");

            Check(source.Contains("_MODULE_CACHE = {}", StringComparison.Ordinal)
                  && loader.Contains("cached = _MODULE_CACHE.get(path)", StringComparison.Ordinal)
                  && loader.Contains("sys.modules[name] = cached", StringComparison.Ordinal)
                  && loader.Contains("_MODULE_CACHE[path] = module", StringComparison.Ordinal),
                "164-41A-1: core smoke loader caches executed modules by resolved file path");
            Check(!loader.Contains("original_path = list(sys.path)", StringComparison.Ordinal)
                  && !loader.Contains("sys.path[:] = original_path", StringComparison.Ordinal),
                "164-41A-2: core smoke loader avoids full sys.path list-copy restore");
            Check(source.Contains("_SOURCE_CACHE = {}", StringComparison.Ordinal)
                  && source.Contains("def read_source(relative: str) -> str:", StringComparison.Ordinal)
                  && source.Contains("def read_repo_source(relative: str) -> str:", StringComparison.Ordinal),
                "164-41A-3: core smoke source-shape tests cache static file reads");
        }

        private static void VerifyPhase110ImportUsesSharedLoader()
        {
            var source = Read("Scripts/smoke/test_core_smoke_scripts.py");
            var test = PythonFunction(source, "    def test_phase110_import_does_not_exit_when_ros2_env_helper_is_missing");

            Check(test.Contains("load_smoke_module(", StringComparison.Ordinal)
                  && test.Contains("include_sibling_path=False", StringComparison.Ordinal)
                  && test.Contains("excluded_paths=(SMOKE / \"ros2\",)", StringComparison.Ordinal),
                "164-41B-1: Phase110 import-missing-helper test uses the shared loader exclusion path");
            Check(!test.Contains("original_path = list(sys.path)", StringComparison.Ordinal)
                  && !test.Contains("sys.path = [entry for entry in sys.path", StringComparison.Ordinal),
                "164-41B-2: Phase110 import test no longer duplicates sys.path copy/filter logic");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-41\"", StringComparison.Ordinal), "164-41C-1: validation registry exposes Phase164-41");
            Check(project.Contains("Phase164_41Validation.cs", StringComparison.Ordinal), "164-41C-2: runtime validation project compiles Phase164-41");
        }

        private static string PythonFunction(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] missing Python function: " + signature);
            var next = source.IndexOf("\n    def ", start + signature.Length, StringComparison.Ordinal);
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
