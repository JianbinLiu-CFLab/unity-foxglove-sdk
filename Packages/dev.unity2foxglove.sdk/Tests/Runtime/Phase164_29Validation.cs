using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_29Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-29 Tests ---");
            _passed = 0;

            VerifyRuntimeSelectionGovernanceUsesCachedJsonPaths();
            VerifyRuntimeValidationHelpersAvoidRepeatedAllocation();
            VerifyR2fuPackageValidatorsCacheJsonLoads();
            VerifyRegistry();

            Console.WriteLine("Phase 164-29: " + _passed + " checks passed.\n");
        }

        private static void VerifyRuntimeSelectionGovernanceUsesCachedJsonPaths()
        {
            var selection = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var inspector = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelectorInspector.cs");
            var drawActive = PhaseValidationSourceHelpers.SourceMethod(inspector, "public static void DrawActiveRuntimeSelector");
            var drawRestart = PhaseValidationSourceHelpers.SourceMethod(inspector, "private static void DrawRestartStatus");
            var containsPackage = PhaseValidationSourceHelpers.SourceMethod(selection, "private static bool ContainsPackageName");
            var removeDependencies = PhaseValidationSourceHelpers.SourceMethod(selection, "private static string RemoveRuntimePackageDependencies");

            Check(Count(drawActive, "Ros2ForUnityRuntimeSelection.GetStatus(projectDirectory)") == 1
                  && drawActive.Contains("DrawRestartStatus(projectDirectory, status)", StringComparison.Ordinal)
                  && !drawRestart.Contains("GetStatus(projectDirectory)", StringComparison.Ordinal),
                "164-29A-1: R2FU inspector reuses one runtime status snapshot across restart rendering");
            Check(selection.Contains("_cachedCandidatesProjectDirectory", StringComparison.Ordinal)
                  && selection.Contains("_cachedManifestWriteTimeUtc", StringComparison.Ordinal)
                  && selection.Contains("_cachedManifestLength", StringComparison.Ordinal)
                  && selection.Contains("ReadManifestRuntimePackages(projectDirectory)", StringComparison.Ordinal),
                "164-29A-2: runtime candidate and manifest governance paths are cached by project and manifest state");
            Check(containsPackage.Contains("JObject.Parse(json ?? string.Empty)", StringComparison.Ordinal)
                  && !containsPackage.Contains("Regex", StringComparison.Ordinal),
                "164-29A-3: package identity checks parse package.json instead of scanning with regex");
            Check(removeDependencies.Contains("ReadManifestJson(manifest", StringComparison.Ordinal)
                  && removeDependencies.Contains("property.Remove();", StringComparison.Ordinal)
                  && !removeDependencies.Contains("Regex", StringComparison.Ordinal)
                  && !removeDependencies.Contains("Split(", StringComparison.Ordinal),
                "164-29A-4: manifest runtime dependency removal edits parsed JSON instead of regex-splitting text");
        }

        private static void VerifyRuntimeValidationHelpersAvoidRepeatedAllocation()
        {
            var phase107 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase107Validation.cs");
            var guard = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseRos2ForUnityValidationHelpers.cs");
            var optionalTokens = PhaseValidationSourceHelpers.SourceMethod(phase107, "private static IReadOnlyList<string> OptionalEditorForbiddenTokens");
            var allGuarded = PhaseValidationSourceHelpers.SourceMethod(guard, "public static bool AllR2fuReferencesAreGuarded");

            Check(phase107.Contains("private static readonly string[] OptionalEditorForbiddenTokenList", StringComparison.Ordinal)
                  && optionalTokens.Contains("=> OptionalEditorForbiddenTokenList;", StringComparison.Ordinal)
                  && !optionalTokens.Contains("new[]", StringComparison.Ordinal),
                "164-29B-1: optional editor forbidden token checks reuse a static token array");
            Check(allGuarded.Contains("using var reader = new StringReader(text);", StringComparison.Ordinal)
                  && allGuarded.Contains("while ((line = reader.ReadLine()) != null)", StringComparison.Ordinal)
                  && !allGuarded.Contains("Replace(\"\\r\\n\"", StringComparison.Ordinal)
                  && !allGuarded.Contains("Split('\\n')", StringComparison.Ordinal),
                "164-29B-2: R2FU guard validation scans source text line-by-line without allocating a split array");
        }

        private static void VerifyR2fuPackageValidatorsCacheJsonLoads()
        {
            foreach (var distro in new[] { "humble", "jazzy", "lyrical" })
            {
                var source = Read("Scripts/ros2forunity/windows/" + distro + "/validate_ros2forunity_package.py");
                var loadJson = Slice(source, "def load_json", "\n\ndef check_package_metadata");

                Check(source.Contains("JSON_CACHE: dict[Path, dict] = {}", StringComparison.Ordinal)
                      && loadJson.Contains("cached = JSON_CACHE.get(cache_key)", StringComparison.Ordinal)
                      && loadJson.Contains("return cached", StringComparison.Ordinal)
                      && loadJson.Contains("JSON_CACHE[cache_key] = data", StringComparison.Ordinal),
                    "164-29C-" + distro + ": " + distro + " package validator caches repeated JSON parses");
            }
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-29\"", StringComparison.Ordinal), "164-29D-1: validation registry exposes Phase164-29");
            Check(project.Contains("Phase164_29Validation.cs", StringComparison.Ordinal), "164-29D-2: runtime validation project compiles Phase164-29");
        }

        private static int Count(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
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
