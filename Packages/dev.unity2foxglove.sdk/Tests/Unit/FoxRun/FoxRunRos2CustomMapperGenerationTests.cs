// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Pins direct Phase181 DTO-to-custom-ROS2 generated mapper output.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "181-D")]
    [Trait("Domain", "FoxRun")]
    public sealed class FoxRunRos2CustomMapperGenerationTests
    {
        [Fact]
        public void CustomDtoEmitUsesClosedEnvelopeMappersAndDedicatedNativeApplyPath()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Phase181",
                "CustomStateSource",
                new[] { CreateCustomMember() });

            Assert.Contains(
                "#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES",
                source,
                StringComparison.Ordinal);
            Assert.Contains("IFoxRunRos2CustomSubscriptionSource", source, StringComparison.Ordinal);
            Assert.Contains("IFoxRunRos2CustomPublisherSource", source, StringComparison.Ordinal);
            var publisherStart = source.IndexOf(
                "IFoxRunRos2CustomPublisherSource.FoxRunRos2RegisterCustomPublishers",
                StringComparison.Ordinal);
            Assert.True(publisherStart >= 0, source);
            var publisherSection = source.Substring(publisherStart);
            Assert.Contains("new global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CustomPublisherContract(", publisherSection, StringComparison.Ordinal);
            Assert.Contains(
                "\"unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1\"",
                publisherSection,
                StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2CustomTypesupportMetadata.InterfaceDigest", publisherSection, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2CustomTypesupportMetadata.BaseRuntimePackageId", publisherSection, StringComparison.Ordinal);
            Assert.Contains("declaredTargets:", publisherSection, StringComparison.Ordinal);
            Assert.Contains("hasExplicitTargets: false", publisherSection, StringComparison.Ordinal);
            Assert.Contains("hasExplicitQos: true", source, StringComparison.Ordinal);
            Assert.Contains(
                "registrar.Register<global::unity2foxglove_foxrun_interfaces_v1.msg.Phase181State48D288ED82F1Envelope>",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "new global::unity2foxglove_foxrun_interfaces_v1.msg.Phase181State48D288ED82F1Envelope()",
                source,
                StringComparison.Ordinal);
            Assert.Contains("target.Foxrun_origin_id = origin;", source, StringComparison.Ordinal);
            Assert.Contains("target.Foxrun_sequence = sequence;", source, StringComparison.Ordinal);
            Assert.Contains("target.Payload = __FoxRunRos2CustomMapDtoToPayload_0(source, budget);", source, StringComparison.Ordinal);
            Assert.Contains("target.Count = source.Count;", source, StringComparison.Ordinal);
            Assert.Contains("target.Kind = (ushort)source.Kind;", source, StringComparison.Ordinal);
            Assert.Contains("target.Foxrun_has_message = source.Message != null;", source, StringComparison.Ordinal);
            Assert.Contains("target.Foxrun_has_optional_count = source.OptionalCount.HasValue;", source, StringComparison.Ordinal);
            Assert.Contains("target.Foxrun_has_values = source.Values != null;", source, StringComparison.Ordinal);
            Assert.Contains("new long[__source_Values.Count]", source, StringComparison.Ordinal);
            Assert.Contains("new byte[__source_Bytes.Length]", source, StringComparison.Ordinal);
            Assert.Contains("__FoxRunRos2CustomMapPayloadToDto_0", source, StringComparison.Ordinal);
            Assert.Contains("__FoxRunRos2CustomApply_0", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2CustomTypesupportMetadata.BaseRuntimePackageId", source, StringComparison.Ordinal);
            Assert.Contains("typed.Foxrun_origin_id", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MakeGenericMethod", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Activator", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);

            var customSection = source.Substring(source.IndexOf(
                "#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES",
                StringComparison.Ordinal));
            Assert.DoesNotContain("__foxRunSuppressNextPublish_", customSection, StringComparison.Ordinal);
        }

        [Fact]
        public void CustomNativePublishUsesTheDtoAsItsTypedBusPayload()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Phase181",
                "CustomStateSource",
                new[] { CreateCustomMember() });
            var busStart = source.IndexOf(
                "void IFoxgloveTopicBusSource.FoxgloveLog_PublishToBus",
                StringComparison.Ordinal);
            Assert.True(busStart >= 0, source);
            var busSection = source.Substring(busStart);

            Assert.Contains("var __foxRunNativePayload_0 = this.State;", busSection, StringComparison.Ordinal);
            Assert.Contains(
                "bus.Publish<global::Phase181.State>",
                busSection,
                StringComparison.Ordinal);
            Assert.DoesNotContain("new Dictionary<string, object>", busSection, StringComparison.Ordinal);
        }

        [Fact]
        public void CustomNullableNestedDtoMapsToASerializableDefaultRosValueWhenAbsent()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Phase181",
                "CustomStateSource",
                new[] { CreateCustomMember() });

            // ros2cs writes every nested ROS member through its managed wrapper,
            // even when the adjacent FoxRun presence bit is false.  Keep the
            // wire-level null distinction in Foxrun_has_nested while retaining
            // a concrete wrapper that the generated native writer can marshal.
            Assert.Contains(
                "target.Nested = source.Nested == null ? new global::unity2foxglove_foxrun_interfaces_v1.msg.Phase181NestedState3281D0E21244() : __FoxRunRos2CustomNested_1MapDtoToPayload_0(source.Nested, budget);",
                source,
                StringComparison.Ordinal);
            Assert.Contains("target.Foxrun_has_nested = source.Nested != null;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("target.Nested = source.Nested == null ? null :", source, StringComparison.Ordinal);
        }

        [Fact]
        public void CustomSubscribeContractDoesNotEmitASecondNativePublisherSource()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Phase181",
                "CustomSubscribeSource",
                new[] { CreateCustomMember(mode: (int)FoxRunFlow.Subscribe) });

            Assert.Contains("IFoxRunRos2CustomSubscriptionSource", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IFoxRunRos2CustomPublisherSource", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRunRos2RegisterCustomPublishers", source, StringComparison.Ordinal);
        }

        [Fact]
        public void CustomPublishContractEmitsNativePublisherWithoutAnInboundSubscriptionSource()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Phase181",
                "CustomPublishSource",
                new[]
                {
                    CreateCustomMember(
                        mode: (int)FoxRunFlow.Publish,
                        source: FoxRunGenerationDescriptorConstants.InheritSource)
                });

            Assert.Contains("IFoxRunRos2CustomPublisherSource", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2RegisterCustomPublishers", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IFoxRunRos2CustomSubscriptionSource", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRunRos2RegisterCustomSubscriptions", source, StringComparison.Ordinal);
        }

        private static FoxgloveSourceEmitter.TopicMember CreateCustomMember(
            int mode = (int)FoxRunFlow.PublishAndSubscribe,
            string source = FoxRunGenerationDescriptorConstants.Ros2NativeSource)
        {
            var nested = new FoxRunRos2CustomDtoShape(
                "Phase181.NestedState",
                "phase181/NestedState",
                "Phase181NestedState3281D0E21244",
                hasPublicParameterlessConstructor: true,
                isSupported: true,
                members: new[]
                {
                    new FoxRunRos2CustomDtoMemberShape(
                        "Enabled", "enabled", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Boolean", "bool", "", "", false, true, true),
                    new FoxRunRos2CustomDtoMemberShape(
                        "Label", "label", FoxRunRos2CustomDtoMemberKind.String,
                        "System.String", "string", "", "", true, true, true),
                },
                diagnostics: Array.Empty<string>());
            var state = new FoxRunRos2CustomDtoShape(
                "Phase181.State",
                "phase181/State",
                "Phase181State48D288ED82F1",
                hasPublicParameterlessConstructor: true,
                isSupported: true,
                members: new[]
                {
                    new FoxRunRos2CustomDtoMemberShape("Bytes", "bytes", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.Byte[]", "uint8[]", "System.Byte", "", true, true, true,
                        FoxRunRos2CustomDtoSequenceRepresentation.Array),
                    new FoxRunRos2CustomDtoMemberShape("Count", "count", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Int32", "int32", "", "", false, true, true),
                    new FoxRunRos2CustomDtoMemberShape("Kind", "kind", FoxRunRos2CustomDtoMemberKind.Enum,
                        "Phase181.StateKind", "uint16", "", "", false, true, true),
                    new FoxRunRos2CustomDtoMemberShape("Message", "message", FoxRunRos2CustomDtoMemberKind.String,
                        "System.String", "string", "", "", true, true, true),
                    new FoxRunRos2CustomDtoMemberShape("Nested", "nested", FoxRunRos2CustomDtoMemberKind.NestedDto,
                        "Phase181.NestedState", "Phase181NestedState3281D0E21244", "", nested.CanonicalIdentity,
                        true, true, true, nestedShape: nested),
                    new FoxRunRos2CustomDtoMemberShape("OptionalCount", "optional_count", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Nullable<System.Int32>", "int32", "", "", true, true, true),
                    new FoxRunRos2CustomDtoMemberShape("OptionalText", "optional_text", FoxRunRos2CustomDtoMemberKind.String,
                        "System.String", "string", "", "", true, true, true),
                    new FoxRunRos2CustomDtoMemberShape("Values", "values", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.Collections.Generic.List<System.Int64>", "int64[]", "System.Int64", "", true, true, true,
                        FoxRunRos2CustomDtoSequenceRepresentation.List),
                },
                diagnostics: Array.Empty<string>());
            return new FoxgloveSourceEmitter.TopicMember(
                "State",
                "Phase181.State",
                "/phase181/custom-state",
                10f,
                "phase181.State",
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                mode: mode,
                canonicalType: "phase181/State",
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                source: source,
                qosProfile: FoxRunGenerationDescriptorConstants.DefaultQosProfile,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: null,
                ros2CustomDtoShape: state,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto,
                namedArgumentPresence:
                    FoxRunNamedArgumentPresence.QoS
                    | FoxRunNamedArgumentPresence.Reliability
                    | FoxRunNamedArgumentPresence.Durability
                    | FoxRunNamedArgumentPresence.History
                    | FoxRunNamedArgumentPresence.Depth,
                qosReliability: FoxRunGenerationDescriptorConstants.ReliableQosReliability,
                qosDurability: FoxRunGenerationDescriptorConstants.VolatileQosDurability,
                qosHistory: FoxRunGenerationDescriptorConstants.KeepLastQosHistory,
                qosDepth: 10);
        }
    }
}
