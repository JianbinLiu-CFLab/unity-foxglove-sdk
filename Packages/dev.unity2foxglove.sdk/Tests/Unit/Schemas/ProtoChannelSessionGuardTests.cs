// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Schemas
// Purpose: Phase150 protobuf channel wrapper behavior tests.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FoxgloveSdk.UnitTests.Schemas
{
    public sealed class ProtoChannelSessionGuardTests
    {
        [Fact]
        public void ProtoChannelPublishesByCapturedChannelId()
        {
            var assembly = CompileProtoChannelHarness();
            var manager = CreateManager(assembly);
            var channel = CreateProtoChannel(assembly, manager, "/phase150/proto");

            InvokeLog(channel, new Foxglove.KeyValuePair { Key = "phase", Value = "150" }, 1234UL);

            Assert.Equal(1U, Get<uint>(channel, "ChannelId"));
            Assert.Equal(1U, Get<uint>(manager, "LastPublishedChannelId"));
            Assert.Equal("/phase150/proto", Get<string>(manager, "LastPublishedTopic"));
            Assert.Equal(1234UL, Get<ulong>(manager, "LastPublishedTimestampNs"));
            var payload = Get<byte[]>(manager, "LastPublishedPayload");
            Assert.NotNull(payload);
            Assert.NotEmpty(payload);
        }

        [Fact]
        public void ProtoChannelReusesCapturedChannelIdAcrossLogs()
        {
            var assembly = CompileProtoChannelHarness();
            var manager = CreateManager(assembly);
            var channel = CreateProtoChannel(assembly, manager, "/phase150/proto");

            InvokeLog(channel, new Foxglove.KeyValuePair { Key = "first", Value = "1" }, 100UL);
            InvokeLog(channel, new Foxglove.KeyValuePair { Key = "second", Value = "2" }, 200UL);

            Assert.Equal(1U, Get<uint>(channel, "ChannelId"));
            Assert.Equal(1U, Get<uint>(manager, "LastPublishedChannelId"));
            Assert.Equal(1, Get<int>(manager, "RegisterCallCount"));
            Assert.Equal(200UL, Get<ulong>(manager, "LastPublishedTimestampNs"));
        }

        [Fact]
        public void ProtoChannelRejectsNullMessage()
        {
            var assembly = CompileProtoChannelHarness();
            var manager = CreateManager(assembly);
            var channel = CreateProtoChannel(assembly, manager, "/phase150/proto");

            var ex = Assert.Throws<TargetInvocationException>(
                () => InvokeLog(channel, null, 42UL));

            Assert.IsType<ArgumentNullException>(ex.InnerException);
            Assert.Null(Get<byte[]>(manager, "LastPublishedPayload"));
        }

        [Fact]
        public void ProtoChannelRejectsUnknownClrType()
        {
            var assembly = CompileProtoChannelHarness();
            var manager = CreateManager(assembly);

            var ex = Assert.Throws<TargetInvocationException>(
                () => CreateProtoChannel(assembly, manager, "/phase150/unknown", typeof(Google.Protobuf.WellKnownTypes.StringValue)));

            var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("Unknown Foxglove protobuf message type", inner.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ProtoChannelRejectsLogAfterSessionGenerationChanges()
        {
            var assembly = CompileProtoChannelHarness();
            var manager = CreateManager(assembly);
            var channel = CreateProtoChannel(assembly, manager, "/phase150/proto");

            Invoke(manager, "AdvanceSessionGeneration");

            var ex = Assert.Throws<TargetInvocationException>(
                () => InvokeLog(channel, new Foxglove.KeyValuePair { Key = "phase", Value = "stale" }, 42UL));

            var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("old session", inner.Message, StringComparison.Ordinal);
            Assert.Null(Get<byte[]>(manager, "LastPublishedPayload"));
        }

        private static Assembly CompileProtoChannelHarness()
        {
            var wrapperSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Channels/FoxgloveProtoChannel.cs");
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);
            var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(wrapperSource, parseOptions),
                CSharpSyntaxTree.ParseText(FakeManagerSource, parseOptions),
                CSharpSyntaxTree.ParseText(FakeCatalogSource, parseOptions),
            };

            var compilation = CSharpCompilation.Create(
                "Phase150ProtoChannelHarness_" + Guid.NewGuid().ToString("N"),
                syntaxTrees,
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            if (!emit.Success)
            {
                var diagnostics = string.Join(
                    Environment.NewLine,
                    emit.Diagnostics
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(diagnostic => diagnostic.ToString()));
                throw new InvalidOperationException(diagnostics);
            }

            return Assembly.Load(stream.ToArray());
        }

        private static MetadataReference[] References()
        {
            var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));

            return trusted
                .Concat(new[]
                {
                    MetadataReference.CreateFromFile(typeof(Foxglove.KeyValuePair).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Google.Protobuf.IMessage).Assembly.Location),
                })
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private static object CreateManager(Assembly assembly)
        {
            var type = assembly.GetType("Unity.FoxgloveSDK.Components.FoxgloveManager", throwOnError: true);
            return Activator.CreateInstance(type);
        }

        private static object CreateProtoChannel(Assembly assembly, object manager, string topic)
            => CreateProtoChannel(assembly, manager, topic, typeof(Foxglove.KeyValuePair));

        private static object CreateProtoChannel(Assembly assembly, object manager, string topic, Type messageType)
        {
            var type = assembly.GetType("Unity.FoxgloveSDK.Components.FoxgloveProtoChannelExtensions", throwOnError: true);
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(candidate => candidate.Name == "CreateProtoChannel"
                                     && candidate.GetParameters().Length == 2
                                     && candidate.GetParameters()[1].ParameterType == typeof(string));

            return method.MakeGenericMethod(messageType).Invoke(null, new[] { manager, topic });
        }

        private static void InvokeLog(object channel, Foxglove.KeyValuePair message, ulong timestampNs)
        {
            var method = channel.GetType().GetMethod("Log", new[] { typeof(Foxglove.KeyValuePair), typeof(ulong) });
            Assert.NotNull(method);
            method.Invoke(channel, new object[] { message, timestampNs });
        }

        private static void Invoke(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);
            method.Invoke(instance, Array.Empty<object>());
        }

        private static T Get<T>(object instance, string propertyName)
        {
            var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            return (T)property.GetValue(instance);
        }

        private static string ReadRepoText(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository file " + relativePath + ".");
        }

        private const string FakeManagerSource = @"
