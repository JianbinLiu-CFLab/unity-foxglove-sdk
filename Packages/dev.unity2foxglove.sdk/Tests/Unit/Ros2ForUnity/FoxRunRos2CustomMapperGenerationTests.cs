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
    public sealed class FoxRunRos2CustomMapperGenerationTests
    {
        [Fact]
        public void CustomDtoEmitUsesClosedEnvelopeMappersAndDedicatedNativeApplyPath()
        {
            var source = EmitR2fuClass(
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
                "#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES",
                StringComparison.Ordinal));
            Assert.DoesNotContain("__foxRunSuppressNextPublish_", customSection, StringComparison.Ordinal);
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

        [Fact]
        public void CustomPublishContractEmitsNativePublisherWithoutAnInboundSubscriptionSource()
        {
            var source = EmitR2fuClass(
                "Phase181",
                "CustomPublishSource",
                new[]
                {
                    CreateCustomMember(
                        mode: (int)FoxRunFlow.Publish,
                        source: FoxRunR2fuGenerationConstants.Inherit)
                });

            Assert.Contains("IFoxRunRos2CustomPublisherSource", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2RegisterCustomPublishers", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IFoxRunRos2CustomSubscriptionSource", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRunRos2RegisterCustomSubscriptions", source, StringComparison.Ordinal);
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        public void GeneratedCustomMapperMatrixCompilesRoundTripsNullableEnumAndDisposesNestedSequences()
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
                    FoxRunR2fuGenerationConstants.Inherit,
                    stateShape),
                CreateCustomMember(
                    "PublishGetter",
                    "Phase181.State",
                    "/phase184/b",
                    (int)FoxRunFlow.Publish,
                    FoxRunR2fuGenerationConstants.Inherit,
                    stateShape),
                CreateCustomMember(
                    "SubscribeOther",
                    "Phase184.OtherState",
                    "/phase184/c",
                    (int)FoxRunFlow.Subscribe,
                    FoxRunR2fuGenerationConstants.ProviderId,
                    otherShape),
                CreateCustomMember(
                    "DuplexState",
                    "Phase181.State",
                    "/phase184/d",
                    (int)FoxRunFlow.PublishAndSubscribe,
                    FoxRunR2fuGenerationConstants.ProviderId,
                    stateShape),
                CreateCustomMember(
                    "PublishOther",
                    "Phase184.OtherState",
                    "/phase184/e",
                    (int)FoxRunFlow.Publish,
                    FoxRunR2fuGenerationConstants.Inherit,
                    otherShape),
            };
            var generated = EmitR2fuClass(
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

        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void GeneratedCustomStreamDefersUserConstructionAndSettersUntilConsumerDrain()
        {
            var shape = new FoxRunRos2CustomDtoShape(
                "Phase184.StreamProbeState",
                "phase184/StreamProbeState",
                "Phase184StreamProbeState184E",
                hasPublicParameterlessConstructor: true,
                isSupported: true,
                members: new[]
                {
                    new FoxRunRos2CustomDtoMemberShape(
                        "Value", "value", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Int32", "int32", "", "", false, true, true),
                },
                diagnostics: Array.Empty<string>());
            var generated = EmitR2fuClass(
                "Phase184",
                "GeneratedStream",
                new[]
                {
                    CreateCustomMember(
                        "State",
                        "Phase184.StreamProbeState",
                        "/phase184/custom-stream",
                        (int)FoxRunFlow.Subscribe,
                        FoxRunR2fuGenerationConstants.ProviderId,
                        shape,
                        isStream: true),
                });
            Assert.Contains(
                "RegisterStream<global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope, global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope>",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "var __foxRunRos2CustomTryAdmit_0 = __foxRunRos2CustomStream_0 == null ? null : new global::System.Func<bool>(__foxRunRos2CustomStream_0.TryAdmitInput);",
                generated,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "throw new global::System.InvalidOperationException(\"FoxRunStream field is null",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(".TryEnqueueDeferredOwned(", generated, StringComparison.Ordinal);

            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[]
                {
                    "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                    "UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES",
                });
            var compilation = CSharpCompilation.Create(
                "phase184_custom_stream_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(generated, parseOptions),
                    CSharpSyntaxTree.ParseText(CustomStreamDynamicSupport, parseOptions),
                },
                DynamicReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(
                emit.Success,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var hostType = assembly.GetType("Phase184.GeneratedStream", throwOnError: true);
            var registrarType = assembly.GetType("Phase184.CapturingStreamRegistrar", throwOnError: true);
            var dtoType = assembly.GetType("Phase184.StreamProbeState", throwOnError: true);
            var envelopeType = assembly.GetType(
                "unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope",
                throwOnError: true);
            var payloadType = assembly.GetType(
                "unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184E",
                throwOnError: true);
            var host = Activator.CreateInstance(hostType);
            var registrar = Activator.CreateInstance(registrarType);
            ((Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2CustomSubscriptionSource)host)
                .FoxRunRos2RegisterCustomSubscriptions(
                    (Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2SubscriptionRegistrar)registrar);

            var borrowed = Activator.CreateInstance(envelopeType);
            var borrowedPayload = Activator.CreateInstance(payloadType);
            payloadType.GetProperty("Value").SetValue(borrowedPayload, 184);
            envelopeType.GetProperty("Payload").SetValue(borrowed, borrowedPayload);
            envelopeType.GetProperty("Foxrun_origin_id").SetValue(borrowed, "remote");
            Exception producerFailure = null;
            var producerThreadId = 0;
            var producer = new System.Threading.Thread(() =>
            {
                try
                {
                    producerThreadId = Environment.CurrentManagedThreadId;
                    registrarType.GetMethod("Emit").Invoke(registrar, new[] { borrowed });
                }
                catch (Exception exception)
                {
                    producerFailure = exception;
                }
            }) { IsBackground = true };
            producer.Start();
            Assert.True(producer.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(producerFailure);
            Assert.Equal(1, hostType.GetProperty("StreamCount").GetValue(host));
            Assert.Equal(0, dtoType.GetProperty("ConstructorThreadId").GetValue(null));
            Assert.Equal(0, dtoType.GetProperty("SetterThreadId").GetValue(null));

            var consumerThreadId = Environment.CurrentManagedThreadId;
            Assert.Equal(1, hostType.GetMethod("DrainStream").Invoke(host, null));

            Assert.NotEqual(producerThreadId, consumerThreadId);
            Assert.Equal(consumerThreadId, dtoType.GetProperty("ConstructorThreadId").GetValue(null));
            Assert.Equal(consumerThreadId, dtoType.GetProperty("SetterThreadId").GetValue(null));
            Assert.Equal(184, hostType.GetProperty("LastValue").GetValue(host));
            var ownedEnvelope = registrarType.GetProperty("LastOwnedEnvelope").GetValue(registrar);
            var ownedPayload = registrarType.GetProperty("LastOwnedPayload").GetValue(registrar);
            Assert.Equal(1, envelopeType.GetProperty("DisposeCalls").GetValue(ownedEnvelope));
            Assert.Equal(1, payloadType.GetProperty("DisposeCalls").GetValue(ownedPayload));
            Assert.Null(envelopeType.GetProperty("Payload").GetValue(ownedEnvelope));
        }
#endif

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

        private static TestR2fuMember CreateCustomMember(
            string memberName,
            string typeName,
            string topic,
            int mode,
            string source,
            FoxRunRos2CustomDtoShape shape,
            bool isStream = false)
            => new TestR2fuMember(
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
                qosProfile: FoxRunR2fuGenerationConstants.Inherit,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: null,
                ros2CustomDtoShape: shape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto,
                namedArgumentPresence:
                    FoxRunNamedArgumentPresence.Reliability
                    | FoxRunNamedArgumentPresence.Durability
                    | FoxRunNamedArgumentPresence.History
                    | FoxRunNamedArgumentPresence.Depth,
                qosReliability: "reliable",
                qosDurability: "volatile",
                qosHistory: "keep-last",
                qosDepth: 10,
                isStream: isStream);

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
        private void __FoxRunMarkRemoteApplied_2() { }
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

        private const string CustomStreamDynamicSupport = CustomMapperDynamicSupport + @"
namespace Phase184
{
    public sealed class StreamProbeState
    {
        private int _value;
        public StreamProbeState()
        {
            ConstructorThreadId = global::System.Environment.CurrentManagedThreadId;
        }
        public static int ConstructorThreadId { get; private set; }
        public static int SetterThreadId { get; private set; }
        public int Value
        {
            get => _value;
            set
            {
                SetterThreadId = global::System.Environment.CurrentManagedThreadId;
                _value = value;
            }
        }
    }

    public partial class GeneratedStream
    {
        private readonly global::Unity.FoxgloveSDK.Components.FoxRunStream<StreamProbeState> State =
            new global::Unity.FoxgloveSDK.Components.FoxRunStream<StreamProbeState>();
        public int StreamCount => State.Count;
        public int LastValue { get; private set; }
        public int DrainStream() => State.Drain(value => LastValue = value.Value);
    }

    public sealed class CapturingStreamRegistrar :
        global::Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2SubscriptionRegistrar
    {
        private global::System.Func<bool> _tryAdmit;
        private global::System.Func<
            global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope,
            global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext,
            global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope> _materialize;
        private global::System.Action<
            global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope> _transfer;

        public global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope LastOwnedEnvelope { get; private set; }
        public global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184E LastOwnedPayload { get; private set; }

        public void Register<T>(
            global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract contract,
            global::System.Func<T, global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext, T> copy,
            global::System.Action<T> dispose,
            global::System.Action<T> apply,
            global::System.Func<T, bool> clearIfOwned,
            global::System.Func<T, T, bool> valuesEqual,
            global::System.Func<bool> consumeTrigger,
            global::System.Func<bool> canApply)
            where T : global::ROS2.Message, new()
        {
            throw new global::System.InvalidOperationException(""Expected stream registration."");
        }

        public void RegisterStream<TTransport, TSample>(
            global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract contract,
            global::System.Func<bool> tryAdmitInput,
            global::System.Func<TTransport, global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext, TSample> materializeOwned,
            global::System.Action<TSample> transferOwned,
            global::System.Action clearOwned)
            where TTransport : global::ROS2.Message, new()
        {
            _tryAdmit = tryAdmitInput;
            _materialize = (source, budget) =>
                (global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope)(object)
                    materializeOwned((TTransport)(object)source, budget);
            _transfer = owned => transferOwned((TSample)(object)owned);
        }

        public void Emit(
            global::unity2foxglove_foxrun_interfaces_v1.msg.Phase184StreamProbeState184EEnvelope borrowed)
        {
            if (!_tryAdmit()) return;
            LastOwnedEnvelope = _materialize(
                borrowed,
                new global::Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CopyContext(1024 * 1024));
            LastOwnedPayload = LastOwnedEnvelope.Payload;
            _transfer(LastOwnedEnvelope);
        }
    }
}

namespace unity2foxglove_foxrun_interfaces_v1.msg
{
    public sealed class Phase184StreamProbeState184E : DisposableMessage
    {
        public int Value { get; set; }
    }

    public sealed class Phase184StreamProbeState184EEnvelope : DisposableMessage
    {
        public string Foxrun_origin_id { get; set; }
        public ulong Foxrun_sequence { get; set; }
        public global::builtin_interfaces.msg.Time Foxrun_stamp { get; set; }
        public Phase184StreamProbeState184E Payload { get; set; }
    }
}";

        private static TestR2fuMember CreateCustomMember(
            int mode = (int)FoxRunFlow.PublishAndSubscribe,
            string source = FoxRunR2fuGenerationConstants.ProviderId)
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
            return new TestR2fuMember(
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
                qosProfile: FoxRunR2fuGenerationConstants.Inherit,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: null,
                ros2CustomDtoShape: state,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto,
                namedArgumentPresence:
                    FoxRunNamedArgumentPresence.Reliability
                    | FoxRunNamedArgumentPresence.Durability
                    | FoxRunNamedArgumentPresence.History
                    | FoxRunNamedArgumentPresence.Depth,
                qosReliability: "reliable",
                qosDurability: "volatile",
                qosHistory: "keep-last",
                qosDepth: 10);
        }

        private static string EmitR2fuClass(
            string ns,
            string className,
            IReadOnlyList<TestR2fuMember> members)
        {
            var inputMembers = members
                .Where(
                    member =>
                        member.Mode == (int)FoxRunFlow.Subscribe
                        || member.Mode
                        == (int)FoxRunFlow.PublishAndSubscribe)
                .Cast<IFoxRunR2fuEmitterMember>()
                .ToList();
            var publishTopics = members
                .Where(
                    member =>
                        member.Mode != (int)FoxRunFlow.Subscribe)
                .Select(member => member.Topic)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToList();
            var customPublishMembers = members
                .Where(
                    member =>
                        member.Mode != (int)FoxRunFlow.Subscribe
                        && IsSupportedCustom(member))
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .Cast<IFoxRunR2fuEmitterMember>()
                .ToList();
            var mapperMembers = inputMembers
                .Concat(customPublishMembers)
                .Distinct()
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(member => member.MemberName, StringComparer.Ordinal)
                .ToList();

            var output = new StringBuilder();
            Ros2InputDispatchEmitter.EmitConditionalPartial(
                output,
                ns,
                className,
                Array.Empty<IFoxRunR2fuEmitterMember>(),
                publishTopics);
            Ros2CustomDtoMapperEmitter.EmitConditionalPartial(
                output,
                ns,
                className,
                mapperMembers,
                inputMembers,
                publishTopics);
            Ros2CustomPublishEmitter.EmitConditionalPartial(
                output,
                ns,
                className,
                customPublishMembers,
                mapperMembers);
            return output.ToString();
        }

        private static bool IsSupportedCustom(
            TestR2fuMember member)
            => member != null
               && member.GeneratesRos2NativeRegistration
               && member.Ros2ContractKind
               == FoxRunRos2ContractKind.CustomDto
               && member.Ros2CustomDtoShape != null
               && member.Ros2CustomDtoShape.IsSupported;

        private sealed class TestR2fuMember :
            IFoxRunR2fuEmitterMember
        {
            internal TestR2fuMember(
                string memberName,
                string typeName,
                string topic,
                float hz,
                string schemaName,
                int policy,
                float tolerance,
                int mode,
                string canonicalType,
                string encoding,
                string source,
                string qosProfile,
                bool generatesWebSocketCodec,
                bool generatesRos2NativeRegistration,
                FoxRunRos2MessageShape ros2MessageShape,
                FoxRunRos2CustomDtoShape ros2CustomDtoShape,
                FoxRunRos2ContractKind ros2ContractKind,
                FoxRunNamedArgumentPresence namedArgumentPresence,
                string qosReliability,
                string qosDurability,
                string qosHistory,
                int qosDepth,
                bool isStream = false)
            {
                MemberName = memberName;
                TypeName = typeName;
                Topic = topic;
                Hz = hz;
                SchemaName = schemaName;
                Policy = policy;
                Mode = mode;
                Encoding = encoding;
                Source = source;
                QosProfile = qosProfile;
                GeneratesRos2NativeRegistration =
                    generatesRos2NativeRegistration;
                Ros2MessageShape = ros2MessageShape;
                Ros2CustomDtoShape = ros2CustomDtoShape;
                Ros2ContractKind = ros2ContractKind;
                NamedArgumentPresence = namedArgumentPresence;
                QosReliability = qosReliability;
                QosDurability = qosDurability;
                QosHistory = qosHistory;
                QosDepth = qosDepth;
                IsStream = isStream;
            }

            public string MemberName { get; }
            public string TypeName { get; }
            public string Topic { get; }
            public float Hz { get; }
            public bool HasExplicitHz => true;
            public string SchemaName { get; }
            public int Policy { get; }
            public int Mode { get; }
            public string OnlyIf => string.Empty;
            public FoxRunConditionMemberKind ConditionMemberKind =>
                FoxRunConditionMemberKind.None;
            public string Encoding { get; }
            public FoxRunNamedArgumentPresence NamedArgumentPresence
            {
                get;
            }
            public bool IsStream { get; }
            public string Source { get; }
            public string Targets =>
                FoxRunR2fuGenerationConstants.Inherit;
            public string QosProfile { get; }
            public string QosReliability { get; }
            public string QosDurability { get; }
            public string QosHistory { get; }
            public int QosDepth { get; }
            public bool GeneratesRos2NativeRegistration { get; }
            public FoxRunRos2MessageShape Ros2MessageShape { get; }
            public FoxRunRos2CustomDtoShape Ros2CustomDtoShape
            {
                get;
            }
            public FoxRunRos2ContractKind Ros2ContractKind { get; }
        }
    }
}
