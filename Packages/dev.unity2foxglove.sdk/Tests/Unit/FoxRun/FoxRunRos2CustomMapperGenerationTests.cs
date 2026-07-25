// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Pins direct Phase181 DTO-to-custom-ROS2 generated mapper output.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
            Assert.Contains("hasExplicitQosProfile: true", source, StringComparison.Ordinal);
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

            Assert.Contains("var __foxRunNativePayload_0 = __foxRunCapture_0_0;", busSection, StringComparison.Ordinal);
            Assert.Contains(
                "bus.Publish<global::Phase181.State>",
                busSection,
                StringComparison.Ordinal);
            Assert.DoesNotContain("new Dictionary<string, object>", busSection, StringComparison.Ordinal);
        }

        [Fact]
        public void CustomTargetAwarePublishSeparatesObserversFromNativeTransportResults()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Phase181",
                "CustomStateSource",
                new[] { CreateCustomMember() });

            Assert.Contains(
                "IFoxgloveTopicObserverSource",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "bus.HasObservers<global::Phase181.State>",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "bus.PublishToObservers<global::Phase181.State>",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "bus.PublishToResultSubscribers<global::Phase181.State>",
                source,
                StringComparison.Ordinal);

            var targetStart = source.IndexOf(
                "bool IFoxglovePublishTargetSource.FoxgloveLog_PublishCaptured",
                StringComparison.Ordinal);
            var observerStart = source.IndexOf(
                "void IFoxgloveTopicObserverSource.FoxgloveLog_PublishCapturedToObservers",
                StringComparison.Ordinal);
            Assert.True(
                targetStart >= 0 && observerStart > targetStart,
                source);
            Assert.DoesNotContain(
                "PublishToObservers",
                source.Substring(targetStart, observerStart - targetStart),
                StringComparison.Ordinal);
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
        public void CustomCdrUsesTheSharedFourMiBBoundAndNormalizesNullStringElements()
        {
            var source = FoxgloveSourceEmitter.EmitClass(
                "Phase181",
                "CustomStateSource",
                new[] { CreateCustomMember() });

            Assert.Contains(
                "FoxRunRos2CustomOutboundBudgetPolicy.MaximumBytes",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "catch (global::Unity.FoxgloveSDK.Schemas.Ros2Msg.Ros2CdrWriterBudgetExceededException exception)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "var __sequenceCount_",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "__index < __sequence_",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "writer.WriteString(__sequence_",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "?? string.Empty);",
                source,
                StringComparison.Ordinal);
            Assert.Equal(
                4L * 1024L * 1024L,
                FoxRunRos2CustomOutboundBudgetPolicy.MaximumBytes);
        }

        [Fact]
        public void BoundedCdrWriterRejectsGrowthBeforeAllocatingPastItsCap()
        {
            var writer =
                new Unity.FoxgloveSDK.Schemas.Ros2Msg.Ros2CdrWriter(
                    capacityBytes: 4,
                    maximumBytes: 8);

            writer.WriteUInt32(184);
            Assert.Equal(8, writer.Position);
            Assert.Equal(8, writer.ToArray().Length);
            Assert.Throws<Unity.FoxgloveSDK.Schemas.Ros2Msg.Ros2CdrWriterBudgetExceededException>(
                () => writer.WriteUInt8(1));
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

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        public void GeneratedCustomMapperMatrixCompilesRoundTripsNullableEnumWritesCdrAndDisposesNestedSequences()
        {
            var stateMember = CreateCustomMember();
            var stateShape = stateMember.Ros2CustomDtoShape;
            var nestedShape = stateShape.Members
                .Single(member => member.Name == "Nested")
                .NestedShape;
            var otherShape = new FoxRunRos2CustomDtoShape(
                "Phase184.OtherState",
                "phase184/OtherState",
                "Phase184OtherStateA184D001",
                hasPublicParameterlessConstructor: true,
                isSupported: true,
                members: new[]
                {
                    new FoxRunRos2CustomDtoMemberShape(
                        "Children",
                        "children",
                        FoxRunRos2CustomDtoMemberKind.Sequence,
                        "Phase181.NestedState[]",
                        "Phase181NestedState3281D0E21244[]",
                        "Phase181.NestedState",
                        nestedShape.CanonicalIdentity,
                        true,
                        true,
                        true,
                        FoxRunRos2CustomDtoSequenceRepresentation.Array,
                        nestedShape),
                    new FoxRunRos2CustomDtoMemberShape(
                        "OptionalCount",
                        "optional_count",
                        FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Nullable<System.Int32>",
                        "int32",
                        "",
                        "",
                        true,
                        true,
                        true),
                    new FoxRunRos2CustomDtoMemberShape(
                        "OptionalKind",
                        "optional_kind",
                        FoxRunRos2CustomDtoMemberKind.Enum,
                        "System.Nullable<Phase184.OptionalKind>",
                        "uint16",
                        "",
                        "",
                        true,
                        true,
                        true),
                },
                diagnostics: Array.Empty<string>());
            var members = new[]
            {
                CreateCustomMember(
                    "PublishReadonly",
                    "Phase181.State",
                    "/phase184/a",
                    (int)FoxRunFlow.Publish,
                    FoxRunGenerationDescriptorConstants.InheritSource,
                    stateShape),
                CreateCustomMember(
                    "PublishGetter",
                    "Phase181.State",
                    "/phase184/b",
                    (int)FoxRunFlow.Publish,
                    FoxRunGenerationDescriptorConstants.InheritSource,
                    stateShape),
                CreateCustomMember(
                    "SubscribeOther",
                    "Phase184.OtherState",
                    "/phase184/c",
                    (int)FoxRunFlow.Subscribe,
                    FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                    otherShape),
                CreateCustomMember(
                    "DuplexState",
                    "Phase181.State",
                    "/phase184/d",
                    (int)FoxRunFlow.PublishAndSubscribe,
                    FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                    stateShape),
                CreateCustomMember(
                    "PublishOther",
                    "Phase184.OtherState",
                    "/phase184/e",
                    (int)FoxRunFlow.Publish,
                    FoxRunGenerationDescriptorConstants.InheritSource,
                    otherShape),
            };
            var generated = FoxgloveSourceEmitter.EmitClass(
                "Phase184",
                "GeneratedMatrix",
                members);
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[]
                {
                    "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                    "UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES",
                });
            var support = CSharpSyntaxTree.ParseText(CustomMapperDynamicSupport, parseOptions);
            var compilation = CSharpCompilation.Create(
                "phase184_custom_mapper_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(generated, parseOptions),
                    support,
                },
                DynamicReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(
                emit.Success,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Where(
                        diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var hostType = assembly.GetType("Phase184.GeneratedMatrix", throwOnError: true);
            var stateType = assembly.GetType("Phase181.State", throwOnError: true);
            var otherType = assembly.GetType("Phase184.OtherState", throwOnError: true);
            var kindType = assembly.GetType("Phase184.OptionalKind", throwOnError: true);
            var host = Activator.CreateInstance(hostType);

            // Shared mapper suffixes are stable across pure Publish,
            // Subscribe, P&S, and mixed DTO ordering.
            var expectedParameterTypes = new[]
            {
                stateType,
                stateType,
                otherType,
                stateType,
                otherType,
            };
            for (var index = 0; index < expectedParameterTypes.Length; index++)
            {
                var mapper = hostType.GetMethod(
                    "__FoxRunRos2CustomMapDtoToEnvelope_" + index,
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(mapper);
                Assert.Equal(expectedParameterTypes[index], mapper.GetParameters()[0].ParameterType);
            }

            var outboundContext = Unity2Foxglove.Ros2ForUnity.Native
                .FoxRunRos2CustomOutboundMappingPolicy.CreateContext();
            var other = Activator.CreateInstance(otherType);
            var nestedType = assembly.GetType("Phase181.NestedState", throwOnError: true);
            var firstNested = Activator.CreateInstance(nestedType);
            var secondNested = Activator.CreateInstance(nestedType);
            nestedType.GetProperty("Label").SetValue(firstNested, "first");
            nestedType.GetProperty("Label").SetValue(secondNested, "second");
            var children = Array.CreateInstance(nestedType, 2);
            children.SetValue(firstNested, 0);
            children.SetValue(secondNested, 1);
            otherType.GetProperty("Children").SetValue(other, children);
            otherType.GetProperty("OptionalCount").SetValue(other, 7);
            otherType.GetProperty("OptionalKind").SetValue(
                other,
                Enum.ToObject(kindType, 2));

            var mapOther = hostType.GetMethod(
                "__FoxRunRos2CustomMapDtoToEnvelope_4",
                BindingFlags.NonPublic | BindingFlags.Static);
            var envelope = mapOther.Invoke(
                null,
                new object[] { other, "origin-a", 9UL, 184000000123UL, outboundContext });
            var payload = envelope.GetType().GetProperty("Payload").GetValue(envelope);
            Assert.Equal((ushort)2, payload.GetType().GetProperty("Optional_kind").GetValue(payload));
            Assert.True((bool)payload.GetType().GetProperty("Foxrun_has_optional_kind").GetValue(payload));
            var rosChildren = (Array)payload.GetType().GetProperty("Children").GetValue(payload);
            Assert.Equal(2, rosChildren.Length);
            Assert.Equal("first", rosChildren.GetValue(0).GetType().GetProperty("Label").GetValue(rosChildren.GetValue(0)));

            var mapToDto = hostType.GetMethod(
                "__FoxRunRos2CustomMapPayloadToDto_2",
                BindingFlags.NonPublic | BindingFlags.Static);
            var roundTrip = mapToDto.Invoke(null, new[] { payload });
            Assert.Equal(7, otherType.GetProperty("OptionalCount").GetValue(roundTrip));
            Assert.Equal(
                Enum.ToObject(kindType, 2),
                otherType.GetProperty("OptionalKind").GetValue(roundTrip));
            payload.GetType().GetProperty("Foxrun_has_optional_kind").SetValue(payload, false);
            var absentRoundTrip = mapToDto.Invoke(null, new[] { payload });
            Assert.Null(otherType.GetProperty("OptionalKind").GetValue(absentRoundTrip));

            var dispose = hostType.GetMethod(
                "__FoxRunRos2CustomDisposeEnvelope_4",
                BindingFlags.NonPublic | BindingFlags.Static);
            dispose.Invoke(null, new[] { envelope });
            Assert.Null(payload.GetType().GetProperty("Children").GetValue(payload));
            Assert.Equal(1, rosChildren.GetValue(0).GetType().GetProperty("DisposeCalls").GetValue(rosChildren.GetValue(0)));
            Assert.Equal(1, rosChildren.GetValue(1).GetType().GetProperty("DisposeCalls").GetValue(rosChildren.GetValue(1)));
            Assert.Equal(1, payload.GetType().GetProperty("DisposeCalls").GetValue(payload));
            Assert.Equal(1, envelope.GetType().GetProperty("DisposeCalls").GetValue(envelope));

            // The publish-only getter remains legal and two same-DTO topics
            // plus a different root sharing NestedState compile without CDR
            // helper collisions. Read the nullable enum and adjacent presence
            // bit from the real generated XCDR1 payload for null and value.
            var publishOther = hostType.GetProperty("PublishOther").GetValue(host);
            var beginCapture = typeof(IFoxglovePublishCaptureSource).GetMethod(
                nameof(IFoxglovePublishCaptureSource.FoxgloveLog_BeginCapture));
            Assert.True((bool)beginCapture.Invoke(host, new object[] { 3 }));
            var nullCdr = InvokeCdrBuilder(hostType, host, 3);
            AssertNullableEnumCdr(nullCdr, 0, false);
            typeof(IFoxglovePublishCaptureSource)
                .GetMethod(nameof(IFoxglovePublishCaptureSource.FoxgloveLog_EndCapture))
                .Invoke(host, new object[] { 3 });
            otherType.GetProperty("OptionalKind").SetValue(
                publishOther,
                Enum.ToObject(kindType, 2));
            Assert.Equal(
                Enum.ToObject(kindType, 2),
                otherType.GetProperty("OptionalKind").GetValue(publishOther));
            Assert.True((bool)beginCapture.Invoke(host, new object[] { 3 }));
            var valueCdr = InvokeCdrBuilder(hostType, host, 3);
            AssertNullableEnumCdr(valueCdr, 2, true);
        }
#endif

        private static byte[] InvokeCdrBuilder(Type hostType, object host, int topicIndex)
        {
            var method = hostType.GetMethod(
                "__TryBuildFoxRunRos2Cdr_" + topicIndex,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var arguments = new object[] { 184000000123UL, null, null };
            Assert.True((bool)method.Invoke(host, arguments), arguments[2]?.ToString());
            return Assert.IsType<byte[]>(arguments[1]);
        }

        private static void AssertNullableEnumCdr(byte[] payload, ushort expectedValue, bool expectedPresence)
        {
            var reader = new Unity.FoxgloveSDK.Schemas.Ros2Msg.Ros2CdrReader(payload);
            reader.ReadString();
            reader.ReadUInt64();
            reader.ReadInt32();
            reader.ReadUInt32();
            Assert.Equal(0U, reader.ReadUInt32()); // children
            Assert.False(reader.ReadBool()); // children presence
            Assert.Equal(0, reader.ReadInt32()); // optional_count
            Assert.False(reader.ReadBool());
            var actualValue = reader.ReadUInt16();
            Assert.True(
                actualValue == expectedValue,
                $"Expected nullable enum {expectedValue}, got {actualValue}; payload={Convert.ToHexString(payload)}.");
            Assert.Equal(expectedPresence, reader.ReadBool());
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        private static IReadOnlyList<MetadataReference> DynamicReferences()
        {
            // Force the optional native/ros2cs contracts into the load context
            // before capturing its managed reference set.
            _ = typeof(ROS2.Message);
            _ = typeof(Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2CustomPublisherSource);
            var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator);
            return trusted
                .Concat(
                    AppDomain.CurrentDomain.GetAssemblies()
                        .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                        .Select(assembly => assembly.Location))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }
#endif

        private static FoxgloveSourceEmitter.TopicMember CreateCustomMember(
            string memberName,
            string typeName,
            string topic,
            int mode,
            string source,
            FoxRunRos2CustomDtoShape shape)
            => new FoxgloveSourceEmitter.TopicMember(
                memberName,
                typeName,
                topic,
                10f,
                typeName,
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                mode: mode,
                canonicalType: typeName,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                source: source,
                qosProfile: FoxRunGenerationDescriptorConstants.DefaultQosProfile,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: null,
                ros2CustomDtoShape: shape,
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

        private const string CustomMapperDynamicSupport = @"
namespace UnityEngine.Scripting
{
    public sealed class PreserveAttribute : global::System.Attribute { }
}
namespace Phase181
{
    public enum StateKind { Zero = 0, One = 1, Two = 2 }
    public sealed class NestedState
    {
        public bool Enabled { get; set; }
        public string Label { get; set; }
    }
    public sealed class State
    {
        public byte[] Bytes { get; set; }
        public int Count { get; set; }
        public global::System.Collections.Generic.List<NestedState> Children { get; set; }
        public StateKind Kind { get; set; }
        public string[] Labels { get; set; }
        public string Message { get; set; }
        public NestedState Nested { get; set; }
        public int? OptionalCount { get; set; }
        public string OptionalText { get; set; }
        public global::System.Collections.Generic.List<long> Values { get; set; }
    }
}
namespace Phase184
{
    public enum OptionalKind { Zero = 0, One = 1, Two = 2 }
    public sealed class OtherState
    {
        public global::Phase181.NestedState[] Children { get; set; }
        public int? OptionalCount { get; set; }
        public OptionalKind? OptionalKind { get; set; }
    }
    public partial class GeneratedMatrix
    {
        private readonly global::Phase181.State PublishReadonly = new global::Phase181.State();
        public global::Phase181.State PublishGetter { get; } = new global::Phase181.State();
        public global::Phase184.OtherState SubscribeOther { get; set; } = new global::Phase184.OtherState();
        public global::Phase181.State DuplexState { get; set; } = new global::Phase181.State();
        public global::Phase184.OtherState PublishOther { get; } = new global::Phase184.OtherState();
    }
}
namespace builtin_interfaces.msg
{
    public sealed class Time : global::ROS2.Message, global::System.IDisposable
    {
        public int Sec { get; set; }
        public uint Nanosec { get; set; }
        public bool IsDisposed { get; private set; }
        public void Dispose() { IsDisposed = true; }
    }
}
namespace Unity2Foxglove.FoxRun.CustomRos2Typesupport
{
    public static class FoxRunRos2CustomTypesupportMetadata
    {
        public const int InterfaceRevision = 1;
        public const string InterfaceDigest = ""digest"";
        public const string BaseRuntimePackageId = ""runtime"";
    }
}
namespace unity2foxglove_foxrun_interfaces_v1.msg
{
    public abstract class DisposableMessage : global::ROS2.Message, global::System.IDisposable
    {
        public int DisposeCalls { get; private set; }
        public bool IsDisposed { get; private set; }
        public virtual void Dispose() { DisposeCalls++; IsDisposed = true; }
    }
    public sealed class Phase181NestedState3281D0E21244 : DisposableMessage
    {
        public bool Enabled { get; set; }
        public string Label { get; set; }
        public bool Foxrun_has_label { get; set; }
    }
    public sealed class Phase181State48D288ED82F1 : DisposableMessage
    {
        public byte[] Bytes { get; set; }
        public bool Foxrun_has_bytes { get; set; }
        public int Count { get; set; }
        public Phase181NestedState3281D0E21244[] Children { get; set; }
        public bool Foxrun_has_children { get; set; }
        public ushort Kind { get; set; }
        public string[] Labels { get; set; }
        public bool Foxrun_has_labels { get; set; }
        public string Message { get; set; }
        public bool Foxrun_has_message { get; set; }
        public Phase181NestedState3281D0E21244 Nested { get; set; }
        public bool Foxrun_has_nested { get; set; }
        public int Optional_count { get; set; }
        public bool Foxrun_has_optional_count { get; set; }
        public string Optional_text { get; set; }
        public bool Foxrun_has_optional_text { get; set; }
        public long[] Values { get; set; }
        public bool Foxrun_has_values { get; set; }
    }
    public sealed class Phase181State48D288ED82F1Envelope : DisposableMessage
    {
        public string Foxrun_origin_id { get; set; }
        public ulong Foxrun_sequence { get; set; }
        public global::builtin_interfaces.msg.Time Foxrun_stamp { get; set; }
        public Phase181State48D288ED82F1 Payload { get; set; }
    }
    public sealed class Phase184OtherStateA184D001 : DisposableMessage
    {
        public Phase181NestedState3281D0E21244[] Children { get; set; }
        public bool Foxrun_has_children { get; set; }
        public int Optional_count { get; set; }
        public bool Foxrun_has_optional_count { get; set; }
        public ushort Optional_kind { get; set; }
        public bool Foxrun_has_optional_kind { get; set; }
    }
    public sealed class Phase184OtherStateA184D001Envelope : DisposableMessage
    {
        public string Foxrun_origin_id { get; set; }
        public ulong Foxrun_sequence { get; set; }
        public global::builtin_interfaces.msg.Time Foxrun_stamp { get; set; }
        public Phase184OtherStateA184D001 Payload { get; set; }
    }
}";

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
                    new FoxRunRos2CustomDtoMemberShape("Children", "children", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.Collections.Generic.List<Phase181.NestedState>",
                        "Phase181NestedState3281D0E21244[]",
                        "Phase181.NestedState",
                        nested.CanonicalIdentity,
                        true,
                        true,
                        true,
                        FoxRunRos2CustomDtoSequenceRepresentation.List,
                        nested),
                    new FoxRunRos2CustomDtoMemberShape("Kind", "kind", FoxRunRos2CustomDtoMemberKind.Enum,
                        "Phase181.StateKind", "uint16", "", "", false, true, true),
                    new FoxRunRos2CustomDtoMemberShape("Labels", "labels", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.String[]", "string[]", "System.String", "", true, true, true,
                        FoxRunRos2CustomDtoSequenceRepresentation.Array),
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
