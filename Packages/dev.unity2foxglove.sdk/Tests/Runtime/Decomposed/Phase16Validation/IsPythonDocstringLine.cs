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
        static bool IsPythonDocstringLine(string trimmedLine)
        {
            return trimmedLine.StartsWith("\"\"\"", StringComparison.Ordinal)
                || trimmedLine.StartsWith("'''", StringComparison.Ordinal);
        }

        static bool HasInlinePythonDocstring(string declarationLine)
        {
            var colon = declarationLine.IndexOf(':');
            if (colon < 0 || colon + 1 >= declarationLine.Length)
                return false;

            var tail = declarationLine.Substring(colon + 1).TrimStart();
            return Regex.IsMatch(tail, "^(?:[rRuUbBfF]{0,3})(?:\"\"\"|''')");
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
                var text = File.ReadAllText(path);
                if (text.Contains('\\'))
                    text = text.Replace('\\', '/');
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

        static void ValidatePrivateWorkspaceBoundaries(string repoRoot)
        {
            if (!TryRunGitLsFiles(
                    repoRoot,
                    new[]
                    {
                        "Plan/**",
                        "Developer/**",
                        "Developer.meta",
                        ":(glob)**/Developer/**",
                        ":(glob)**/Developer.meta"
                    },
                    out var trackedPrivateFiles))
            {
                Assert(false, "git is available for tracked private workspace boundary checks");
            }

            Assert(trackedPrivateFiles.Count == 0,
                "No tracked Plan/Developer private workspace files are present");
        }

        static bool TryRunGitLsFiles(string repoRoot, IReadOnlyList<string> pathspecs, out IReadOnlyList<string> files)
        {
            files = Array.Empty<string>();

            try
            {
                var arguments = "ls-files -- " + string.Join(" ", pathspecs.Select(QuoteGitArgument));
                var startInfo = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return false;

                var output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(5000) || process.ExitCode != 0)
                    return false;

                files = output.Replace("\r\n", "\n")
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Replace('\\', '/'))
                    .ToArray();
                return true;
            }
            catch
            {
                return false;
            }
        }

        static string QuoteGitArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
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

        static void ValidatePackageContractReviewFindings(string repoRoot)
        {
            var adapterPackage = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.ros2forunity");
            var adapterPackageJson = File.ReadAllText(Path.Combine(adapterPackage, "package.json"));
            Assert(adapterPackageJson.Contains("\"dev.unity2foxglove.sdk\": \"1.9.7\""),
                "R2FU adapter package declares its SDK package dependency");

            var nativeAsmdef = File.ReadAllText(Path.Combine(adapterPackage, "Runtime", "Native",
                "Unity2Foxglove.Ros2ForUnity.Native.asmdef"));
            Assert(!nativeAsmdef.Contains("\"Unity.FoxgloveSDK.Runtime\""),
                "R2FU native asmdef does not reference stale Unity.FoxgloveSDK.Runtime assembly");
            Assert(nativeAsmdef.Contains("\"Unity.FoxgloveSDK\"")
                   && nativeAsmdef.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime\""),
                "R2FU native asmdef references valid SDK and active runtime assemblies");

            var sensorsAsmdef = File.ReadAllText(Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk",
                "Runtime", "Sensors", "Unity.FoxgloveSDK.Sensors.asmdef"));
            Assert(sensorsAsmdef.Contains("\"rootNamespace\": \"Unity.FoxgloveSDK.Sensors\""),
                "Sensors asmdef has contributor-facing root namespace");

            var citation = File.ReadAllText(Path.Combine(repoRoot, "CITATION.cff"));
            Assert(citation.Contains("version: \"1.9.7\"")
                   && citation.Contains("date-released: \"2026-11-02\""),
                "CITATION.cff carries exact release metadata");
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

        /// <summary>
        /// Validation method for FindRepoRoot.
        /// </summary>
        /// <returns>The value produced by the validation helper.</returns>
        internal static string FindRepoRoot()
            => RepoRoot.Value;

        static void Assert(bool condition, string description)
        {
            if (condition)
                Console.WriteLine($"[PASS] {description}");
            else
                throw new Exception($"[FAIL] {description}");
        }
    }
}
