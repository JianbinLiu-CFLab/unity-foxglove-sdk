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

        private static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(
                        directory.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Unity2Foxglove repository root.");
        }

        private static int FindTopicIndex(object source, string topic)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveLogSource");
            var count = Assert.IsType<int>(
                interfaceType.GetProperty("FoxgloveLog_TopicCount")!
                    .GetValue(source));
            var getTopic = interfaceType.GetMethod("FoxgloveLog_GetTopic")
                           ?? throw new InvalidOperationException(
                               "Generated log source is missing FoxgloveLog_GetTopic.");
            for (var index = 0;
                 index < count;
                 index++)
            {
                var info = getTopic.Invoke(source, new object[] { index })
                           ?? throw new InvalidOperationException(
                               "Generated topic metadata is null.");
                var infoType = info.GetType();
                var topicValue =
                    infoType.GetProperty("Topic")?.GetValue(info)
                    ?? infoType.GetField("Topic")?.GetValue(info);
                var actualTopic = Assert.IsType<string>(topicValue);
                if (string.Equals(
                        actualTopic,
                        topic,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
            throw new InvalidOperationException(
                "Generated topic is missing: " + topic);
        }

        private static bool BeginCapture(object source, int topicIndex)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxglovePublishCaptureSource");
            var method = interfaceType.GetMethod("FoxgloveLog_BeginCapture")
                         ?? throw new InvalidOperationException(
                             "Generated capture source is missing FoxgloveLog_BeginCapture.");
            return Assert.IsType<bool>(
                method.Invoke(source, new object[] { topicIndex }));
        }

        private static void EndCapture(object source, int topicIndex)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxglovePublishCaptureSource");
            var method = interfaceType.GetMethod("FoxgloveLog_EndCapture")
                         ?? throw new InvalidOperationException(
                             "Generated capture source is missing FoxgloveLog_EndCapture.");
            method.Invoke(source, new object[] { topicIndex });
        }

        private static FoxTopicContract GetContract(
            object source,
            int topicIndex)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicContractSource");
            var method = interfaceType.GetMethod("FoxgloveLog_GetContract")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing FoxgloveLog_GetContract.");
            return Assert.IsType<FoxTopicContract>(
                method.Invoke(source, new object[] { topicIndex }));
        }

        private static bool HasObservers(
            object source,
            int topicIndex,
            FoxTopicBus bus)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicObserverSource");
            var method = interfaceType.GetMethod("FoxgloveLog_HasObservers")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing FoxgloveLog_HasObservers.");
            return Assert.IsType<bool>(
                method.Invoke(
                    source,
                    new object[] { topicIndex, bus }));
        }

        private static void PublishCapturedToObservers(
            object source,
            int topicIndex,
            FoxTopicBus bus,
            ulong timestampNs)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicObserverSource");
            var method = interfaceType.GetMethod(
                             "FoxgloveLog_PublishCapturedToObservers")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing observer publication.");
            method.Invoke(
                source,
                new object[] { topicIndex, bus, timestampNs });
        }

        private static void PublishToBus(
            object source,
            int topicIndex,
            FoxTopicBus bus,
            ulong timestampNs)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicBusSource");
            var method = interfaceType.GetMethod("FoxgloveLog_PublishToBus")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing bus publication.");
            method.Invoke(
                source,
                new object[] { topicIndex, bus, timestampNs });
        }

        private static void PublishToSinks(
            object source,
            int topicIndex,
            FoxTopicSinkRouter router,
            ulong timestampNs)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicSinkSource");
            var method = interfaceType.GetMethod("FoxgloveLog_PublishToSinks")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing sink publication.");
            method.Invoke(
                source,
                new object[] { topicIndex, router, timestampNs });
        }

        private static void SetCaptureEncoding(
            Type type,
            object instance,
            int topicIndex,
            FoxRunEncoding encoding)
        {
            var field = type.GetField(
                            "__foxRunCaptureEncoding_" + topicIndex,
                            BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException(
                            "Generated capture encoding is missing.");
            field.SetValue(instance, encoding);
        }

        private static Type FindGeneratedInterface(object source, string name)
            => source.GetType()
                   .GetInterfaces()
                   .SingleOrDefault(candidate => candidate.Name == name)
               ?? throw new InvalidOperationException(
                   "Generated source is missing " + name + ".");

        private static FoxgloveMsgPackReader CapturedMessagePack(
            Type type,
            object instance,
            int topicIndex)
        {
            var payload = Assert.IsType<byte[]>(
                type.GetField(
                        "__foxRunLastMessagePack_" + topicIndex,
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(instance));
            return new FoxgloveMsgPackReader(
                payload,
                FoxgloveMsgPackReadLimits.ForPayloadBytes(4096));
        }

        private static FieldInfo CaptureField(Type type, int topicIndex)
            => type.GetField(
                   "__foxRunCapture_" + topicIndex + "_0",
                   BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException(
                   "Generated capture field is missing for topic " + topicIndex + ".");

    }
}
