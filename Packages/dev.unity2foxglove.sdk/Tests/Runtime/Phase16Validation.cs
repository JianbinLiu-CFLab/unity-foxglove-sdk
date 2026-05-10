// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates package metadata (package.json, LICENSE), .gitignore build artifact coverage, CI workflows, and asmdef consistency.

using System;
using System.IO;

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
            Assert(json.Contains("\"version\": \"1.1.0\""), "package.json version is 1.1.0");
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

            Console.WriteLine("Phase 16: All checks passed.");
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
