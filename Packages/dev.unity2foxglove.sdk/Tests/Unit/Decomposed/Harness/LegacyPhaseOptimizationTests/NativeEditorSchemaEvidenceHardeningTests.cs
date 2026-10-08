// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-21/22/23/24/27 legacy phase optimization checks.

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{

    [Trait("Phase", "140-23")]
    [Trait("Domain", "Harness")]
    public sealed class NativeEditorSchemaEvidenceHardeningTests
    {
        [Fact]
        public void SchemaEvidenceAndManifestWritersKeepFailureBoundaries()
        {
            var paths = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");
            var build = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            var schemaInfo = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunSchemaInfoWriter.cs");
            var manifest = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs");

            Assert.Contains("Path.GetFullPath(Path.Combine(ProjectRoot, candidate))", paths, StringComparison.Ordinal);
            Assert.Contains("Schema evidence root must stay inside Assets.", paths, StringComparison.Ordinal);
            Assert.Contains("ProjectAssetsRoot", paths, StringComparison.Ordinal);
            Assert.Contains("IsSameOrChildPath", paths, StringComparison.Ordinal);
            Assert.Contains("RemoveStaleLinkXml(linkPath)", build, StringComparison.Ordinal);
            Assert.Contains("Failed at: delete-stale-link", build, StringComparison.Ordinal);
            Assert.Contains("throw new BuildFailedException", build, StringComparison.Ordinal);
            Assert.Contains("TryDeleteTempFile(tempPath)", schemaInfo, StringComparison.Ordinal);
            Assert.Contains("catch (IOException)", schemaInfo, StringComparison.Ordinal);
            Assert.Contains("catch (UnauthorizedAccessException)", schemaInfo, StringComparison.Ordinal);
            Assert.Contains("TryDeleteTempFile(tempPath)", manifest, StringComparison.Ordinal);
            Assert.Contains("catch (IOException)", manifest, StringComparison.Ordinal);
            Assert.Contains("catch (UnauthorizedAccessException)", manifest, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14023MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_23Validation.cs", "--phase140-23", "Phase140_23Validation.Validate");
    }
}
