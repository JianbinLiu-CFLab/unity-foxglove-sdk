// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Prevents a wildcard-only or retired-builder MessagePack compile surface.

using System;
using System.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "185-A")]
    [Trait("Domain", "FoxRun")]
    public sealed class FoxRunMessagePackCompileSurfaceTests
    {
        [Fact]
        public void BothTestProjectsCompileTheOneReflectionTypeShapeBuilder()
        {
            var props = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/FoxgloveSdk.TestSurface.props");
            var runtimeProject = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Assert.Contains("Editor/FoxRun/FoxRunReflectionTypeShapeBuilder.cs", Normalize(props), StringComparison.Ordinal);
            Assert.Contains("../../Editor/FoxRun/FoxRunReflectionTypeShapeBuilder.cs", Normalize(runtimeProject), StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRunProtobufReflectionTypeShapeBuilder.cs", props, StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRunProtobufReflectionTypeShapeBuilder.cs", runtimeProject, StringComparison.Ordinal);
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string Normalize(string value) => (value ?? string.Empty).Replace('\\', '/');

        private static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Packages", "dev.unity2foxglove.sdk")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Could not locate the Unity2Foxglove repository root.");
        }
    }
}
