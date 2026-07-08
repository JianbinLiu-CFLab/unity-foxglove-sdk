// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Foxglove;
using Google.Protobuf;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.UnitTests.Harness;
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
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs");

            Assert.Contains("=> ReplayProtobufParser.Parse(typeName, payload);", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Type.GetType(typeName + \", Unity.FoxgloveSDK.Proto\")", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplayProtobufParserReusesInvokeArguments()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayProtobufParser.cs");

            Assert.Contains("ParseFromArguments = new object[1];", source, StringComparison.Ordinal);
            Assert.Contains("return binding.Parse(payload);", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new object[] { payload }", source, StringComparison.Ordinal);
        }
    }
}
