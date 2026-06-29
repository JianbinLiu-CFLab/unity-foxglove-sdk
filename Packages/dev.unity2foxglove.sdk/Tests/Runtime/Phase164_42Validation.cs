using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_42Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-42 Tests ---");
            _passed = 0;

            VerifyRos2OptVendorPathCache();
            VerifyRegistry();

            Console.WriteLine("Phase 164-42: " + _passed + " checks passed.\n");
        }

        private static void VerifyRos2OptVendorPathCache()
        {
            var source = Read("Scripts/smoke/ros2/_ros2_windows_env.py");
            var resolver = PythonFunction(source, "def ros2_opt_bin_paths(");

            Check(source.Contains("_ROS2_OPT_BIN_PATHS_CACHE: dict[pathlib.Path, tuple[pathlib.Path, ...]] = {}", StringComparison.Ordinal),
                "164-42A-1: ROS2 smoke env caches opt vendor bin path probes per ROS2 root");
            Check(resolver.Contains("cached = _ROS2_OPT_BIN_PATHS_CACHE.get(key)", StringComparison.Ordinal)
                  && resolver.Contains("if cached is not None:", StringComparison.Ordinal)
                  && resolver.Contains("return list(cached)", StringComparison.Ordinal),
                "164-42A-2: ROS2 opt vendor path resolver returns cached list copies on repeat calls");
            Check(resolver.Contains("_ROS2_OPT_BIN_PATHS_CACHE[key] = ()", StringComparison.Ordinal)
                  && resolver.Contains("_ROS2_OPT_BIN_PATHS_CACHE[key] = tuple(result)", StringComparison.Ordinal),
                "164-42A-3: ROS2 opt vendor path resolver caches both missing and discovered opt trees");
            Check(source.Contains("def cached_qt_plugin_path", StringComparison.Ordinal)
                  && source.Contains("_QT_PLUGIN_PATH_CACHE", StringComparison.Ordinal),
                "164-42A-4: RViz Qt plugin path cache remains separate from ROS2 opt vendor cache");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-42\"", StringComparison.Ordinal), "164-42B-1: validation registry exposes Phase164-42");
            Check(project.Contains("Phase164_42Validation.cs", StringComparison.Ordinal), "164-42B-2: runtime validation project compiles Phase164-42");
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
