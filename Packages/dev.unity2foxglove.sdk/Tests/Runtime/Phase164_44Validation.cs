using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_44Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-44 Tests ---");
            _passed = 0;

            VerifyRegistryDispatchUsesFlagIndex();
            VerifyProgramUsesArgumentSetForContainsChecks();
            VerifySharedHelpersAvoidExtraLookupsAndAllocations();
            VerifyRegistry();

            Console.WriteLine("Phase 164-44: " + _passed + " checks passed.\n");
        }

        private static void VerifyRegistryDispatchUsesFlagIndex()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var find = SourceMethod(source, "Find(IReadOnlyCollection<string> args)");
            var findAll = SourceMethod(source, "FindAll(IReadOnlyCollection<string> args)");

            Check(source.Contains("IReadOnlyDictionary<string, PhaseValidationCase> FlagIndex", StringComparison.Ordinal)
                  && source.Contains("flagIndex.TryAdd(flag, item)", StringComparison.Ordinal),
                "164-44A-1: phase validation registry builds an immutable flag index");
            Check(find.Contains("FlagIndex.TryGetValue(arg, out var validation)", StringComparison.Ordinal)
                  && !find.Contains("All.FirstOrDefault(item => item.Matches(args))", StringComparison.Ordinal),
                "164-44A-2: phase validation Find uses indexed lookup instead of scanning all cases");
            Check(findAll.Contains("FlagIndex.TryGetValue(arg, out var validation)", StringComparison.Ordinal)
                  && findAll.Contains("new HashSet<PhaseValidationCase>()", StringComparison.Ordinal)
                  && !findAll.Contains("All.Where(item => item.Matches(args))", StringComparison.Ordinal),
                "164-44A-3: phase validation FindAll uses indexed lookup and de-duplicates aliases");
        }

        private static void VerifyProgramUsesArgumentSetForContainsChecks()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var main = SourceMethod(source, "MainCore(string[] args)");
            var registered = SourceMethod(source, "TryRunRegisteredValidation(List<string> argList, IReadOnlyCollection<string> argSet");

            Check(main.Contains("new HashSet<string>(argList, StringComparer.Ordinal)", StringComparison.Ordinal),
                "164-44B-1: Program.MainCore builds one argument set for contains-only dispatch checks");
            Check(main.Contains("argSet.Contains(\"--serve\")", StringComparison.Ordinal)
                  && main.Contains("argSet.Contains(\"--demo\")", StringComparison.Ordinal)
                  && main.Contains("argSet.Contains(\"--local-evidence\")", StringComparison.Ordinal),
                "164-44B-2: Program.MainCore uses argument-set lookup for repeated dispatch flags");
            Check(registered.Contains("argSet.Contains(\"--list-validations\")", StringComparison.Ordinal)
                  && registered.Contains("PhaseValidationRegistry.FindAll(argList)", StringComparison.Ordinal),
                "164-44B-3: registered validation dispatch uses the set for option checks while preserving arg order");
        }

        private static void VerifySharedHelpersAvoidExtraLookupsAndAllocations()
        {
            var reflection = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationReflectionHelpers.cs");
            var sourceHelpers = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationSourceHelpers.cs");
            var readCamera = SourceMethod(sourceHelpers, "ReadCameraPublisherSources()");

            Check(!reflection.Contains("TypeCache.ContainsKey(fullName)", StringComparison.Ordinal)
                  && !reflection.Contains("return TypeCache[fullName]", StringComparison.Ordinal)
                  && reflection.Contains("TypeCache.TryGetValue(fullName, out var cached)", StringComparison.Ordinal)
                  && reflection.Contains("return resolved;", StringComparison.Ordinal),
                "164-44C-1: reflection helper miss path avoids redundant dictionary lookups");
            Check(sourceHelpers.Contains("using System.Text;", StringComparison.Ordinal)
                  && readCamera.Contains("new StringBuilder()", StringComparison.Ordinal)
                  && readCamera.Contains("source.Append(File.ReadAllText(file))", StringComparison.Ordinal)
                  && !readCamera.Contains("string.Join(Environment.NewLine, files.Select(File.ReadAllText))", StringComparison.Ordinal),
                "164-44C-2: shared camera source reader avoids string.Join over read-all-text enumeration");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-44\"", StringComparison.Ordinal), "164-44D-1: validation registry exposes Phase164-44");
            Check(project.Contains("Phase164_44Validation.cs", StringComparison.Ordinal), "164-44D-2: runtime validation project compiles Phase164-44");
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
