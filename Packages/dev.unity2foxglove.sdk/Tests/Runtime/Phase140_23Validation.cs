// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-23 regression coverage for native editor tooling and schema evidence hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_23Validation.
    /// </summary>
    public static class Phase140_23Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-23: Native Editor Tooling and Schema Evidence ===");
            _passed = 0;

            SchemaEvidenceRootRejectsRelativeTraversal();
            FoxRunLinkXmlDeleteUsesStructuredBuildFailure();
            FoxRunManifestWritersPreserveOriginalWriteFailures();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-23: {_passed} checks passed.");
        }

        private static void SchemaEvidenceRootRejectsRelativeTraversal()
        {
            var paths = Read("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");

            Check(paths.Contains("Path.GetFullPath(Path.Combine(ProjectRoot, candidate))", StringComparison.Ordinal)
                  && paths.Contains("Schema evidence root must stay inside Assets.", StringComparison.Ordinal),
                "140-23A-1: schema evidence root canonicalizes relative paths before accepting Assets-relative input");
            Check(paths.Contains("ProjectAssetsRoot", StringComparison.Ordinal)
                  && paths.Contains("IsSameOrChildPath", StringComparison.Ordinal),
                "140-23A-2: schema evidence root validates containment against the canonical Assets root");
        }

        private static void FoxRunLinkXmlDeleteUsesStructuredBuildFailure()
        {
            var build = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");

            Check(build.Contains("RemoveStaleLinkXml(linkPath)", StringComparison.Ordinal)
                  && build.Contains("Failed at: delete-stale-link", StringComparison.Ordinal)
                  && build.Contains("throw new BuildFailedException", StringComparison.Ordinal),
                "140-23B-1: stale FoxRun_link.xml delete failures are wrapped as BuildFailedException");
        }

        private static void FoxRunManifestWritersPreserveOriginalWriteFailures()
        {
            var schemaInfo = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunSchemaInfoWriter.cs");
            var manifest = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs");

            Check(schemaInfo.Contains("TryDeleteTempFile(tempPath)", StringComparison.Ordinal)
                  && schemaInfo.Contains("catch (IOException)", StringComparison.Ordinal)
                  && schemaInfo.Contains("catch (UnauthorizedAccessException)", StringComparison.Ordinal),
                "140-23C-1: FoxRun schema info writer does not let temp cleanup mask write failures");
            Check(manifest.Contains("TryDeleteTempFile(tempPath)", StringComparison.Ordinal)
                  && manifest.Contains("catch (IOException)", StringComparison.Ordinal)
                  && manifest.Contains("catch (UnauthorizedAccessException)", StringComparison.Ordinal),
                "140-23C-2: FoxRun manifest writer does not let temp cleanup mask write failures");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase140_23Validation.cs", StringComparison.Ordinal),
                "140-23D-1: test project compiles Phase140_23Validation");
            Check(registry.Contains("Ci(\"--phase140-23\", \"Phase 140-23\", Phase140_23Validation.Validate", StringComparison.Ordinal),
                "140-23D-2: validation registry exposes --phase140-23");
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
