// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Phase185-D full-duplex MessagePack integration coverage.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunMessagePackDuplexTests
    {
        [Fact]
        [Trait("Phase", "185-D")]
        public void GeneratedMessagePackDuplexUsesExistingOriginSuppressionSurface()
        {
            var member = new FoxgloveSourceEmitter.TopicMember(
                "Value",
                "System.Int32",
                "/phase185/duplex",
                10f,
                string.Empty,
                policy: (int)FoxRunPolicy.Change,
                tolerance: 0f,
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                canonicalType: "int32",
                encoding:
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape: FoxRunTypeShape.Canonical("int32"),
                source:
                    FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource,
                namedArgumentPresence:
                    FoxRunNamedArgumentPresence.Source
                    | FoxRunNamedArgumentPresence.Encoding);

            var generated = FoxgloveSourceEmitter.EmitClass(
                "Demo",
                "MessagePackDuplex",
                new[] { member });

            Assert.Contains(
                "IFoxglovePublishOriginSource",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "__FoxRunMarkRemoteApplied_0();",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (explicitTrigger) return true;",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "case 0: return __FoxRunCanPublishOrigin_0();",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (!__foxRunRemoteOwned_0) return true;",
                generated,
                StringComparison.Ordinal);
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        [Trait("Phase", "185-D")]
        public void RemoteMessagePackApplyDoesNotMirrorButLaterLocalMutationPublishesOnce()
        {
            var compilation = CSharpCompilation.Create(
                "Phase185MessagePackDuplex_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class MessagePackDuplex
    {
        [FoxRun(""/phase185/duplex"", Mode = FoxRunFlow.PublishAndSubscribe,
            Encoding = FoxRunEncoding.MessagePack, Policy = FoxRunPolicy.Change,
            Hz = 10f)]
        public int Value;
    }
}")
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver =
                CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var output,
                out _);
            using var image = new MemoryStream();
            var emit = output.Emit(image);

            Assert.True(
                emit.Success,
                "Generated MessagePack duplex fixture failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType(
                "Demo.MessagePackDuplex",
                throwOnError: true);
            var receiver = Activator.CreateInstance(receiverType);
            var input = Assert.IsAssignableFrom<IFoxgloveInputSource>(receiver);
            var transactional =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    receiver);
            var policy = Assert.IsAssignableFrom<IFoxgloveLogPolicySource>(
                receiver);
            var origin = Assert.IsAssignableFrom<IFoxglovePublishOriginSource>(
                receiver);
            var value = receiverType.GetField("Value");
            Assert.NotNull(value);

            value.SetValue(receiver, 1);
            Assert.True(origin.FoxgloveLog_CanPublishOrigin(
                0,
                explicitTrigger: false));
            Assert.True(policy.FoxgloveLog_ShouldPublish(0, 0d));
            policy.FoxgloveLog_MarkPublished(0, 0d);

            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    Payload(4),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var error),
                error);
            Assert.Equal(1, input.FoxgloveInput_Flush(0.01d, 60));
            Assert.Equal(4, value.GetValue(receiver));

            Assert.False(origin.FoxgloveLog_CanPublishOrigin(
                0,
                explicitTrigger: false));
            Assert.False(origin.FoxgloveLog_CanPublishOrigin(
                0,
                explicitTrigger: false));

            value.SetValue(receiver, 5);
            Assert.True(origin.FoxgloveLog_CanPublishOrigin(
                0,
                explicitTrigger: false));
            Assert.True(policy.FoxgloveLog_ShouldPublish(0, 0.02d));
            policy.FoxgloveLog_MarkPublished(0, 0.02d);
            Assert.False(policy.FoxgloveLog_ShouldPublish(0, 0.03d));
        }
#endif

        private static byte[] Payload(int value)
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(1);
            writer.WriteString("Value");
            writer.WriteInt32(value);
            return writer.ToArray();
        }

        private static MetadataReference[] DynamicCompilationReferences()
        {
            var locations = ((string)AppContext.GetData(
                                 "TRUSTED_PLATFORM_ASSEMBLIES")
                             ?? string.Empty)
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Append(typeof(FoxRunEncoding).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return locations
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }
    }
}
