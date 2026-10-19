// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates package metadata (package.json, LICENSE), .gitignore build artifact coverage, CI workflows, and asmdef consistency.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
// Phase16 validates generated-source provenance, so the runtime test project
// links the Editor/Shared emitter source in FoxgloveSdk.Tests.csproj.
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase16Validation.
    /// </summary>
    public static partial class Phase16Validation
    {
        private static readonly Lazy<string> RepoRoot = new Lazy<string>(TestRepoRootLocator.FindRepoRoot);
        private static readonly Regex PackageVersionRegex = new Regex(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 16 Tests ---");

            var repoRoot = FindRepoRoot();
            if (repoRoot == null)
            {
                Console.WriteLine("[WARN] Could not find repo root - skipping path-based checks.");
                return;
            }

            var pkgDir = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk");

            // --- 16A: Package metadata ---
            var pkgJson = Path.Combine(pkgDir, "package.json");
            Assert(File.Exists(pkgJson), $"package.json exists at {pkgJson}");

            var json = File.ReadAllText(pkgJson);
            Assert(json.Contains("\"name\": \"dev.unity2foxglove.sdk\""), "package.json name correct");
            var packageVersion = ExtractPackageJsonString(json, "version");
            Assert(packageVersion != null && PackageVersionRegex.IsMatch(packageVersion), "package.json version is valid semver");
            Assert(json.Contains("\"displayName\": \"Unity2Foxglove SDK\""), "package.json displayName correct");
            Assert(json.Contains("\"license\": \"Apache-2.0\""), "package.json license is Apache-2.0");

            // --- 16A: LICENSE files ---
            var pkgLicense = Path.Combine(pkgDir, "LICENSE");
            Assert(File.Exists(pkgLicense), $"Package LICENSE exists at {pkgLicense}");
            var rootLicense = Path.Combine(repoRoot, "LICENSE");
            Assert(File.Exists(rootLicense), $"Root LICENSE exists at {rootLicense}");

            // --- 16C: .gitignore covers build artifacts ---
            var gitignorePath = Path.Combine(repoRoot, ".gitignore");
            Assert(File.Exists(gitignorePath), ".gitignore exists");
            var gitignore = File.ReadAllText(gitignorePath);
            Assert(gitignore.Contains("bin/") || gitignore.Contains("**/bin/"), ".gitignore covers bin/");
            Assert(gitignore.Contains("obj/") || gitignore.Contains("**/obj/"), ".gitignore covers obj/");
            Assert(gitignore.Contains("build/"), ".gitignore covers build/");
            ValidatePackageBuildOutputsAbsent(repoRoot);

            // --- 16D: CI workflows ---
            var ciDir = Path.Combine(repoRoot, ".github", "workflows");
            Assert(Directory.Exists(ciDir), ".github/workflows/ exists");
            Assert(File.Exists(Path.Combine(ciDir, "dotnet-tests.yml")), "dotnet-tests.yml exists");
            Assert(File.Exists(Path.Combine(ciDir, "package-check.yml")), "package-check.yml exists");

            // --- asmdef consistency ---
            var asmdefPath = Path.Combine(pkgDir, "Runtime", "Unity.FoxgloveSDK.asmdef");
            Assert(File.Exists(asmdefPath), "Runtime asmdef exists");
            var asmdef = File.ReadAllText(asmdefPath);
            Assert(asmdef.Contains("\"name\": \"Unity.FoxgloveSDK\""), "asmdef name matches namespace");

            ValidateRepositoryHeaders(repoRoot);
            ValidateWorkflowScriptReferences(repoRoot);
            ValidateDocsWorkflowCoverage(repoRoot);
            ValidatePublicDocsScriptReferences(repoRoot);
            ValidatePrivateWorkspaceBoundaries(repoRoot);
            ValidateResearchDoiPolicy(repoRoot);
            ValidatePackageContractReviewFindings(repoRoot);
            ValidateGeneratedSourceProvenance(repoRoot);
            ValidateThirdPartyNotices(repoRoot);

            Console.WriteLine("Phase 16: All checks passed.");
        }

        static void ValidatePackageBuildOutputsAbsent(string repoRoot)
        {
            var packagesDir = Path.Combine(repoRoot, "Packages");
            Assert(Directory.Exists(packagesDir), "Packages directory exists");

            var leakedDirectories = EnumeratePackageBuildOutputDirectories(packagesDir)
                .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
                .ToArray();
            if (leakedDirectories.Length > 0)
                Array.Sort(leakedDirectories, StringComparer.Ordinal);

            Assert(leakedDirectories.Length == 0,
                "Packages contains no bin/ or obj/ build output directories: " + string.Join(", ", leakedDirectories));
        }

        static IEnumerable<string> EnumeratePackageBuildOutputDirectories(string packagesDir)
        {
            return Directory.EnumerateDirectories(packagesDir, "bin", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateDirectories(packagesDir, "obj", SearchOption.AllDirectories));
        }

        static void ValidateRepositoryHeaders(string repoRoot)
        {
            var requiredHeaderFiles = new[]
            {
                "Scripts/architecture/analyze_coupling.py",
                "Scripts/unity_build/unity_il2cpp.py",
                "Scripts/performance/run_baseline.py",
                "Scripts/release/bump_version.py",
                "Scripts/package/validate_unity_package.py",
                "Scripts/samples/sync_full_demo.py",
                "Scripts/schema/generate_ros2_msg_schema_catalog.py",
                "Scripts/smoke/mcap/phase34_attachment_mcap.py",
                "Scripts/smoke/mcap/phase44_all_schemas_mcap.py",
                "Scripts/smoke/websocket/phase40_slow_camera_client.py",
                "Scripts/smoke/websocket/tf_websocket_smoke.py",
                "Scripts/smoke/assets/fetch_asset_smoke.py",
                "Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs",
            };

            foreach (var relativePath in requiredHeaderFiles)
            {
                var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert(File.Exists(path), $"{relativePath} exists");
                var text = PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
                Assert(text.Contains("Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors."),
                    $"{relativePath} has Unity2Foxglove copyright header");
                Assert(text.Contains("SPDX-License-Identifier: Apache-2.0"),
                    $"{relativePath} has Apache-2.0 SPDX header");

                if (relativePath.EndsWith(".py", StringComparison.Ordinal))
                {
                    Assert(text.Contains("# Purpose:"), $"{relativePath} has purpose header");
                    Assert(text.Contains("# Usage:"), $"{relativePath} has usage header");
                    Assert(text.Contains("# Inputs:"), $"{relativePath} has inputs header");
                    Assert(text.Contains("# Outputs:"), $"{relativePath} has outputs header");
                }
            }

            ValidatePythonDocstrings(repoRoot);

            var scriptsDir = Path.Combine(repoRoot, "Scripts");
            var powershellScripts = Directory.GetFiles(scriptsDir, "*.ps1", SearchOption.AllDirectories);
            Assert(powershellScripts.Length == 0, "Scripts contains no PowerShell-only helper scripts");

            var rootPythonScripts = Directory.GetFiles(scriptsDir, "*.py", SearchOption.TopDirectoryOnly);
            Assert(rootPythonScripts.Length == 0, "Scripts root contains no loose Python helper scripts");

            var legacyTestsDir = Path.Combine(repoRoot, "Unity2Foxglove", "Tests");
            Assert(!Directory.Exists(legacyTestsDir), "Unity2Foxglove/Tests legacy script folder removed");
        }

        static void ValidatePythonDocstrings(string repoRoot)
        {
            var scriptsDir = Path.Combine(repoRoot, "Scripts");
            foreach (var path in Directory.EnumerateFiles(scriptsDir, "*.py", SearchOption.AllDirectories))
            {
                AssertPythonDefinitionsHaveDocstrings(repoRoot, path);
            }
        }

        static string ExtractPackageJsonString(string json, string propertyName)
        {
            var pattern = "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"([^\"]+)\"";
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        static void AssertPythonDefinitionsHaveDocstrings(string repoRoot, string path)
        {
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].TrimStart();
                if (!IsPythonDefinitionStart(trimmed))
                    continue;

                var declarationEnd = FindPythonDeclarationEnd(lines, index);
                var relativePath = Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                var hasInlineDocstring = HasInlinePythonDocstring(lines[declarationEnd]);
                var docLine = hasInlineDocstring ? declarationEnd : FindNextNonEmptyLine(lines, declarationEnd + 1);
                Assert(hasInlineDocstring || (docLine >= 0 && IsPythonDocstringLine(lines[docLine].TrimStart())),
                    $"{relativePath}:{index + 1} has a docstring");
            }
        }

        static bool IsPythonDefinitionStart(string trimmedLine)
        {
            return trimmedLine.StartsWith("def ", StringComparison.Ordinal)
                || trimmedLine.StartsWith("async def ", StringComparison.Ordinal)
                || trimmedLine.StartsWith("class ", StringComparison.Ordinal);
        }

        static int FindPythonDeclarationEnd(IReadOnlyList<string> lines, int start)
        {
            var parenDepth = 0;
            var quote = '\0';
            var tripleQuoted = false;
            var escaped = false;
            for (var index = start; index < lines.Count; index++)
            {
                CountPythonParensOutsideStrings(lines[index], ref parenDepth, ref quote, ref tripleQuoted, ref escaped);

                if (parenDepth <= 0 && PythonLineEndsWithDeclarationColonOutsideStrings(lines[index]))
                    return index;
            }

            return start;
        }

        static void CountPythonParensOutsideStrings(
            string line,
            ref int parenDepth,
            ref char quote,
            ref bool tripleQuoted,
            ref bool escaped)
        {
            for (var index = 0; index < line.Length; index++)
            {
                var ch = line[index];
                if (quote != '\0')
                {
                    if (!tripleQuoted && escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (!tripleQuoted && ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (tripleQuoted)
                    {
                        if (ch == quote && index + 2 < line.Length && line[index + 1] == quote && line[index + 2] == quote)
                        {
                            quote = '\0';
                            tripleQuoted = false;
                            index += 2;
                        }
                    }
                    else if (ch == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (ch == '#')
                    break;

                if (ch == '"' || ch == '\'')
                {
                    quote = ch;
                    tripleQuoted = index + 2 < line.Length && line[index + 1] == ch && line[index + 2] == ch;
                    if (tripleQuoted)
                        index += 2;
                    continue;
                }

                if (ch == '(')
                    parenDepth++;
                else if (ch == ')')
                    parenDepth--;
            }

            if (!tripleQuoted)
                escaped = false;
        }

        static bool PythonLineEndsWithDeclarationColonOutsideStrings(string line)
        {
            var quote = '\0';
            var tripleQuoted = false;
            var escaped = false;
            var lastOutsideString = '\0';

            for (var index = 0; index < line.Length; index++)
            {
                var ch = line[index];
                if (quote != '\0')
                {
                    if (!tripleQuoted && escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (!tripleQuoted && ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (tripleQuoted)
                    {
                        if (ch == quote && index + 2 < line.Length && line[index + 1] == quote && line[index + 2] == quote)
                        {
                            quote = '\0';
                            tripleQuoted = false;
                            index += 2;
                        }
                    }
                    else if (ch == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (ch == '#')
                    break;

                if (ch == '"' || ch == '\'')
                {
                    quote = ch;
                    tripleQuoted = index + 2 < line.Length && line[index + 1] == ch && line[index + 2] == ch;
                    if (tripleQuoted)
                        index += 2;
                    continue;
                }

                if (!char.IsWhiteSpace(ch))
                    lastOutsideString = ch;
            }

            return lastOutsideString == ':';
        }

        static int FindNextNonEmptyLine(IReadOnlyList<string> lines, int start)
        {
            for (var index = start; index < lines.Count; index++)
            {
                if (!string.IsNullOrWhiteSpace(lines[index]))
                    return index;
            }

            return -1;
        }
    }
}
