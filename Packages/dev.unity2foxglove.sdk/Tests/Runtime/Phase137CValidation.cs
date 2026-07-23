// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137C editor codegen refactoring guard.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase137CValidation.
    /// </summary>
    public static class Phase137CValidation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 137C Tests ---");
            _passed = 0;

            VerifySubEmitterFiles();
            VerifyEmitClassOutputUnchanged();
            VerifyIsAnonymousPropertyNameRemoved();
            VerifyTopicPolicyFixed();
            VerifyCsprojGlobs();
            VerifyPublicApiPreserved();

            Console.WriteLine("Phase 137C: " + _passed + " checks passed.\n");
        }

        private static void VerifySubEmitterFiles()
        {
            var dir = RepoPath("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter");
            Check(Directory.Exists(dir), "137C-1: FoxgloveSourceEmitter/ directory exists");

            var files = new[] {
                "FoxgloveSourceEmitter.cs", "ClassFrameEmitter.cs", "TopicMetadataEmitter.cs",
                "PublishDispatchEmitter.cs", "TriggerEmitter.cs", "PolicyEmitter.cs",
                "TypeExprEmitter.cs", "StringLiteralEmitter.cs", "IdentifierUtils.cs"
            };
            foreach (var f in files)
            {
                var path = Path.Combine(dir, f);
                Check(File.Exists(path), "137C-2: sub-emitter exists: " + f);
            }

            Check(!File.Exists(RepoPath("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter.cs")),
                "137C-3: old flat file removed");
        }

        private static void VerifyEmitClassOutputUnchanged()
        {
            var members = new[] {
                new FoxgloveSourceEmitter.TopicMember("_val", "System.Single", "/test", 10f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("Test", "NoInline", members);
            Check(output.Contains("FoxgloveLog_TopicCount => 1"), "137C-4: EmitClass still produces expected output");
            Check(output.Contains("mgr.PublishJson"), "137C-5: Publish dispatch unchanged");
        }

        private static void VerifyIsAnonymousPropertyNameRemoved()
        {
            var dir = RepoPath("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter");
            var allSource = string.Join("\n",
                Directory.GetFiles(dir, "*.cs").Select(File.ReadAllText));
            Check(!allSource.Contains("IsAnonymousPropertyName", StringComparison.Ordinal), "137C-6: IsAnonymousPropertyName removed");
        }

        private static void VerifyTopicPolicyFixed()
        {
            var entryPath = RepoPath("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/FoxgloveSourceEmitter.cs");
            Check(File.Exists(entryPath), "137C-7: FoxgloveSourceEmitter entry source exists");
            var entry = File.ReadAllText(entryPath);
            Check(!entry.Contains("fields.Max(f => f.Policy)", StringComparison.Ordinal), "137C-8: unreachable Max call replaced");
            Check(entry.Contains("return PolicyFixedRate;", StringComparison.Ordinal)
                  && entry.Contains("PolicyTrigger", StringComparison.Ordinal),
                "137C-9: TopicPolicy returns named mode constants");
        }

        private static void VerifyCsprojGlobs()
        {
            var testCsprojPath = RepoPath("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(File.Exists(testCsprojPath), "137C-10: runtime validation csproj exists");
            var testCsproj = File.ReadAllText(testCsprojPath);
            Check(testCsproj.Contains("FoxgloveSourceEmitter/**/*.cs", StringComparison.Ordinal), "137C-11: test csproj uses wildcard glob");

            var sgCsprojPath = RepoPath("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj");
            Check(File.Exists(sgCsprojPath), "137C-12: source generator csproj exists");
            var sgCsproj = File.ReadAllText(sgCsprojPath);
            Check(sgCsproj.Contains("FoxgloveSourceEmitter\\**\\*.cs", StringComparison.Ordinal), "137C-13: SG csproj uses wildcard glob");
        }

        private static void VerifyPublicApiPreserved()
        {
            Check(typeof(FoxgloveSourceEmitter).GetMethod("ChangeExpr") != null,
                "137C-14: ChangeExpr still on FoxgloveSourceEmitter");
            Check(typeof(FoxgloveSourceEmitter).GetMethod("ValueExpr") != null,
                "137C-15: ValueExpr still on FoxgloveSourceEmitter");
            Check(typeof(FoxgloveSourceEmitter).GetMethod("EmitClass", new[] { typeof(FoxRunGenerationType) }) != null,
                "137C-16: EmitClass(FoxRunGenerationType) preserved");
        }

        private static string RepoPath(string relativePath)
        {
            var repoRoot = Phase16Validation.FindRepoRoot()
                ?? throw new DirectoryNotFoundException("Could not find repository root for Phase137C validation.");
            return Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                Console.WriteLine("[PASS] " + label);
                _passed++;
            }
            else
            {
                Console.WriteLine("[FAIL] " + label);
                throw new InvalidOperationException("Phase 137C validation failed: " + label);
            }
        }
    }
}
