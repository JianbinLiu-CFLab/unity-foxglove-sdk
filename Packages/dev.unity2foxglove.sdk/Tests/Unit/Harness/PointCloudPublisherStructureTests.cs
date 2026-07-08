// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140F point-cloud publisher structure checks.

using System;
using System.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140F")]
    [Trait("Domain", "Harness")]
    public sealed class PointCloudPublisherStructureTests
    {
        private static readonly string[] ExpectedPartials =
        {
            "FoxglovePointCloudPublisher.Draco.cs",
            "FoxglovePointCloudPublisher.PointCloud2Native.cs",
            "FoxglovePointCloudPublisher.MotionCompensation.cs",
            "FoxglovePointCloudPublisher.Raw.cs",
            "FoxglovePointCloudPublisher.Diagnostics.cs"
        };

        [Fact]
        public void PointCloudPublisherIsSplitIntoFocusedPartials()
        {
            var core = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");

            Assert.Contains("public partial class FoxglovePointCloudPublisher", core, StringComparison.Ordinal);
            foreach (var file in ExpectedPartials)
            {
                var relativePath = "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/" + file;
                Assert.True(File.Exists(PathOf(relativePath)), relativePath + " should exist.");
                var source = Text(relativePath);
                Assert.Contains("partial class FoxglovePointCloudPublisher", source, StringComparison.Ordinal);
                Assert.Contains("// Module: Runtime/Schemas/Proto/Publishers", source, StringComparison.Ordinal);
                Assert.Contains("// Purpose:", source, StringComparison.Ordinal);
            }

            Assert.Contains("TryQueueVirtualLidarDracoFrame", Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Draco.cs"), StringComparison.Ordinal);
            Assert.Contains("TryQueueVirtualLidarPointCloud2NativeFrame", Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.PointCloud2Native.cs"), StringComparison.Ordinal);
            Assert.Contains("ResolveMotionCompensationSettings", Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.MotionCompensation.cs"), StringComparison.Ordinal);
            Assert.Contains("PublishRawFrame", Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Raw.cs"), StringComparison.Ordinal);
            Assert.Contains("LogPointCloudDiagnosticMessage", Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Diagnostics.cs"), StringComparison.Ordinal);
            Assert.DoesNotContain("private void LogPointCloudDiagnosticMessage", core, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloudRos2SchemaNameComesFromOutputProfile()
        {
            var profile = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudOutputMode.cs");
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");

            Assert.Contains("public string Ros2SchemaName { get; }", profile, StringComparison.Ordinal);
            Assert.Contains("Ros2PublisherSchemaNames.CompressedPointCloud", profile, StringComparison.Ordinal);
            Assert.Contains("Ros2PublisherSchemaNames.SensorPointCloud2", profile, StringComparison.Ordinal);
            Assert.Contains("Ros2PublisherSchemaNames.PointCloud", profile, StringComparison.Ordinal);
            Assert.Contains("protected override string Ros2SchemaName => ActiveProfile.Ros2SchemaName;", publisher, StringComparison.Ordinal);
            Assert.DoesNotContain("switch (_outputMode)", TestSources.Slice(publisher, "protected override string Ros2SchemaName", "protected override bool SupportsJsonEncoding"), StringComparison.Ordinal);
        }

        [Fact]
        public void DracoCompletedQueueKeepsLatestResultOnly()
        {
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");

            Assert.Contains("MaxCompletedDracoEncodeResults = 1", publisher, StringComparison.Ordinal);
            Assert.Contains("latest completed Draco frame only", publisher, StringComparison.Ordinal);
        }

        private static string Text(string relativePath)
            => File.ReadAllText(PathOf(relativePath));

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
        {
            get
            {
                var overrideRoot = Environment.GetEnvironmentVariable("UNITY2FOXGLOVE_REPO_ROOT");
                if (!string.IsNullOrWhiteSpace(overrideRoot) && LooksLikeRepoRoot(overrideRoot))
                    return overrideRoot;

                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (LooksLikeRepoRoot(dir.FullName))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }

        private static bool LooksLikeRepoRoot(string path)
            => File.Exists(Path.Combine(path, "README.md"))
               && Directory.Exists(Path.Combine(path, "Unity2Foxglove"))
               && File.Exists(Path.Combine(path, "Packages", "dev.unity2foxglove.sdk", "package.json"));
    }
}
