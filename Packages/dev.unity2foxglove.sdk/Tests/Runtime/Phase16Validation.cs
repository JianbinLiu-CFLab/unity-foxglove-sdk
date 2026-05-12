// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates package metadata (package.json, LICENSE), .gitignore build artifact coverage, CI workflows, and asmdef consistency.

using System;
using System.IO;
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
            Assert(json.Contains("\"version\": \"1.3.0\""), "package.json version is 1.3.0");
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
            ValidateGeneratedSourceProvenance(repoRoot);
            ValidateThirdPartyNotices(repoRoot);

            Console.WriteLine("Phase 16: All checks passed.");
        }

        static void ValidateRepositoryHeaders(string repoRoot)
        {
            var requiredHeaderFiles = new[]
            {
                "Scripts/bump_version.py",
                "Scripts/run_performance_baseline.py",
                "Scripts/smoke/generate_phase34_attachment_smoke.ps1",
                "Scripts/smoke/generate_phase44_all_schemas_mcap.py",
                "Scripts/smoke/phase40_slow_camera_client.ps1",
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
            }

            var phase32Path = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime", "Phase32Validation.cs");
            var phase32 = File.ReadAllText(phase32Path);
            Assert(phase32.Contains("// Module: Tests/Runtime"), "Phase32Validation.cs has module header");
            Assert(phase32.Contains("// Purpose:"), "Phase32Validation.cs has purpose header");
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

            var protobufSource = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk", "Runtime", "Schemas", "Proto", "ArrowPrimitive.cs");
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
