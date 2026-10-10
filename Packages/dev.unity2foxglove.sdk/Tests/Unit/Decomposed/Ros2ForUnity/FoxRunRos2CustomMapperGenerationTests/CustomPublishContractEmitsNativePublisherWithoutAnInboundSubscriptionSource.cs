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


public sealed partial class FoxRunRos2CustomMapperGenerationTests
    {
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
                    "UNITY_EDITOR_WIN",
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
        [Trait("Phase", "187-R2-H02-023")]
        public void GeneratedCustomMapperAttemptsEveryCleanupAndPreservesFirstCleanupFailure()
        {
            var nestedShape = CreateCustomMember().Ros2CustomDtoShape.Members
                .Single(x => x.Name == "Nested").NestedShape;
            var member = CreateCustomMember(
                "CleanupProbe",
                "Phase184.OtherState",
                "/phase184/cleanup-probe",
                (int)FoxRunFlow.Publish,
                FoxRunR2fuGenerationConstants.Inherit,
                new FoxRunRos2CustomDtoShape(
                    "Phase184.OtherState",
                    "phase184/OtherState",
                    "Phase184OtherStateA184D001",
                    hasPublicParameterlessConstructor: true,
                    isSupported: true,
                    members: new[]
                    {
                        new FoxRunRos2CustomDtoMemberShape(
                            "Children", "children", FoxRunRos2CustomDtoMemberKind.Sequence,
                            "Phase181.NestedState[]", "Phase181NestedState3281D0E21244[]",
                            "Phase181.NestedState", nestedShape.CanonicalIdentity, true, true, true,
                            FoxRunRos2CustomDtoSequenceRepresentation.Array,
                            nestedShape),
                    },
                    diagnostics: Array.Empty<string>()));
            var generated = EmitR2fuClass("Phase184", "GeneratedCleanupProbe", new[] { member });
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[]
                {
                    "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                    "UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES",
                    "UNITY_EDITOR_WIN",
                });
            var compilation = CSharpCompilation.Create(
                "phase184_custom_cleanup_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(generated, parseOptions),
                    CSharpSyntaxTree.ParseText(CustomMapperDynamicSupport, parseOptions),
                },
                DynamicReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error)));
            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var hostType = assembly.GetType("Phase184.GeneratedCleanupProbe", throwOnError: true);
            var envelopeType = assembly.GetType("unity2foxglove_foxrun_interfaces_v1.msg.Phase184OtherStateA184D001Envelope", throwOnError: true);
            var payloadType = assembly.GetType("unity2foxglove_foxrun_interfaces_v1.msg.Phase184OtherStateA184D001", throwOnError: true);
            var nestedType = assembly.GetType("unity2foxglove_foxrun_interfaces_v1.msg.Phase181NestedState3281D0E21244", throwOnError: true);
            var timeType = assembly.GetType("builtin_interfaces.msg.Time", throwOnError: true);
            var envelope = Activator.CreateInstance(envelopeType);
            var payload = Activator.CreateInstance(payloadType);
            var children = Array.CreateInstance(nestedType, 2);
            var first = Activator.CreateInstance(nestedType);
            var second = Activator.CreateInstance(nestedType);
            nestedType.GetProperty("ThrowOnDispose").SetValue(first, true);
            nestedType.GetProperty("DisposeFailureMessage").SetValue(first, "first-child-cleanup");
            nestedType.GetProperty("ThrowOnDispose").SetValue(second, true);
            children.SetValue(first, 0);
            children.SetValue(second, 1);
            payloadType.GetProperty("Children").SetValue(payload, children);
            var stamp = Activator.CreateInstance(timeType);
            timeType.GetProperty("ThrowOnDispose").SetValue(stamp, true);
            envelopeType.GetProperty("Payload").SetValue(envelope, payload);
            envelopeType.GetProperty("Foxrun_stamp").SetValue(envelope, stamp);
            envelopeType.GetProperty("ThrowOnDispose").SetValue(envelope, true);

            var dispose = hostType.GetMethod("__FoxRunRos2CustomDisposeEnvelope_0", BindingFlags.NonPublic | BindingFlags.Static);
            var thrown = Assert.Throws<TargetInvocationException>(() => dispose.Invoke(null, new[] { envelope }));
            Assert.Equal("first-child-cleanup", thrown.InnerException.Message);
            Assert.Equal(1, nestedType.GetProperty("DisposeCalls").GetValue(first));
            Assert.Equal(1, nestedType.GetProperty("DisposeCalls").GetValue(second));
            Assert.Equal(1, payloadType.GetProperty("DisposeCalls").GetValue(payload));
            Assert.Equal(1, timeType.GetProperty("DisposeCalls").GetValue(stamp));
            Assert.Equal(1, envelopeType.GetProperty("DisposeCalls").GetValue(envelope));
            Assert.Null(payloadType.GetProperty("Children").GetValue(payload));
            Assert.Null(envelopeType.GetProperty("Payload").GetValue(envelope));
            Assert.Null(envelopeType.GetProperty("Foxrun_stamp").GetValue(envelope));
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
            Assert.Contains(".TryEnqueueDeferredOwnedAfterAdmission(", generated, StringComparison.Ordinal);

            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[]
                {
                    "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                    "UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES",
                    "UNITY_EDITOR_WIN",
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

    }
}
