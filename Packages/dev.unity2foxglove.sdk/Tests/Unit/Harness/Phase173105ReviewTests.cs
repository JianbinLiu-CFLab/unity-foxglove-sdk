// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using Foxglove.Schemas;
using Google.Protobuf;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-105")]
    public sealed class Phase173105ReviewTests
    {
        [Fact]
        public void ReplayPropertyCacheResetsForNoDomainReloadPlayMode()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayPropertyCache.cs");

            Assert.Contains("RuntimeInitializeLoadType.SubsystemRegistration", source, StringComparison.Ordinal);
            Assert.Contains("Cache.Clear();", source, StringComparison.Ordinal);
        }

        [Fact]
        public void CompressedVideoBuilderUsesSharedEmptyByteString()
        {
            var nullPayload = CameraCompressedVideoBuilder.Create(1, "camera", null);
            var emptyPayload = CameraCompressedVideoBuilder.Create(1, "camera", Array.Empty<byte>());

            Assert.Same(ByteString.Empty, nullPayload.Data);
            Assert.Same(ByteString.Empty, emptyPayload.Data);
        }

        [Fact]
        public void McapDataLoaderProblemsAreImmutableAfterConstruction()
        {
            var problem = new McapDataLoaderProblem(
                McapDataLoaderProblemSeverity.Warning,
                "message",
                "Code",
                "tip");

            Assert.Equal(McapDataLoaderProblemSeverity.Warning, problem.Severity);
            Assert.Equal("message", problem.Message);
            Assert.Equal("Code", problem.Code);
            Assert.Equal("tip", problem.Tip);
            Assert.All(
                typeof(McapDataLoaderProblem).GetProperties().Where(property => property.DeclaringType == typeof(McapDataLoaderProblem)),
                property => Assert.Null(property.SetMethod));
        }

        [Fact]
        public void CoordinateConverterOffersScaleCheckingTransformOverload()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/CoordinateConverterFloat3.cs");

            Assert.Contains("RigidWorldToLocal(Transform transform)", source, StringComparison.Ordinal);
            Assert.Contains("transform.lossyScale", source, StringComparison.Ordinal);
            Assert.Contains("UnityEngine.Debug.Assert", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Ros2CameraNodeCleanupLogsFailures()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraBindingBase.cs");

            Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
            Assert.Contains("ROS2 Camera node cleanup failed", source, StringComparison.Ordinal);
            Assert.DoesNotContain("catch (Exception) { }", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ChineseSecureWssDocsListTokenLeakSurfaces()
        {
            var docs = TestSources.Text("Packages/dev.unity2foxglove.sdk/Documentation~/zh/15_Secure_WSS.md");

            Assert.Contains("浏览器历史", docs, StringComparison.Ordinal);
            Assert.Contains("代理日志", docs, StringComparison.Ordinal);
            Assert.Contains("客户端诊断信息", docs, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplayObjectAdapterSourceShapeTestsUseSharedSourceHelper()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/Replay/ReplayObjectAdapterProtobufTests.cs");

            Assert.Contains("TestSources.Text(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DirectoryNotFoundException", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AppContext.BaseDirectory", source, StringComparison.Ordinal);
        }

        [Fact]
        public void FoxRunInboundPolicyNameSeparatesTokenComparisonFromPolicyGate()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInboundAuthorization.cs");
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");

            Assert.Contains("IsRemoteInboundPolicyMet", source, StringComparison.Ordinal);
            Assert.Contains("FixedTimeEqualsUtf8(sharedToken, incomingToken)", source, StringComparison.Ordinal);
            Assert.Contains("IsRemoteInboundPolicyMet(", manager, StringComparison.Ordinal);
        }
    }
}
