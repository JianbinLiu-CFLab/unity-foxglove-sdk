// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-100")]
    public sealed class Phase173100ReviewTests
    {
        [Fact]
        public void ProtoChannelDocumentsPerCallSerializationAllocation()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Channels/FoxgloveProtoChannel.cs");

            Assert.Contains("ToByteArray()", source, StringComparison.Ordinal);
            Assert.Contains("allocates once per call", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Ros2BridgeSampleScriptValidatesUserControlledLaunchValues()
        {
            var source = TestSources.Text("Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.sh");

            Assert.Contains("PORT must be a number between 1 and 65535.", source, StringComparison.Ordinal);
            Assert.Contains("10#$PORT", source, StringComparison.Ordinal);
            Assert.Contains("PAYLOAD_FORMAT must be one of", source, StringComparison.Ordinal);
            Assert.Contains("cdr-with-encapsulation|raw-cdr", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeBuildScriptsPreserveTimeSourceHardening()
        {
            foreach (var distro in new[] { "humble", "jazzy", "lyrical" })
            {
                var source = TestSources.Text("Scripts/ros2forunity/windows/" + distro + "/build_r2fu_runtime_package.py");

                Assert.Contains("lastEmittedSeconds", source, StringComparison.Ordinal);
                Assert.Contains("wall-clock corrections cannot move time backward", source, StringComparison.Ordinal);
                Assert.Contains("patch_deps_json_sha512", source, StringComparison.Ordinal);
            }
        }
    }
}
