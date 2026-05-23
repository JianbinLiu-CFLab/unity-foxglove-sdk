// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates package metadata (package.json, LICENSE), .gitignore build artifact coverage, CI workflows, and asmdef consistency.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase16Validation
    {
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 16 Tests ---");

            var repoRoot = FindRepoRoot();
            if (repoRoot == null)
            {
                Console.WriteLine("[WARN] Could not find repo root — skipping path-based checks.");
                return;
            }

            var pkgDir = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk");

            // ── 16A: Package metadata ──
            var pkgJson = Path.Combine(pkgDir, "package.json");
            Assert(File.Exists(pkgJson), $"package.json exists at {pkgJson}");

            var json = File.ReadAllText(pkgJson);
            Assert(json.Contains("\"name\": \"dev.unity2foxglove.sdk\""), "package.json name correct");
            Assert(json.Contains("\"version\": \"1.9.1\""), "package.json version is 1.9.1");
            Assert(json.Contains("\"displayName\": \"Unity2Foxglove SDK\""), "package.json displayName correct");
            Assert(json.Contains("\"license\": \"Apache-2.0\""), "package.json license is Apache-2.0");

            // ── 16A: LICENSE files ──
            var pkgLicense = Path.Combine(pkgDir, "LICENSE");
            Assert(File.Exists(pkgLicense), $"Package LICENSE exists at {pkgLicense}");
            var rootLicense = Path.Combine(repoRoot, "LICENSE");
            Assert(File.Exists(rootLicense), $"Root LICENSE exists at {rootLicense}");

            // ── 16C: .gitignore covers build artifacts ──
            var gitignorePath = Path.Combine(repoRoot, ".gitignore");
            Assert(File.Exists(gitignorePath), ".gitignore exists");
            var gitignore = File.ReadAllText(gitignorePath);
            Assert(gitignore.Contains("bin/") || gitignore.Contains("**/bin/"), ".gitignore covers bin/");
            Assert(gitignore.Contains("obj/") || gitignore.Contains("**/obj/"), ".gitignore covers obj/");
            Assert(gitignore.Contains("build/"), ".gitignore covers build/");

            // ── 16D: CI workflows ──
            var ciDir = Path.Combine(repoRoot, ".github", "workflows");
            Assert(Directory.Exists(ciDir), ".github/workflows/ exists");
            Assert(File.Exists(Path.Combine(ciDir, "dotnet-tests.yml")), "dotnet-tests.yml exists");
            Assert(File.Exists(Path.Combine(ciDir, "package-check.yml")), "package-check.yml exists");

            // ── asmdef consistency ──
            var asmdefPath = Path.Combine(pkgDir, "Runtime", "Unity.FoxgloveSDK.asmdef");
            Assert(File.Exists(asmdefPath), "Runtime asmdef exists");
            var asmdef = File.ReadAllText(asmdefPath);
            Assert(asmdef.Contains("\"name\": \"Unity.FoxgloveSDK\""), "asmdef name matches namespace");

            ValidateRepositoryHeaders(repoRoot);
            ValidateWorkflowScriptReferences(repoRoot);
            ValidateDocsWorkflowCoverage(repoRoot);
            ValidatePublicDocsScriptReferences(repoRoot);
            ValidateResearchDoiPolicy(repoRoot);
            ValidateGeneratedSourceProvenance(repoRoot);
            ValidateThirdPartyNotices(repoRoot);

            Console.WriteLine("Phase 16: All checks passed.");
        }

        static void ValidateRepositoryHeaders(string repoRoot)
        {
            var requiredHeaderFiles = new[]
            {
                "Scripts/build_tools/unity_il2cpp.py",
                "Scripts/performance/run_baseline.py",
                "Scripts/release/bump_version.py",
                "Scripts/release/validate_package.py",
                "Scripts/samples/sync_full_demo.py",
                "Scripts/schema/generate_ros2_msg_schema_catalog.py",
                "Scripts/smoke/phase34_attachment_mcap.py",
                "Scripts/smoke/phase44_all_schemas_mcap.py",
                "Scripts/smoke/phase40_slow_camera_client.py",
                "Scripts/smoke/tf_websocket_smoke.py",
                "Scripts/smoke/fetch_asset_smoke.py",
                "Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs",
            };

            foreach (var relativePath in requiredHeaderFiles)
            {
                var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert(File.Exists(path), $"{relativePath} exists");
                var text = File.ReadAllText(path);
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

            var phase32Path = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime", "Phase32Validation.cs");
            var phase32 = File.ReadAllText(phase32Path);
            Assert(phase32.Contains("// Module: Tests/Runtime"), "Phase32Validation.cs has module header");
            Assert(phase32.Contains("// Purpose:"), "Phase32Validation.cs has purpose header");

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
            var pythonFiles = Directory.GetFiles(scriptsDir, "*.py", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            foreach (var path in pythonFiles)
            {
                AssertPythonDefinitionsHaveDocstrings(repoRoot, path);
            }
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
                var docLine = FindNextNonEmptyLine(lines, declarationEnd + 1);
                var relativePath = Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                Assert(docLine >= 0 && IsPythonDocstringLine(lines[docLine].TrimStart()),
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
            for (var index = start; index < lines.Count; index++)
            {
                foreach (var ch in lines[index])
                {
                    if (ch == '(')
                        parenDepth++;
                    else if (ch == ')')
                        parenDepth--;
                }

                if (parenDepth <= 0 && lines[index].TrimEnd().EndsWith(":", StringComparison.Ordinal))
                    return index;
            }

            return start;
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

        static bool IsPythonDocstringLine(string trimmedLine)
        {
            return trimmedLine.StartsWith("\"\"\"", StringComparison.Ordinal)
                || trimmedLine.StartsWith("'''", StringComparison.Ordinal);
        }

        static void ValidateWorkflowScriptReferences(string repoRoot)
        {
            var workflowsDir = Path.Combine(repoRoot, ".github", "workflows");
            var workflowFiles = Directory.GetFiles(workflowsDir, "*.yml", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(workflowsDir, "*.yaml", SearchOption.TopDirectoryOnly))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var stalePaths = new[]
            {
                "Scripts/validate_release_package.py",
                "Scripts/build_unity_il2cpp.py",
                "Scripts/bump_version.py",
                "Scripts/run_performance_baseline.py",
                "Scripts/sync_full_demo_sample.py",
                "Scripts/smoke/generate_phase34_attachment_smoke.ps1",
                "Scripts/smoke/generate_phase44_all_schemas_mcap.py",
                "Scripts/smoke/phase40_slow_camera_client.ps1",
                "Unity2Foxglove/Tests/test_ws.py",
                "Unity2Foxglove/Tests/test_fetch_asset.ps1",
            };

            foreach (var path in workflowFiles)
            {
                var text = File.ReadAllText(path).Replace('\\', '/');
                var relativePath = Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                foreach (var stalePath in stalePaths)
                    Assert(!text.Contains(stalePath), $"{relativePath} does not reference stale script path {stalePath}");
            }
        }

        static void ValidateDocsWorkflowCoverage(string repoRoot)
        {
            var docsWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "docs-check.yml");
            Assert(File.Exists(docsWorkflowPath), "docs-check.yml exists");
            var workflow = File.ReadAllText(docsWorkflowPath).Replace('\\', '/');

            var requiredPublicDocs = new[]
            {
                "CITATION.cff",
                "PAPER.md",
                "docs/research-*.md",
                "Packages/dev.unity2foxglove.sdk/README.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/zh",
                "Packages/dev.unity2foxglove.sdk/Documentation~/deu",
            };

            foreach (var requiredPath in requiredPublicDocs)
                Assert(workflow.Contains(requiredPath), $"docs-check covers {requiredPath}");
        }

        static void ValidatePublicDocsScriptReferences(string repoRoot)
        {
            var publicDocsRoots = new[]
            {
                "README.md",
                "PAPER.md",
                "docs",
                "Packages/dev.unity2foxglove.sdk/README.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~",
                "Packages/dev.unity2foxglove.sdk/Samples~",
            };

            foreach (var root in publicDocsRoots)
            {
                var path = Path.Combine(repoRoot, root.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                {
                    AssertPublicDocHasNoStaleScriptPath(repoRoot, path);
                    continue;
                }

                if (!Directory.Exists(path))
                    continue;

                foreach (var file in Directory.GetFiles(path, "*.md", SearchOption.AllDirectories))
                    AssertPublicDocHasNoStaleScriptPath(repoRoot, file);
            }
        }

        static void AssertPublicDocHasNoStaleScriptPath(string repoRoot, string path)
        {
            var relativePath = Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
            var text = File.ReadAllText(path).Replace('\\', '/');
            Assert(!text.Contains("Scripts/build_unity_il2cpp.py"),
                $"{relativePath} does not reference removed build_unity_il2cpp.py");
        }

        static void ValidateResearchDoiPolicy(string repoRoot)
        {
            var researchPath = Path.Combine(repoRoot, "docs", "research-shared-emitter-architecture.md");
            Assert(File.Exists(researchPath), "shared-emitter research note exists");
            var research = File.ReadAllText(researchPath);
            Assert(!research.Contains("cite that DOI from `CITATION.cff`"),
                "shared-emitter research note keeps CITATION.cff on the Concept DOI policy");
            Assert(research.Contains("Concept DOI") && research.Contains("version-specific DOI"),
                "shared-emitter research note distinguishes Concept DOI from version-specific DOI");
        }

        static void ValidateGeneratedSourceProvenance(string repoRoot)
        {
            var generated = FoxgloveSourceEmitter.EmitClass(
                "",
                "Phase16HeaderSmoke",
                new[]
                {
                    new FoxgloveSourceEmitter.TopicMember("_value", "System.Int32", "/phase16/header", 1f, "")
                });

            Assert(generated.Contains("// <auto-generated/>"), "FoxRun emitted source has auto-generated marker");
            Assert(generated.Contains("SPDX-License-Identifier: Apache-2.0"), "FoxRun emitted source has SPDX header");
            Assert(generated.Contains("Generated by the Unity2Foxglove [FoxRun] source emitter."),
                "FoxRun emitted source records source-emitter provenance");

            var protobufSource = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk", "Runtime", "Schemas", "Proto", "Generated", "Messages", "ArrowPrimitive.cs");
            var protoText = File.ReadAllText(protobufSource);
            Assert(protoText.Contains("Generated by the protocol buffer compiler"), "Protobuf C# sources retain protoc marker");
        }

        static void ValidateThirdPartyNotices(string repoRoot)
        {
            var noticesPath = Path.Combine(repoRoot, "THIRD_PARTY_NOTICES.md");
            Assert(File.Exists(noticesPath), "THIRD_PARTY_NOTICES.md exists");
            var notices = File.ReadAllText(noticesPath);
            Assert(notices.Contains("Runtime/Schemas/Proto"), "third-party notices cover generated protobuf C# sources");
            Assert(notices.Contains("foxglove_schemas.pb"), "third-party notices cover protobuf descriptor asset");
            Assert(notices.Contains("Google.Protobuf") && notices.Contains("BSD-3-Clause"),
                "third-party notices cover bundled Google.Protobuf runtime DLL");
            Assert(notices.Contains("does not claim authorship"), "third-party notices preserve upstream schema authorship");
        }

        internal static string FindRepoRoot()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, ".gitignore")))
                    return dir;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }

        static void Assert(bool condition, string description)
        {
            if (condition)
                Console.WriteLine($"[PASS] {description}");
            else
                throw new Exception($"[FAIL] {description}");
        }
    }
}
