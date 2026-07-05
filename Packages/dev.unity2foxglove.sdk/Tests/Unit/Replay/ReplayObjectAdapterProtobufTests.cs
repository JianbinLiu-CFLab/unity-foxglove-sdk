// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using Foxglove;
using Google.Protobuf;
using Unity.FoxgloveSDK.Core;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Replay
{
    public sealed class ReplayObjectAdapterProtobufTests
    {
        [Fact]
        public void ParseProtobufResolvesGeneratedFrameTransformTypes()
        {
            var message = new FrameTransform
            {
                ParentFrameId = "map",
                ChildFrameId = "base_link",
                Translation = new Vector3 { X = 1, Y = 2, Z = 3 },
                Rotation = new Quaternion { X = 0, Y = 0, Z = 0, W = 1 }
            };

            var parsed = ReplayProtobufParser.Parse("Foxglove.FrameTransform", message.ToByteArray());

            var transform = Assert.IsType<FrameTransform>(parsed);
            Assert.Equal("base_link", transform.ChildFrameId);
        }

        [Fact]
        public void ReplayObjectAdapterDelegatesProtobufParsingToCoreHelper()
        {
            var source = File.ReadAllText(PathOf("Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs"));

            Assert.Contains("=> ReplayProtobufParser.Parse(typeName, payload);", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Type.GetType(typeName + \", Unity.FoxgloveSDK.Proto\")", source, StringComparison.Ordinal);
        }

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }
    }
}
