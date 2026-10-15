// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.SourceGenerators;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunMessagePackInboundTests
    {
        [Fact]
        [Trait("Phase", "185-C")]
        public void GeneratedConsumerDecodesOneAtomicTopicTransaction()
        {
            var compilation = CSharpCompilation.Create(
                "Phase185MessagePackInbound_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class MessagePackReceiver
    {
        [FoxRun(""/phase185/atomic"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack, Hz = 60f)]
        public int Count;

        [FoxRun(""/phase185/atomic"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack, Hz = 60f)]
        public string Label;
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
                "Generated transactional consumer failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType(
                "Demo.MessagePackReceiver",
                throwOnError: true);
            var receiver = Activator.CreateInstance(receiverType);
            var input = Assert.IsAssignableFrom<IFoxgloveInputSource>(receiver);
            var transactional =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    receiver);
            Assert.Equal(1, transactional.FoxgloveInput_TransactionCount);
            Assert.Equal(
                FoxRunEncoding.MessagePack,
                transactional.FoxgloveInput_GetTransaction(0).DeclaredEncoding);

            var first = Payload(1, "first", reverseOrder: true);
            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    first,
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var firstError),
                firstError);

            var malformed = PayloadWithDuplicateCount();
            Assert.False(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    malformed,
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var malformedError));
            Assert.Contains(
                "duplicate",
                malformedError,
                StringComparison.OrdinalIgnoreCase);

            Assert.False(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    PayloadWithDuplicateUnknown(),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var unknownDuplicateError));
            Assert.Contains(
                "duplicate",
                unknownDuplicateError,
                StringComparison.OrdinalIgnoreCase);

            Assert.Equal(1, input.FoxgloveInput_Flush(1d, 60));
            Assert.Equal(1, receiverType.GetField("Count")!.GetValue(receiver));
            Assert.Equal(
                "first",
                receiverType.GetField("Label")!.GetValue(receiver));
            Assert.Equal(0, input.FoxgloveInput_Flush(2d, 60));

            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    Payload(2, "second", reverseOrder: false),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var secondError),
                secondError);
            transactional.FoxgloveInput_ClearTransaction(0);
            Assert.Equal(0, input.FoxgloveInput_Flush(3d, 60));
            Assert.Equal(1, receiverType.GetField("Count")!.GetValue(receiver));
        }

        [Fact]
        [Trait("Phase", "185-C")]
        public void RequiredAndNullableCanonicalMembersCompileWithDistinctReaders()
        {
            var compilation = CSharpCompilation.Create(
                "Phase185MessagePackNullableInbound_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class NullableReceiver
    {
        [FoxRun(""/phase185/nullable"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public int Required;

        [FoxRun(""/phase185/nullable"", Mode = Subscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public int? Optional;
    }
}")
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver =
                CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var output,
                out _);
            var generatedSource = driver.GetRunResult()
                .Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName.EndsWith(
                    "_FoxRun.g.cs",
                    StringComparison.Ordinal))
                .SourceText
                .ToString();
            using var image = new MemoryStream();
            var emit = output.Emit(image);

            Assert.True(
                emit.Success,
                "Generated nullable MessagePack consumer failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(diagnostic => diagnostic.ToString()))
                + Environment.NewLine
                + generatedSource);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void NullableUnityValueDecodesNilAsNull()
        {
            var compilation = CSharpCompilation.Create(
                "Phase185FNullableUnityInbound_"
                + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class NullableUnityReceiver
    {
        [FoxRun(""/phase185f/nullable-vector"", Mode = FoxRunFlow.Subscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public Vector3? Position;
    }
}")
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver =
                CSharpGeneratorDriver.Create(
                    new FoxgloveLogSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var output,
                out _);
            using var image = new MemoryStream();
            var emit = output.Emit(image);
            Assert.True(
                emit.Success,
                "Generated nullable Unity MessagePack consumer failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(
                        diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType(
                "Demo.NullableUnityReceiver",
                throwOnError: true);
            var receiver = Activator.CreateInstance(receiverType);
            var transactional =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    receiver);
            var input = Assert.IsAssignableFrom<IFoxgloveInputSource>(receiver);
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(1);
            writer.WriteString("Position");
            writer.WriteNil();

            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    writer.ToArray(),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(1024),
                    out var error),
                error);
            Assert.Equal(1, input.FoxgloveInput_Flush(1d, 60));
            Assert.Null(receiverType.GetField("Position")!.GetValue(receiver));
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void GeneratedEnumDecoderRejectsUndeclaredAndNarrowingValues()
        {
            var compilation = CSharpCompilation.Create(
                "Phase185FEnumDomainInbound_"
                + Guid.NewGuid().ToString("N"),
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
    public enum ByteMode : byte
    {
        Zero = 0,
        One = 1,
        AliasOfOne = 1
    }

    public enum UIntMode : uint
    {
        Zero = 0,
        One = 1
    }

    public partial class ByteEnumReceiver
    {
        [FoxRun(""/phase185f/byte-enum"", Mode = FoxRunFlow.Subscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public ByteMode Mode;
    }

    public partial class UIntEnumReceiver
    {
        [FoxRun(""/phase185f/uint-enum"", Mode = FoxRunFlow.Subscribe,
            Encoding = FoxRunEncoding.MessagePack)]
        public UIntMode Mode;
    }
}")
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver =
                CSharpGeneratorDriver.Create(
                    new FoxgloveLogSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var output,
                out _);
            using var image = new MemoryStream();
            var emit = output.Emit(image);
            Assert.True(
                emit.Success,
                "Generated enum MessagePack consumer failed to compile: "
                + string.Join(
                    "; ",
                    emit.Diagnostics.Select(
                        diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var byteType = assembly.GetType(
                "Demo.ByteEnumReceiver",
                throwOnError: true);
            var byteReceiver = Activator.CreateInstance(byteType);
            var byteTransactions =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    byteReceiver);

            Assert.False(
                byteTransactions.FoxgloveInput_TryStageTransaction(
                    0,
                    SingleIntPayload("Mode", 256),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(128),
                    out var narrowingError));
            Assert.Contains(
                "declared enum",
                narrowingError,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(
                byteTransactions.FoxgloveInput_TryStageTransaction(
                    0,
                    SingleIntPayload("Mode", 2),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(128),
                    out var undeclaredError));
            Assert.Contains(
                "declared enum",
                undeclaredError,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(
                byteTransactions.FoxgloveInput_TryStageTransaction(
                    0,
                    SingleIntPayload("Mode", 1),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(128),
                    out var aliasError),
                aliasError);
            Assert.Equal(
                1,
                Assert.IsAssignableFrom<IFoxgloveInputSource>(byteReceiver)
                    .FoxgloveInput_Flush(1d, 60));
            Assert.Equal(
                1,
                Convert.ToInt32(
                    byteType.GetField("Mode")!.GetValue(byteReceiver)));

            var uintType = assembly.GetType(
                "Demo.UIntEnumReceiver",
                throwOnError: true);
            var uintReceiver = Activator.CreateInstance(uintType);
            var uintTransactions =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    uintReceiver);
            Assert.False(
                uintTransactions.FoxgloveInput_TryStageTransaction(
                    0,
                    SingleIntPayload("Mode", -1),
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(128),
                    out var unsignedError));
            Assert.Contains(
                "declared enum",
                unsignedError,
                StringComparison.OrdinalIgnoreCase);
        }

    }
}