using System;

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxgloveManager
    {
        private uint _nextChannelId = 1;

        internal ulong CurrentChannelSessionGeneration { get; private set; } = 1;

        public ulong NowNs => 42UL;

        public uint LastPublishedChannelId { get; private set; }

        public int RegisterCallCount { get; private set; }

        public string LastPublishedTopic { get; private set; }

        public ulong LastPublishedTimestampNs { get; private set; }

        public byte[] LastPublishedPayload { get; private set; }

        public uint GetOrRegisterSchemaChannel(string topic, string schemaName, string encoding)
        {
            if (encoding != ""protobuf"")
                throw new InvalidOperationException(""Expected protobuf encoding."");
            if (string.IsNullOrWhiteSpace(topic))
                throw new InvalidOperationException(""Topic required."");
            if (string.IsNullOrWhiteSpace(schemaName))
                throw new InvalidOperationException(""Schema required."");

            RegisterCallCount++;
            return _nextChannelId++;
        }

        internal void PublishProtoChannel(ulong generation, uint channelId, string topic, byte[] payload, ulong timestampNs)
        {
            if (generation != CurrentChannelSessionGeneration)
                throw new InvalidOperationException(""Foxglove channel belongs to an old session."");

            LastPublishedChannelId = channelId;
            LastPublishedTopic = topic;
            LastPublishedTimestampNs = timestampNs;
            LastPublishedPayload = payload;
        }

        public void AdvanceSessionGeneration()
        {
            CurrentChannelSessionGeneration++;
        }
    }
}";

        private const string FakeCatalogSource = @"
using System;

namespace Foxglove.Schemas
{
    public sealed class FoxgloveProtoSchemaCatalogEntry
    {
        public FoxgloveProtoSchemaCatalogEntry(string schemaName, Type clrType)
        {
            SchemaName = schemaName;
            ClrType = clrType;
        }

        public string SchemaName { get; }

        public Type ClrType { get; }
    }

    public static class FoxgloveProtoSchemaCatalog
    {
        private static readonly FoxgloveProtoSchemaCatalogEntry Entry =
            new FoxgloveProtoSchemaCatalogEntry(""foxglove.KeyValuePair"", typeof(Foxglove.KeyValuePair));

        public static bool TryGetByClrType(Type clrType, out FoxgloveProtoSchemaCatalogEntry entry)
        {
            entry = clrType == typeof(Foxglove.KeyValuePair) ? Entry : null;
            return entry != null;
        }

        public static bool TryGet(string schemaName, out FoxgloveProtoSchemaCatalogEntry entry)
        {
            entry = schemaName == ""foxglove.KeyValuePair"" ? Entry : null;
            return entry != null;
        }
    }
}";
    }
}
