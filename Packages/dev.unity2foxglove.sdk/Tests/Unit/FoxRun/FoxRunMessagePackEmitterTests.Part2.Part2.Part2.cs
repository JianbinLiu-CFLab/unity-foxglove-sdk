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
        public void GeneratedPayloadsBindMultiFieldCapturesByMemberAndEncodeUnsignedAndNullCollections()
        {
            var topics = new[] { "/phase190/binding", "/phase190/nulllist", "/phase190/unsigned" };
            var topicMap = new Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>>
            {
                // Member-name order (_zeta, alpha) differs from JSON-name order (alpha, zeta).
                ["/phase190/binding"] = new List<FoxgloveSourceEmitter.TopicMember>
                {
                    Phase190Member("_zeta", "System.Double", "/phase190/binding", FoxRunTypeShape.Canonical("float64")),
                    Phase190Member("alpha", "System.Int32", "/phase190/binding", FoxRunTypeShape.Canonical("int32"))
                },
                ["/phase190/nulllist"] = new List<FoxgloveSourceEmitter.TopicMember>
                {
                    Phase190Member(
                        "_value",
                        "System.Collections.Generic.List<int>",
                        "/phase190/nulllist",
                        FoxRunTypeShape.Collection(FoxRunCollectionKind.List, FoxRunTypeShape.Canonical("int32")))
                },
                ["/phase190/unsigned"] = new List<FoxgloveSourceEmitter.TopicMember>
                {
                    Phase190Member("_value", "System.UInt32", "/phase190/unsigned", FoxRunTypeShape.Canonical("uint32"))
                }
            };
            var generated = new StringBuilder();
            MessagePackPublishDispatchEmitter.EmitFieldsAndBuilders(generated, topics, topicMap, "    ");
            var declaration = @"
using System.Collections.Generic;

namespace Phase190
{
    public sealed class MessagePackBinding
    {
        private double __foxRunCapture_0_0;
        private int __foxRunCapture_0_1;
        private List<int> __foxRunCapture_1_0;
        private uint __foxRunCapture_2_0;
"
                + generated
                + @"
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase190GeneratedMessagePack_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(declaration) },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(
                emit.Success,
                "Generated MessagePack fixture failed to compile: "
                + string.Join("; ", emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var type = assembly.GetType("Phase190.MessagePackBinding", throwOnError: true);
            var instance = Activator.CreateInstance(type);
            const BindingFlags fieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            type.GetField("__foxRunCapture_0_0", fieldFlags)!.SetValue(instance, 1.5d);
            type.GetField("__foxRunCapture_0_1", fieldFlags)!.SetValue(instance, 7);
            type.GetField("__foxRunCapture_1_0", fieldFlags)!.SetValue(instance, null);
            type.GetField("__foxRunCapture_2_0", fieldFlags)!.SetValue(instance, 3_000_000_000u);

            var expected = new[]
            {
                // {"alpha": 7, "zeta": 1.5}
                new byte[]
                {
                    0x82,
                    0xa5, 0x61, 0x6c, 0x70, 0x68, 0x61, 0x07,
                    0xa4, 0x7a, 0x65, 0x74, 0x61, 0xcb, 0x3f, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                },
                // {"value": nil}
                new byte[] { 0x81, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65, 0xc0 },
                // {"value": uint32 3000000000}
                new byte[] { 0x81, 0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65, 0xce, 0xb2, 0xd0, 0x5e, 0x00 }
            };
            for (var index = 0; index < topics.Length; index++)
            {
                var payload = Assert.IsType<byte[]>(
                    type.GetMethod(
                            "__BuildFoxRunMessagePack_" + index,
                            BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(instance, null));
                Assert.Equal(expected[index], payload);
            }
        }

        private static FoxgloveSourceEmitter.TopicMember Phase190Member(
            string memberName,
            string typeName,
            string topic,
            FoxRunTypeShape shape)
            => new FoxgloveSourceEmitter.TopicMember(
                memberName,
                typeName,
                topic,
                10f,
                typeName,
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape: shape);

        [Fact]
        public void RoslynAndReflectionLoweringEmitTheSameMessagePackSource()
        {
            var shape = FoxRunTypeShape.Object(
                "Demo.Payload",
                new[]
                {
                    new FoxRunTypeField(
                        "value",
                        "Value",
                        FoxRunTypeShape.Canonical("int32"))
                });
            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "ParitySource", "_payload", "field",
                    "Demo.Payload", "global::Demo.Payload",
                    false, false, "", "/phase185/parity", "Demo.Payload",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Publish,
                    encoding: (int)FoxRunEncoding.MessagePack,
                    typeShape: shape,
                    namedArgumentPresence: FoxRunNamedArgumentPresence.Encoding)
            });
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo", "ParitySource", "_payload", "field",
                    "Demo.Payload", "global::Demo.Payload",
                    false, false, "", "/phase185/parity", "Demo.Payload",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Publish,
                    encoding: (int)FoxRunEncoding.MessagePack,
                    typeShape: shape,
                    namedArgumentPresence: FoxRunNamedArgumentPresence.Encoding)
            });

            var roslynSource = FoxgloveSourceEmitter.EmitClass(roslyn.Types.Single());
            var reflectionSource = FoxgloveSourceEmitter.EmitClass(reflection.Types.Single());

            Assert.Equal(roslynSource, reflectionSource);
            Assert.Contains("msgpack", roslynSource, StringComparison.Ordinal);
        }

        private static FoxgloveSourceEmitter.TopicMember Member(
            string memberName,
            string typeName,
            FoxRunTypeShape shape)
            => new(
                memberName,
                typeName,
                "/phase185/messagepack",
                10f,
                "Demo.MessagePack",
                (int)FoxRunPolicy.FixedRate,
                0f,
                mode: (int)FoxRunFlow.Publish,
                encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape: shape);

        private static string Slice(string source, string start, string end)
        {
            var startIndex = source.IndexOf(start, StringComparison.Ordinal);
            Assert.True(startIndex >= 0, "Missing generated start marker: " + start);
            var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
            Assert.True(endIndex > startIndex, "Missing generated end marker: " + end);
            return source.Substring(startIndex, endIndex - startIndex);
        }

        private static void AssertInOrder(string source, params string[] fragments)
        {
            var previous = -1;
            foreach (var fragment in fragments)
            {
                var current = source.IndexOf(fragment, StringComparison.Ordinal);
                Assert.True(current > previous, "Expected generated fragment in order: " + fragment);
                previous = current;
            }
        }

        private static MetadataReference[] DynamicCompilationReferences()
        {
            var locations = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                             ?? string.Empty)
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Append(typeof(FoxRunEncoding).Assembly.Location)
                .Append(typeof(Google.Protobuf.IMessage).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return locations
                .Select(location => MetadataReference.CreateFromFile(location))
                .ToArray();
        }

        private static Assembly CompileGenerated(string declaration)
        {
            var compilation = CSharpCompilation.Create(
                "Phase185FGeneratedMessagePack_"
                + Guid.NewGuid().ToString("N"),
                GeneratedPublishSyntaxTrees(declaration),
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver =
                CSharpGeneratorDriver.Create(
                    new FoxgloveLogSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var output,
                out _);
            using var image = new MemoryStream();
            var emit = output.Emit(image);
            Assert.True(
                emit.Success,
                "Generated MessagePack publisher failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(
                        diagnostic => diagnostic.ToString())));
            image.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(image);
        }

        private static SyntaxTree[] GeneratedPublishSyntaxTrees(
            params string[] sources)
        {
            var root = FindRepoRoot();
            return sources
                .Concat(new[]
                {
                    File.ReadAllText(Path.Combine(
                        root,
                        "Packages",
                        "dev.unity2foxglove.sdk",
                        "Tests",
                        "AdapterCompileStubs",
                        "FoxgloveLogHubCompileStubs.cs")),
                    GeneratedPublishInterfaces
                })
                .Select(source => CSharpSyntaxTree.ParseText(source))
                .ToArray();
        }

        private const string GeneratedPublishInterfaces = @"
