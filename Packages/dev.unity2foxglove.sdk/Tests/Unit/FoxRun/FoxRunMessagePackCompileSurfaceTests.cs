// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Prevents a wildcard-only or retired-builder MessagePack compile surface.

using System;
using System.IO;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas.MsgPack;
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

        [Fact]
        public void UnitProjectLinksTheMaintainedDescriptorReader()
        {
            var unitProject = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj");

            Assert.Contains(
                "../Runtime/FoxRunGenerationDescriptorJsonReader.cs",
                Normalize(unitProject),
                StringComparison.Ordinal);
            Assert.NotNull(typeof(FoxRunGenerationModel).Assembly.GetType(
                "Unity.FoxgloveSDK.Editor.FoxRunReflectionTypeShapeBuilder"));
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void BothTestSurfacesCompileTheExactSdkMessagePackReaderFiles()
        {
            var props = Normalize(ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Tests/FoxgloveSdk.TestSurface.props"));
            var runtimeProject = Normalize(ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj"));

            Assert.Contains(
                "../Runtime/Schemas/MsgPack/FoxgloveMsgPackWriter.cs",
                props,
                StringComparison.Ordinal);
            Assert.Contains(
                "../Runtime/Schemas/MsgPack/FoxgloveMsgPackReader.cs",
                props,
                StringComparison.Ordinal);
            Assert.Contains(
                "../Runtime/Schemas/MsgPack/FoxgloveMsgPackReadLimits.cs",
                props,
                StringComparison.Ordinal);
            Assert.Contains(
                "../../Runtime/Schemas/MsgPack/FoxgloveMsgPackWriter.cs",
                runtimeProject,
                StringComparison.Ordinal);
            Assert.Contains(
                "../../Runtime/Schemas/MsgPack/FoxgloveMsgPackReader.cs",
                runtimeProject,
                StringComparison.Ordinal);
            Assert.Contains(
                "../../Runtime/Schemas/MsgPack/FoxgloveMsgPackReadLimits.cs",
                runtimeProject,
                StringComparison.Ordinal);

            var limits = new FoxgloveMsgPackReadLimits(34, 64, 64, 64);
            Assert.True(new FoxgloveMsgPackReader(NestedArray(33), limits).TrySkipValue());
            Assert.True(new FoxgloveMsgPackReader(NestedArray(34), limits).TrySkipValue());
            Assert.False(new FoxgloveMsgPackReader(NestedArray(35), limits).TrySkipValue());
        }

        private static byte[] NestedArray(int depth)
        {
            var payload = new byte[depth + 1];
            for (var index = 0; index < depth; index++)
                payload[index] = 0x91;
            payload[depth] = 0xc0;
            return payload;
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
