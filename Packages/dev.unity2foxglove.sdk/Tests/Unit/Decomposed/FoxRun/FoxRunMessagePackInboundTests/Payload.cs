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
        private static byte[] Payload(
            int count,
            string label,
            bool reverseOrder)
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(2);
            if (reverseOrder)
            {
                writer.WriteString("Label");
                writer.WriteString(label);
                writer.WriteString("Count");
                writer.WriteInt32(count);
            }
            else
            {
                writer.WriteString("Count");
                writer.WriteInt32(count);
                writer.WriteString("Label");
                writer.WriteString(label);
            }
            return writer.ToArray();
        }

        private static byte[] PayloadWithDuplicateCount()
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(3);
            writer.WriteString("Count");
            writer.WriteInt32(1);
            writer.WriteString("Label");
            writer.WriteString("bad");
            writer.WriteString("Count");
            writer.WriteInt32(2);
            return writer.ToArray();
        }

        private static byte[] PayloadWithDuplicateUnknown()
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(4);
            writer.WriteString("Count");
            writer.WriteInt32(2);
            writer.WriteString("Label");
            writer.WriteString("duplicate-unknown");
            writer.WriteString("Future");
            writer.WriteInt32(1);
            writer.WriteString("Future");
            writer.WriteInt32(2);
            return writer.ToArray();
        }

        private static byte[] NestedEnvelopePayload(
            bool duplicateUnknownNestedKey)
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(1);
            writer.WriteString("Value");
            writer.WriteMapHeader(6);
            writer.WriteString("Samples");
            writer.WriteArrayHeader(2);
            writer.WriteInt32(3);
            writer.WriteInt32(4);
            writer.WriteString("Position");
            writer.WriteMapHeader(3);
            writer.WriteString("z");
            writer.WriteFloat(3f);
            writer.WriteString("x");
            writer.WriteFloat(1f);
            writer.WriteString("y");
            writer.WriteFloat(2f);
            writer.WriteString("Payload");
            writer.WriteBinary(new byte[] { 0xaa, 0xbb });
            writer.WriteString("Optional");
            writer.WriteNil();
            writer.WriteString("Nested");
            writer.WriteMapHeader(
                duplicateUnknownNestedKey ? 3 : 1);
            writer.WriteString("Value");
            writer.WriteInt32(7);
            if (duplicateUnknownNestedKey)
            {
                writer.WriteString("Future");
                writer.WriteInt32(1);
                writer.WriteString("Future");
                writer.WriteInt32(2);
            }
            writer.WriteString("Mode");
            writer.WriteInt32(2);
            return writer.ToArray();
        }

        private static byte[] SingleIntPayload(string field, int value)
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(1);
            writer.WriteString(field);
            writer.WriteInt32(value);
            return writer.ToArray();
        }

        private static void AppendNestedTypes(
            StringBuilder source,
            string prefix,
            int chainLinks)
        {
            for (var depth = 0; depth < chainLinks; depth++)
            {
                source.Append("    public sealed class ")
                    .Append(prefix).Append("Node").Append(depth)
                    .Append(" { public ").Append(prefix).Append("Node")
                    .Append(depth + 1).AppendLine(" Next; }");
            }
            source.Append("    public sealed class ")
                .Append(prefix).Append("Node").Append(chainLinks)
                .AppendLine(" { public UnityEngine.Vector3 Leaf; }");
        }

        private static byte[] NestedObjectPayload(int chainLinks)
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(1);
            writer.WriteString("Value");
            for (var depth = 0; depth < chainLinks; depth++)
            {
                writer.WriteMapHeader(1);
                writer.WriteString("Next");
            }
            writer.WriteMapHeader(1);
            writer.WriteString("Leaf");
            WriteVector3(writer);
            return writer.ToArray();
        }

        private static byte[] DepthThirtyFiveUnknownFieldPayload()
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(2);
            writer.WriteString("Unknown");
            for (var depth = 0; depth < 34; depth++)
                writer.WriteArrayHeader(1);
            writer.WriteNil();
            writer.WriteString("Value");
            for (var depth = 0; depth < 31; depth++)
            {
                writer.WriteMapHeader(1);
                writer.WriteString("Next");
            }
            writer.WriteMapHeader(1);
            writer.WriteString("Leaf");
            WriteVector3(writer);
            return writer.ToArray();
        }

        private static void WriteVector3(FoxgloveMsgPackWriter writer)
        {
            writer.WriteMapHeader(3);
            writer.WriteString("x");
            writer.WriteFloat(1f);
            writer.WriteString("y");
            writer.WriteFloat(2f);
            writer.WriteString("z");
            writer.WriteFloat(3f);
        }

        private static void AssertGeneratedDepthAccepted(
            Assembly assembly,
            string receiverTypeName,
            byte[] payload)
        {
            var receiver = Activator.CreateInstance(
                assembly.GetType(receiverTypeName, throwOnError: true));
            var transactional =
                Assert.IsAssignableFrom<IFoxgloveTransactionalInputSource>(
                    receiver);
            Assert.True(
                transactional.FoxgloveInput_TryStageTransaction(
                    0,
                    payload,
                    FoxgloveMsgPackReadLimits.ForPayloadBytes(4096),
                    out var error),
                error);
        }

        private static GeneratorDriver RunGenerator(
            string source,
            out Compilation output,
            out Diagnostic[] generatorDiagnostics)
        {
            var compilation = CSharpCompilation.Create(
                "Phase185FGeneratedInbound_"
                + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source) },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver =
                CSharpGeneratorDriver.Create(
                    new FoxgloveLogSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out output,
                out var diagnostics);
            generatorDiagnostics = diagnostics.ToArray();
            return driver;
        }

        private static string GeneratedText(GeneratorDriver driver)
            => string.Join(
                Environment.NewLine,
                driver.GetRunResult()
                    .Results
                    .SelectMany(result => result.GeneratedSources)
                    .Select(source => source.SourceText.ToString()));

        private static string PrimitiveReceiverSource(
            string typeName,
            string className,
            string encodingClause,
            string flow)
            => @"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class " + className + @"
    {
        [FoxRun(""/phase185f/primitive-" + typeName + @""",
            Mode = FoxRunFlow." + flow + encodingClause + @")]
        public " + typeName + @" Value;
    }
}";

        private static MetadataReference[] DynamicCompilationReferences()
        {
            var locations = ((string)AppContext.GetData(
                                 "TRUSTED_PLATFORM_ASSEMBLIES")
                             ?? string.Empty)
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Append(typeof(FoxRunEncoding).Assembly.Location)
                .Append(
                    typeof(Newtonsoft.Json.JsonPropertyAttribute)
                        .Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return locations
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }
    }
}
