// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: RED contract for deterministic generated typed MessagePack publication.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{


public sealed partial class FoxRunMessagePackEmitterTests
    {
        [Fact]
        [Trait("Phase", "185-F")]
        public void InheritedUnavailableMessagePackShapeDoesNotCrashEmission()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "_position",
                "UnityEngine.Vector3",
                "/phase185f/legacy-vector",
                10f,
                string.Empty,
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                canonicalType: "unity.vector3.float32",
                encoding:
                    FoxRunGenerationDescriptorConstants.InheritEncoding);

            var generated = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "LegacyVectorPublisher",
                new[] { member });
            var declaration = @"
namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class LegacyVectorPublisher
    {
        private UnityEngine.Vector3 _position;
    }
}
";
            var compilation = CSharpCompilation.Create(
                "Phase185FLegacyInherited_"
                + Guid.NewGuid().ToString("N"),
                GeneratedPublishSyntaxTrees(declaration, generated),
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);

            Assert.DoesNotContain(
                "__BuildFoxRunMessagePack_0",
                generated,
                StringComparison.Ordinal);
            Assert.True(
                emit.Success,
                "Inherited non-MessagePack declaration failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(
                        diagnostic => diagnostic.ToString())));
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void InheritedObserversBranchOnFrozenMessagePackEncoding()
        {
            const string topic = "/phase185f/inherited-observer";
            var assembly = CompileGenerated(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class InheritedObserverPublisher
    {
        [FoxRun(""" + topic + @""", Mode = FoxRunFlow.Publish)]
        public int Count = 7;
    }
}");
            var type = assembly.GetType(
                "Demo.InheritedObserverPublisher",
                throwOnError: true);
            var instance = Activator.CreateInstance(type);
            var topicIndex = FindTopicIndex(instance, topic);
            var logicalContract = GetContract(instance, topicIndex);

            Assert.Same(
                logicalContract,
                GetContract(instance, topicIndex));

            var messagePackBus = new FoxTopicBus();
            var messagePackEnvelopes =
                new List<FoxTopicEnvelope<byte[]>>();
            messagePackBus.Subscribe<byte[]>(
                topic,
                envelope => messagePackEnvelopes.Add(envelope));
            SetCaptureEncoding(
                type,
                instance,
                topicIndex,
                FoxRunEncoding.MessagePack);
            Assert.True(BeginCapture(instance, topicIndex));
            Assert.True(HasObservers(
                instance,
                topicIndex,
                messagePackBus));
            PublishCapturedToObservers(
                instance,
                topicIndex,
                messagePackBus,
                1851UL);
            PublishToBus(
                instance,
                topicIndex,
                messagePackBus,
                1852UL);

            Assert.Equal(2, messagePackEnvelopes.Count);
            var wireContract = logicalContract.ForWireEncoding(
                FoxRunEncoding.MessagePack);
            Assert.All(
                messagePackEnvelopes,
                envelope =>
                {
                    Assert.Same(wireContract, envelope.Contract);
                    Assert.Equal("msgpack", envelope.Contract.Encoding);
                    Assert.Equal(string.Empty, envelope.Contract.SchemaName);
                });
            Assert.Same(
                messagePackEnvelopes[0].Payload,
                messagePackEnvelopes[1].Payload);
            var reader = new FoxgloveMsgPackReader(
                messagePackEnvelopes[0].Payload,
                FoxgloveMsgPackReadLimits.ForPayloadBytes(1024));
            Assert.True(reader.TryReadMapHeader(out var mapCount), reader.Error);
            Assert.Equal(1, mapCount);
            Assert.True(reader.TryReadString(out var key), reader.Error);
            Assert.Equal("Count", key);
            Assert.True(reader.TryReadInt32(out var value), reader.Error);
            Assert.Equal(7, value);
            Assert.True(reader.TryComplete(), reader.Error);
            EndCapture(instance, topicIndex);

            var jsonBus = new FoxTopicBus();
            var jsonEnvelopes =
                new List<FoxTopicEnvelope<Dictionary<string, object>>>();
            jsonBus.Subscribe<Dictionary<string, object>>(
                topic,
                envelope => jsonEnvelopes.Add(envelope));
            SetCaptureEncoding(
                type,
                instance,
                topicIndex,
                FoxRunEncoding.JSON);
            Assert.True(BeginCapture(instance, topicIndex));
            Assert.True(HasObservers(instance, topicIndex, jsonBus));
            PublishCapturedToObservers(
                instance,
                topicIndex,
                jsonBus,
                1853UL);
            PublishToBus(
                instance,
                topicIndex,
                jsonBus,
                1854UL);

            Assert.Equal(2, jsonEnvelopes.Count);
            Assert.All(
                jsonEnvelopes,
                envelope =>
                {
                    Assert.Same(logicalContract, envelope.Contract);
                    Assert.Equal("json", envelope.Contract.Encoding);
                    Assert.Equal(7, Assert.IsType<int>(
                        envelope.Payload["Count"]));
                });
            EndCapture(instance, topicIndex);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ExplicitMessagePackObserversUseSchemaLessStableWireContract()
        {
            const string topic = "/phase185f/explicit-observer";
            var assembly = CompileGenerated(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class ExplicitObserverPublisher
    {
        [FoxRun(""" + topic + @""", Mode = FoxRunFlow.Publish,
            Encoding = FoxRunEncoding.MessagePack,
            SchemaName = ""Demo.Logical"")]
        public int Value = 9;
    }
}");
            var type = assembly.GetType(
                "Demo.ExplicitObserverPublisher",
                throwOnError: true);
            var instance = Activator.CreateInstance(type);
            var topicIndex = FindTopicIndex(instance, topic);
            var logicalContract = GetContract(instance, topicIndex);
            Assert.Equal("Demo.Logical", logicalContract.SchemaName);
            Assert.Same(
                logicalContract,
                GetContract(instance, topicIndex));

            var bus = new FoxTopicBus();
            var envelopes = new List<FoxTopicEnvelope<byte[]>>();
            bus.Subscribe<byte[]>(
                topic,
                envelope => envelopes.Add(envelope));
            Assert.True(BeginCapture(instance, topicIndex));
            PublishCapturedToObservers(
                instance,
                topicIndex,
                bus,
                1856UL);
            PublishToBus(
                instance,
                topicIndex,
                bus,
                1857UL);

            Assert.Equal(2, envelopes.Count);
            var wireContract = logicalContract.ForWireEncoding(
                FoxRunEncoding.MessagePack);
            Assert.All(
                envelopes,
                envelope =>
                {
                    Assert.Same(wireContract, envelope.Contract);
                    Assert.Equal("msgpack", envelope.Contract.Encoding);
                    Assert.Equal(string.Empty, envelope.Contract.SchemaName);
                });
            EndCapture(instance, topicIndex);
        }

        [Theory]
        [InlineData("JSON")]
        [InlineData("Protobuf")]
        [Trait("Phase", "185-F")]
        public void CompiledNonMessagePackSinkSideChannelUsesOneJsonWireView(
            string encodingName)
        {
            const string topic = "/phase185f/compatible-sink";
            var assembly = CompileGenerated(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class CompatibleSinkPublisher
    {
        [FoxRun(""" + topic + @""", Mode = FoxRunFlow.Publish,
            Encoding = FoxRunEncoding." + encodingName + @")]
        public int Value = 42;
    }
}");
            var type = assembly.GetType(
                "Demo.CompatibleSinkPublisher",
                throwOnError: true);
            var instance = Activator.CreateInstance(type);
            var topicIndex = FindTopicIndex(instance, topic);
            var contract = GetContract(instance, topicIndex);
            var additive = new GeneratedRecordingSink();
            var external = new GeneratedExternalSink();
            var router = new FoxTopicSinkRouter();
            router.AddSink(additive);
            router.AddSink(external);
            Assert.True(router.Register(contract));

            Assert.True(BeginCapture(instance, topicIndex));
            PublishToSinks(instance, topicIndex, router, 1855UL);

            Assert.Equal(1, additive.PublishCalls);
            Assert.Equal(1, external.PublishCalls);
            Assert.Equal("json", additive.LastContract.Encoding);
            Assert.Same(
                additive.RegisteredContract,
                additive.LastContract);
            Assert.Contains(
                "\"Value\":42",
                Encoding.UTF8.GetString(additive.LastPayload),
                StringComparison.Ordinal);
            EndCapture(instance, topicIndex);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void GeneratedNullableValuesAvoidBoxingAndStructObjectsShortCircuitNullChecks()
        {
            var nullable = Member(
                "_optional",
                "System.Nullable<System.Int32>",
                FoxRunTypeShape.Canonical("int32", nullable: true));
            var vector = new FoxgloveSourceEmitter.TopicMember(
                "_position",
                "UnityEngine.Vector3",
                "/phase185f/nonnullable-vector",
                10f,
                string.Empty,
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                encoding:
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape:
                    FoxRunReflectionTypeShapeBuilder.Build(
                        typeof(UnityEngine.Vector3)));
            var source = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "NoBoxingPublisher",
                new[] { nullable, vector });

            Assert.Contains(
                "if (!__foxRunCapture_0_0.HasValue)",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "if ((object)__foxRunCapture_0_0 == null)",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "typeof(global::UnityEngine.Vector3).IsValueType",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "if ((object)__value == null)",
                source,
                StringComparison.Ordinal);
        }


    }
}
