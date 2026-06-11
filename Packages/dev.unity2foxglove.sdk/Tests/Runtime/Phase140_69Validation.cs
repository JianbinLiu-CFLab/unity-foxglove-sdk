// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-69 source-shape regression coverage for FoxRun generation host optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_69Validation.
    /// </summary>
    public static class Phase140_69Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-69: FoxRun Generation Hosts and Source Generators Optimization ===");
            _passed = 0;

            VerifyRoslynGeneratorHotPaths();
            VerifyEditorGeneratorHotPaths();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-69: {_passed} checks passed.");
        }

        private static void VerifyRoslynGeneratorHotPaths()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var hasFoxRunAttr = Slice(source, "private static bool HasFoxRunAttr", "private static MemberData ExtractMember");
            Check(hasFoxRunAttr.Contains("AttrAttributeName", StringComparison.Ordinal)
                  && hasFoxRunAttr.Contains("AttrQualifiedNameSuffix", StringComparison.Ordinal)
                  && hasFoxRunAttr.Contains("AttrQualifiedAttributeNameSuffix", StringComparison.Ordinal)
                  && hasFoxRunAttr.Contains("StringComparison.Ordinal", StringComparison.Ordinal)
                  && !hasFoxRunAttr.Contains("AttrShortName +", StringComparison.Ordinal),
                "140-69A-1: FoxRun attribute syntax filter reuses precomputed names and suffixes");

            var extractMember = Slice(source, "private static MemberData ExtractMember", "private static bool TryReadFloatConstant");
            Check(!extractMember.Contains(".Where(a => a.AttributeClass?.ToDisplayString() == AttrFullName)", StringComparison.Ordinal)
                  && !extractMember.Contains(".ToList()", StringComparison.Ordinal),
                "140-69A-2: Roslyn semantic extraction avoids per-candidate LINQ attribute lists");

            var generate = Slice(source, "private static void Generate", "private static void EmitClass");
            Check(generate.Contains("AppendRoslynMembers", StringComparison.Ordinal)
                  && !generate.Contains("items.Where", StringComparison.Ordinal)
                  && !generate.Contains("SelectMany(m => m.ToRoslynMembers()).ToList()", StringComparison.Ordinal)
                  && !generate.Contains(".GroupBy(m => (m.Ns, m.ClassName))", StringComparison.Ordinal),
                "140-69A-3: Roslyn source output builds validation state in one pass");

            var toRoslynMembers = Slice(source, "public IReadOnlyList<FoxRunRoslynGenerationMember> ToRoslynMembers", "        }\r\n\r\n        /// <summary>\r\n        /// Immutable tuple");
            Check(toRoslynMembers.Contains("AppendRoslynMembers", StringComparison.Ordinal)
                  && !toRoslynMembers.Contains("Topics.Select", StringComparison.Ordinal)
                  && !toRoslynMembers.Contains(".ToList()", StringComparison.Ordinal),
                "140-69A-4: MemberData Roslyn member conversion avoids LINQ projection lists");
        }

        private static void VerifyEditorGeneratorHotPaths()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var collectTypes = Slice(source, "public static List<(string AsmName, string Ns, string ClassName)> CollectFoxRunTypes()", "        /// <summary>\r\n        /// Generate an IL2CPP link.xml snippet");
            var scanMembers = Slice(source, "private static FoxRunScanResult ScanFoxRunMembers", "        /// <summary>\r\n        /// Checks whether a type was declared");
            Check(source.Contains("AppDomain.CurrentDomain.GetAssemblies()", StringComparison.Ordinal)
                  && source.Contains("typeof(MonoBehaviour).IsAssignableFrom(type)", StringComparison.Ordinal)
                  && source.Contains("ReflectionTypeLoadException", StringComparison.Ordinal)
                  && !source.Contains("TypeCache.GetTypesDerivedFrom<MonoBehaviour>()", StringComparison.Ordinal),
                "140-69B-1: Editor FoxRun scans preserve reliable AppDomain discovery for live topics");
            Check(collectTypes.Contains("AppDomain.CurrentDomain.GetAssemblies()", StringComparison.Ordinal)
                  && collectTypes.Contains("typeof(MonoBehaviour).IsAssignableFrom(type)", StringComparison.Ordinal)
                  && scanMembers.Contains("AppDomain.CurrentDomain.GetAssemblies()", StringComparison.Ordinal)
                  && scanMembers.Contains("typeof(MonoBehaviour).IsAssignableFrom(type)", StringComparison.Ordinal),
                "140-69B-1b: both FoxRun type and member discovery use loaded-assembly semantics, not Unity TypeCache semantics");

            Check(!scanMembers.Contains("members.Select(member => member.ToManifestMember())", StringComparison.Ordinal)
                  && !scanMembers.Contains("members.Select(member => member.ToReflectionMember())", StringComparison.Ordinal),
                "140-69B-2: Editor scan projects manifest and reflection members in the main member loop");

            var validate = Slice(source, "private static void ValidateGenerationModel", "private static string GetManifestOutputDirectory");
            Check(!validate.Contains("diagnostics.Where", StringComparison.Ordinal),
                "140-69B-3: generation model validation classifies diagnostics in one pass");

            var emitSourceFile = Slice(source, "public static string EmitSourceFile(MemberData[] members)", "public static string EmitSourceFile(FoxRunGenerationType type)");
            Check(!emitSourceFile.Contains("members.Select(member => member.ToReflectionMember()).ToList()", StringComparison.Ordinal),
                "140-69B-4: physical source emission converts reflection members without LINQ projection lists");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_69Validation.cs", StringComparison.Ordinal),
                "140-69C-1: test project compiles Phase140_69Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-69\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_69Validation.Validate", StringComparison.Ordinal),
                "140-69C-2: validation registry exposes --phase140-69");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static string Slice(string source, string startText, string endText)
        {
            var start = source.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Could not locate source slice start: " + startText);
            var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;
            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
