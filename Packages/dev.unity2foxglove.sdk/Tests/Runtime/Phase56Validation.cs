// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 56 regression coverage for FoxRun source-generation
// identity, generated identifier escaping, localized docs drift, and
// analyzer release tracking.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates fixes from the FoxRun source-generation and IL2CPP review.
    /// </summary>
    public static class Phase56Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 56: FoxRun Source Generation Hardening ===");
            _passed = 0;

            VerifyGeneratedNamesUseNamespaceIdentity();
            VerifyEmitterEscapesGeneratedPayloadIdentifiers();
            VerifyChineseFoxRunDocsDescribeCurrentPolicySurface();
            VerifySourceGeneratorHasReleaseTracking();

            Console.WriteLine($"Phase 56: {_passed} checks passed.");
        }

        private static void VerifyGeneratedNamesUseNamespaceIdentity()
        {
            Check(FoxgloveSourceEmitter.GeneratedSourceName("", "Telemetry") == "Telemetry_FoxRun.g.cs",
                "56A-1: generated names preserve legacy global-namespace file names");
            Check(FoxgloveSourceEmitter.GeneratedSourceName("Robotics.Sim", "Telemetry") == "Robotics_Sim_Telemetry_FoxRun.g.cs",
                "56A-2: generated names include namespace identity for collision avoidance");

            var roslynGenerator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var buildTimeGenerator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");

            Check(roslynGenerator.Contains("FoxgloveSourceEmitter.GeneratedSourceName(ns, className)"),
                "56A-3: Roslyn generator uses shared generated source naming");
            Check(buildTimeGenerator.Contains("FoxgloveSourceEmitter.GeneratedSourceName(kv.Key.Ns, kv.Key.ClassName)"),
                "56A-4: build-time fallback uses shared generated source naming");
        }

        private static void VerifyEmitterEscapesGeneratedPayloadIdentifiers()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Phase56.Generated",
                "KeywordSource",
                new List<FoxgloveSourceEmitter.TopicMember>
                {
                    new FoxgloveSourceEmitter.TopicMember("_1", "System.Int32", "/phase56/mixed", 10f, ""),
                    new FoxgloveSourceEmitter.TopicMember("class", "System.Int32", "/phase56/mixed", 10f, ""),
                    new FoxgloveSourceEmitter.TopicMember("_velocity", "UnityEngine.Vector3", "/phase56/mixed", 10f, "")
                });

            Check(source.Contains("new Dictionary<string, object>"),
                "56B-1: emitter falls back to a string-keyed payload when JSON field names are not C# anonymous-property identifiers");
            Check(source.Contains("[\"1\"] = this._1"),
                "56B-2: leading-underscore numeric member keeps JSON field name while preserving member access");
            Check(source.Contains("[\"class\"] = this.@class"),
                "56B-3: keyword member access is escaped while preserving JSON field name");
            Check(source.Contains("[\"velocity\"] = new { x = this._velocity.x"),
                "56B-4: Unity value builders still use explicit this-qualified member access inside dictionary payloads");
            Check(!source.Contains("new { 1 =") && !source.Contains("class = this.class"),
                "56B-5: emitter no longer writes invalid anonymous-object properties");
        }

        private static void VerifyChineseFoxRunDocsDescribeCurrentPolicySurface()
        {
            var doc = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/zh/07_FoxRun自动发布.md");

            Check(!doc.Contains("RateHz = 0"),
                "56C-1: Chinese FoxRun docs no longer claim RateHz = 0 publishes every frame");
            Check(doc.Contains("PublishMode") && doc.Contains("OnTrigger") && doc.Contains("ChangeEpsilon")
                && doc.Contains("ForceIntervalSeconds") && doc.Contains("FOXRUN005"),
                "56C-2: Chinese FoxRun docs cover current publish policy and FOXRUN005 behavior");
        }

        private static void VerifySourceGeneratorHasReleaseTracking()
        {
            var shippedPath = RepoPath("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.Shipped.md");
            Check(File.Exists(shippedPath),
                "56D-1: source generator project includes analyzer release tracking");

            var shipped = File.ReadAllText(shippedPath);
            foreach (var id in new[] { "FOXRUN001", "FOXRUN002", "FOXRUN003", "FOXRUN004", "FOXRUN005" })
                Check(shipped.Contains(id) && shipped.Contains("FoxRun"),
                    $"56D-2: analyzer release tracking lists {id}");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }

        private static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath));
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
