// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Pins direct Phase181 DTO-to-custom-ROS2 generated mapper output.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "181-D")]
    [Trait("Domain", "FoxRun")]
    public sealed partial class FoxRunRos2CustomMapperGenerationTests
    {
        [Fact]
        public void CustomDtoEmitUsesClosedEnvelopeMappersAndDedicatedNativeApplyPath()
        {
            var source = EmitR2fuClass(
                "Phase181",
                "CustomStateSource",
                new[] { CreateCustomMember() });

            Assert.Contains(
                "#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)",
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
            Assert.Contains(
                "(global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunQosProfile)0",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunQosReliability.Reliable",
                source,
                StringComparison.Ordinal);
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
            Assert.Contains(
                "new global::unity2foxglove_foxrun_interfaces_v1.msg.Phase181NestedState3281D0E21244[__source_Children.Count]",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "__target_Children[__i] = __source_Children[__i] == null ? new global::unity2foxglove_foxrun_interfaces_v1.msg.Phase181NestedState3281D0E21244() : __FoxRunRos2CustomNested_1MapDtoToPayload_0(__source_Children[__i], budget);",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "__values_Children.Add(__FoxRunRos2CustomNested_1MapPayloadToDto_0(source.Children[__i]));",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "var __item_Labels = __source_Labels[__i] ?? string.Empty;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "__target_Labels[__i] = __item_Labels;",
                source,
                StringComparison.Ordinal);
            Assert.Contains("nestedSequence_Children", source, StringComparison.Ordinal);
            Assert.Contains("__FoxRunRos2CustomMapPayloadToDto_0", source, StringComparison.Ordinal);
            Assert.Contains("__FoxRunRos2CustomApply_0", source, StringComparison.Ordinal);
            var applyStart = source.IndexOf(
                "private void __FoxRunRos2CustomApply_0",
                StringComparison.Ordinal);
            var applyEnd = source.IndexOf(
                "private bool __FoxRunRos2CustomClearIfOwned_0",
                applyStart,
                StringComparison.Ordinal);
            Assert.True(applyStart >= 0 && applyEnd > applyStart, source);
            Assert.Contains(
                "__FoxRunMarkRemoteApplied_0();",
                source.Substring(applyStart, applyEnd - applyStart),
                StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2CustomTypesupportMetadata.BaseRuntimePackageId", source, StringComparison.Ordinal);
            Assert.Contains("typed.Foxrun_origin_id", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MakeGenericMethod", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Activator", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);

            var customSection = source.Substring(source.IndexOf(
                "#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)",
                StringComparison.Ordinal));
            Assert.DoesNotContain("__foxRunSuppressNextPublish_", customSection, StringComparison.Ordinal);
        }

        [Fact]
        public void CustomDtoCleanupAttemptsEveryOwnedObjectAndPreservesPrimaryMappingFailure()
        {
            var source = EmitR2fuClass(
                "Phase187",
                "CleanupContract",
                new[] { CreateCustomMember() });

            Assert.Contains(
                "global::System.Exception __foxRunRos2CustomDisposeFailure = null;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (__foxRunRos2CustomDisposeFailure == null) __foxRunRos2CustomDisposeFailure = exception;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "catch (global::System.Exception)",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        public void CustomNullableNestedDtoMapsToASerializableDefaultRosValueWhenAbsent()
        {
            var source = EmitR2fuClass(
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
            var source = EmitR2fuClass(
                "Phase181",
                "CustomSubscribeSource",
                new[] { CreateCustomMember(mode: (int)FoxRunFlow.Subscribe) });

            Assert.Contains("IFoxRunRos2CustomSubscriptionSource", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IFoxRunRos2CustomPublisherSource", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRunRos2RegisterCustomPublishers", source, StringComparison.Ordinal);
        }

    }
}