namespace Unity.FoxgloveSDK.Components
{
    public interface IFoxgloveLogSource
    {
        int FoxgloveLog_TopicCount { get; }
        FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index);
        void FoxgloveLog_Publish(
            int topicIndex,
            FoxgloveManager manager,
            ulong nowNs);
    }

    public interface IFoxgloveTopicContractSource
    {
        string FoxgloveLog_Origin { get; }
        FoxTopicContract FoxgloveLog_GetContract(int index);
    }

    public interface IFoxgloveTopicBusSource
    {
        void FoxgloveLog_PublishToBus(
            int topicIndex,
            FoxTopicBus bus,
            ulong nowNs);
    }

    public interface IFoxgloveTopicBusDemandSource
    {
        bool FoxgloveLog_HasBusSubscribers(
            int topicIndex,
            FoxTopicBus bus);
    }

    public interface IFoxgloveTopicObserverSource
    {
        bool FoxgloveLog_HasObservers(
            int topicIndex,
            FoxTopicBus bus);
        void FoxgloveLog_PublishCapturedToObservers(
            int topicIndex,
            FoxTopicBus bus,
            ulong nowNs);
    }

    public interface IFoxgloveTopicSinkSource
    {
        void FoxgloveLog_PublishToSinks(
            int topicIndex,
            FoxTopicSinkRouter router,
            ulong nowNs);
    }

    public interface IFoxglovePublishCaptureSource
    {
        bool FoxgloveLog_BeginCapture(int topicIndex);
        void FoxgloveLog_EndCapture(int topicIndex);
    }

    public interface IFoxglovePublishRecordingSource
    {
        bool FoxgloveLog_IsRecordingReady(
            int topicIndex,
            FoxgloveManager manager,
            out string reason);
        bool FoxgloveLog_RecordCaptured(
            int topicIndex,
            FoxgloveManager manager,
            ulong nowNs,
            out string reason);
    }

    public interface IFoxglovePublishRecordingPolicySource
    {
        bool FoxgloveLog_ShouldRecord(int topicIndex);
        void FoxgloveLog_MarkRecorded(int topicIndex);
    }

    public interface IFoxRunWebSocketCaptureSource
    {
        void FoxgloveLog_SetWebSocketEncoding(
            int topicIndex,
            FoxRunEncoding encoding);
    }

    public interface IFoxglovePublishOriginSource
    {
        bool FoxgloveLog_CanPublishOrigin(
            int topicIndex,
            bool explicitTrigger);
    }

    public interface IFoxgloveLogPolicySource
    {
        bool FoxgloveLog_ShouldPublish(
            int topicIndex,
            double nowSeconds);
        void FoxgloveLog_MarkPublished(
            int topicIndex,
            double nowSeconds);
    }
}";


    }
}
