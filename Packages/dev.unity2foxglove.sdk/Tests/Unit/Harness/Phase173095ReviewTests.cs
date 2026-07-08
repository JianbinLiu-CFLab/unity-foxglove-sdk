// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-095 review regression guards.

using System;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "173-095")]
    [Trait("Domain", "Review")]
    public sealed class Phase173095ReviewTests
    {
        [Fact]
        public void SlowLocalServiceCallKeepsCompletedResponse()
        {
            var descriptor = new FoxgloveGeneratedServiceDescriptor(
                "/phase173/slow",
                "phase173.Slow",
                string.Empty,
                "phase173.Request",
                "phase173.Response",
                _ =>
                {
                    Thread.Sleep(5);
                    return new JObject { ["applied"] = true };
                });

            var result = FoxgloveLocalServiceCall.Invoke(descriptor, new JObject(), TimeSpan.FromTicks(1));

            Assert.Equal(FoxgloveLocalServiceCallStatus.CompletedButSlow, result.Status);
            Assert.True(result.Response["applied"].Value<bool>());
            Assert.Contains("completed after", result.Error, StringComparison.Ordinal);
        }

        [Fact]
        public void TransportStatsMetaUsesScriptImporterAndOpaqueGuid()
        {
            var meta = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Transport/Common/TransportStats.cs.meta");

            Assert.Contains("guid: ff999633f6d940a38ffcfbb1cd78dd9b", meta, StringComparison.Ordinal);
            Assert.Contains("MonoImporter:", meta, StringComparison.Ordinal);
            Assert.Contains("serializedVersion: 2", meta, StringComparison.Ordinal);
            Assert.DoesNotContain("DefaultImporter:", meta, StringComparison.Ordinal);
            Assert.DoesNotContain("a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7", meta, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraImageTopicAliasDocumentsCompressedTopicContract()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraSensorProfileResolver.cs");

            Assert.Contains("Resolves the compressed image topic", source, StringComparison.Ordinal);
            Assert.Contains("Use <see cref=\"ResolveRawImageTopic\"/>", source, StringComparison.Ordinal);
            Assert.Contains("=> ResolveCompressedImageTopic(sensorUnitProfile, fallbackTopic);", source, StringComparison.Ordinal);
        }

        [Fact]
        public void R2fuPointCloudBuilderCachesStableFieldLayout()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2MessageBuilder.cs");

            Assert.Contains("private static sensor_msgs.msg.PointField[] s_cachedFields;", source, StringComparison.Ordinal);
            Assert.Contains("if (FieldsMatch(s_cachedFields, packedFields))", source, StringComparison.Ordinal);
            Assert.Contains("return s_cachedFields;", source, StringComparison.Ordinal);
            Assert.Contains("s_cachedFields = fields;", source, StringComparison.Ordinal);
            Assert.Contains("string.Equals(cached.Name, field.Name, StringComparison.Ordinal)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void R2fuImuBuilderDocumentsMessageAllocationBoundary()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuMessageBuilder.cs");

            Assert.Contains("allocation-visible until publisher copy/retain semantics are proven", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Ros2BridgeFrameHasValidatedInternalPath()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrame.cs");
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");

            Assert.Contains("internal static Ros2BridgeFrame CreateValidated", source, StringComparison.Ordinal);
            Assert.Contains("validateSchema: false", source, StringComparison.Ordinal);
            Assert.Contains("if (validateSchema && !FoxgloveRos2MsgSchemaCatalog.TryGet", source, StringComparison.Ordinal);
            Assert.Contains("Ros2BridgeFrame.CreateValidated", manager, StringComparison.Ordinal);
        }

        [Fact]
        public void R2fuGuardHelperUsesRepositoryShapeSentinel()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/R2fuGuardHelperOptimizationTests.cs");

            Assert.Contains("README.md", source, StringComparison.Ordinal);
            Assert.Contains("\"Unity2Foxglove\"", source, StringComparison.Ordinal);
            Assert.Contains("\"Packages\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Unity2Foxglove.sln", source, StringComparison.Ordinal);
            Assert.DoesNotContain("\".git\"", source, StringComparison.Ordinal);
        }

        [Fact]
        public void FoxServiceSchemaRejectsEmptyJsonTypes()
        {
            Assert.Throws<ArgumentException>(() => FoxServiceSchemaModel.Scalar(string.Empty));

            var model = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxServiceSchema/FoxServiceSchemaModel.cs");
            var emitter = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxServiceSchema/FoxServiceSchemaEmitter.cs");

            Assert.Contains("FoxServiceSchemaModel.JsonType must be non-empty.", model, StringComparison.Ordinal);
            Assert.Contains("FoxServiceSchemaModel.JsonType must be non-empty.", emitter, StringComparison.Ordinal);
        }
    }
}
