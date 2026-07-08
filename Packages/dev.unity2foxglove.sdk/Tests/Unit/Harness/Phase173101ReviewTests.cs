// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-101")]
    public sealed class Phase173101ReviewTests
    {
        [Fact]
        public void ManagerInspectorWarnsWhenSerializedTransportNoneIsReenabled()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");

            Assert.Contains("nameof(FoxgloveTransportMode.None)", source, StringComparison.Ordinal);
            Assert.Contains("Transport mode is serialized as None while Foxglove WebSocket output is enabled.", source, StringComparison.Ordinal);
            Assert.Contains("Select Web Socket or Secure Web Socket.", source, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraHealthSourceShapeTestUsesRepositoryShapeAnchors()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/Sensors/CameraPipelineHealthPolicyTests.cs");

            Assert.Contains("\"README.md\"", source, StringComparison.Ordinal);
            Assert.Contains("\"Unity2Foxglove\"", source, StringComparison.Ordinal);
            Assert.Contains("\"Packages\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Unity2Foxglove.sln", source, StringComparison.Ordinal);
            Assert.DoesNotContain("\".git\"", source, StringComparison.Ordinal);
        }
    }
}
