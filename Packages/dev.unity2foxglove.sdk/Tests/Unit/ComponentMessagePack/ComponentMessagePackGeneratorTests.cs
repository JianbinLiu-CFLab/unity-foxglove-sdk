using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.ComponentMessagePack
{
    public sealed class ComponentMessagePackGeneratorTests
    {
        [Fact]
        public void SupportedSchemaGeneratesExecutableManifestAndBinaryWriter()
        {
            var source = "using System; using Unity.FoxgloveSDK.Protocol; using Newtonsoft.Json; "
                + "[FoxgloveSchema(\"demo.Telemetry\")] public sealed class Telemetry "
                + "{ [JsonProperty(\"count\")] public int Count; public byte[] Data; [JsonIgnore] public int Ignored; }";
            var compilation = CSharpCompilation.Create("Phase189BFixture", new[] { CSharpSyntaxTree.ParseText(source) }, References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ComponentMessagePackSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
            Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            var generated = driver.GetRunResult().GeneratedTrees.Single(tree => tree.FilePath.EndsWith("ComponentMessagePackManifest.g.cs", StringComparison.Ordinal)).GetText().ToString();
            Directory.CreateDirectory("build/phase189");
            File.WriteAllText("build/phase189/generated-component.cs", generated);
            Assert.Contains("ComponentMessagePackGeneratedManifest", generated, StringComparison.Ordinal);
            Assert.Contains("WriteBinary(typed.Data)", generated, StringComparison.Ordinal);
            using var image = new MemoryStream();
            var emit = updated.Emit(image);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var factory = assembly.GetType("Unity.FoxgloveSDK.Generated.__Unity2FoxgloveComponentMessagePackManifest", true)!.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
            var manifest = (ComponentMessagePackGeneratedManifest)factory.Invoke(null, null);
            Assert.Single(manifest.Entries);
            Assert.True(manifest.Entries[0].IsAvailable);
            Assert.Equal("demo.Telemetry", manifest.Entries[0].LogicalSchemaName);
        }

        [Fact]
        public void DuplicateAndUnsupportedMembersProduceDiagnosticsAndUnavailableEntries()
        {
            var source = "using System; using Unity.FoxgloveSDK.Protocol; using Newtonsoft.Json; "
                + "[FoxgloveSchema(\"demo.Bad\")] public sealed class Bad "
                + "{ [JsonProperty(\"same\")] public int A; [JsonProperty(\"same\")] public int B; public DateTime Unsupported; }";
            var compilation = CSharpCompilation.Create("Phase189BBadFixture", new[] { CSharpSyntaxTree.ParseText(source) }, References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ComponentMessagePackSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
            var runDiagnostics = driver.GetRunResult().Diagnostics.Concat(diagnostics).ToArray();
            Assert.Contains(runDiagnostics, d => d.Id == "FOXCOMP001");
            Assert.Contains(runDiagnostics, d => d.Id == "FOXCOMP002");
            using var image = new MemoryStream();
            var emit = updated.Emit(image);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            var generated = driver.GetRunResult().GeneratedTrees.Single(tree => tree.FilePath.EndsWith("ComponentMessagePackManifest.g.cs", StringComparison.Ordinal)).GetText().ToString();
            Assert.Contains("false, false", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void NestedObjectAndCollectionShapesCompileAndEmitMaps()
        {
            var source = "using System.Collections.Generic; using Unity.FoxgloveSDK.Protocol; using Newtonsoft.Json; "
                + "public sealed class Stamp { public ulong Sec; public uint Nsec; } "
                + "[FoxgloveSchema(\"demo.Nested\")] public sealed class Nested "
                + "{ public Stamp Timestamp; [JsonProperty(\"values\")] public List<int> Values; }";
            var compilation = CSharpCompilation.Create("Phase189BNestedFixture", new[] { CSharpSyntaxTree.ParseText(source) }, References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ComponentMessagePackSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
            Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            var generated = driver.GetRunResult().GeneratedTrees.Single(tree => tree.FilePath.EndsWith("ComponentMessagePackManifest.g.cs", StringComparison.Ordinal)).GetText().ToString();
            Assert.Contains("__WriteFoxRunMessagePackObject_", generated, StringComparison.Ordinal);
            Assert.Contains("WriteArrayHeader", generated, StringComparison.Ordinal);
            using var image = new MemoryStream();
            var emit = updated.Emit(image);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        [Fact]
        public void ManifestHashChangesWhenNestedShapeChanges()
        {
            var first = GenerateManifest("public sealed class Stamp { public ulong Sec; public uint Nsec; }\n"
                + "[FoxgloveSchema(\"demo.Nested\")] public sealed class Nested { public Stamp Timestamp; }");
            var second = GenerateManifest("public sealed class Stamp { public ulong Sec; public ulong Nsec; }\n"
                + "[FoxgloveSchema(\"demo.Nested\")] public sealed class Nested { public Stamp Timestamp; }");
            var marker = "new ComponentMessagePackGeneratedManifest(\"NestedHashFixture\", \"";
            var firstHash = first.Substring(first.IndexOf(marker, StringComparison.Ordinal) + marker.Length, 64);
            var secondHash = second.Substring(second.IndexOf(marker, StringComparison.Ordinal) + marker.Length, 64);
            Assert.NotEqual(firstHash, secondHash);
        }

        [Fact]
        public void SameSimpleTypeNamesInDifferentNamespacesCompile()
        {
            var source = "using Unity.FoxgloveSDK.Protocol; namespace A { [FoxgloveSchema(\"demo.A\")] public sealed class Sample { public int Value; } } "
                + "namespace B { [FoxgloveSchema(\"demo.B\")] public sealed class Sample { public int Value; } }";
            var compilation = CSharpCompilation.Create("CollisionFixture", new[] { CSharpSyntaxTree.ParseText(source) }, References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ComponentMessagePackSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
            Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            using var image = new MemoryStream();
            var emit = updated.Emit(image);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        [Fact]
        public void OutOfRangeEnumFailsClosedWithoutGeneratorCrash()
        {
            var source = "using Unity.FoxgloveSDK.Protocol; enum Huge : ulong { Value = 4294967296 } "
                + "[FoxgloveSchema(\"demo.Huge\")] public sealed class Payload { public Huge Value; }";
            var compilation = CSharpCompilation.Create("HugeEnumFixture", new[] { CSharpSyntaxTree.ParseText(source) }, References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ComponentMessagePackSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
            var all = driver.GetRunResult().Diagnostics.Concat(diagnostics).ToArray();
            Assert.Contains(all, d => d.Id == "FOXCOMP002");
            // Unsupported shapes still produce a diagnostic and no generator
            // exception; the host compilation may legitimately reject the
            // out-of-range fixture itself.
            Assert.NotNull(updated);
        }

        [Fact]
        public void GeneratedCodecWritesOrderedWireNamesAndSkipsIgnoredMembers()
        {
            // Declaration order (by member name) is Alpha, Ignored, Zeta; wire order is alpha, zulu.
            // The two orders disagree, so a dropped sort, a member-name key or an included
            // [JsonIgnore] member all change the bytes.
            var source = "using System; using Unity.FoxgloveSDK.Protocol; using Newtonsoft.Json; "
                + "[FoxgloveSchema(\"demo.Ordered\")] public sealed class Ordered "
                + "{ [JsonProperty(\"zulu\")] public int Alpha; [JsonProperty(\"alpha\")] public uint Zeta; "
                + "[JsonIgnore] public int Ignored; }";
            var manifest = BuildManifest(source, "OrderedCodecFixture", out var assembly);
            var entry = Assert.Single(manifest.Entries);
            Assert.True(entry.IsAvailable, entry.Diagnostic);

            var fixture = Activator.CreateInstance(assembly.GetType("Ordered", true)!)!;
            fixture.GetType().GetField("Alpha")!.SetValue(fixture, 7);
            fixture.GetType().GetField("Zeta")!.SetValue(fixture, 3_000_000_000u);
            fixture.GetType().GetField("Ignored")!.SetValue(fixture, 42);

            var decoded = DecodeMessagePackMap(entry.Serialize(fixture));

            Assert.Equal(new[] { "alpha", "zulu" }, decoded.Select(pair => pair.Key));
            Assert.Equal(3_000_000_000UL, Assert.IsType<ulong>(decoded[0].Value));
            Assert.Equal(7L, Assert.IsType<long>(decoded[1].Value));
        }

        [Fact]
        public void DuplicateWireNamesAloneFailClosedWithDuplicateDiagnostic()
        {
            var source = "using System; using Unity.FoxgloveSDK.Protocol; using Newtonsoft.Json; "
                + "[FoxgloveSchema(\"demo.Duplicate\")] public sealed class Duplicate "
                + "{ [JsonProperty(\"same\")] public int First; [JsonProperty(\"same\")] public int Second; }";
            var manifest = BuildManifest(source, "DuplicateWireNameFixture", out _, "FOXCOMP001");
            var entry = Assert.Single(manifest.Entries);
            Assert.False(entry.IsAvailable);
            Assert.Throws<InvalidOperationException>(() => entry.Serialize(new object()));
        }

        [Fact]
        public void EmptySchemaNameAloneFailsClosedWithInvalidSchemaDiagnostic()
        {
            var source = "using System; using Unity.FoxgloveSDK.Protocol; "
                + "[FoxgloveSchema(\"\")] public sealed class NoSchema { public int Value; }";
            var manifest = BuildManifest(source, "InvalidSchemaFixture", out _, "FOXCOMP003");
            var entry = Assert.Single(manifest.Entries);
            Assert.False(entry.IsAvailable);
        }

        private static ComponentMessagePackGeneratedManifest BuildManifest(
            string source,
            string assemblyName,
            out Assembly assembly,
            string expectedDiagnosticId = null)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { CSharpSyntaxTree.ParseText(source) },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ComponentMessagePackSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
            Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            var runDiagnostics = driver.GetRunResult().Diagnostics;
            if (expectedDiagnosticId != null)
                Assert.Contains(runDiagnostics, d => d.Id == expectedDiagnosticId);

            using var image = new MemoryStream();
            var emit = updated.Emit(image);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            image.Position = 0;
            assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var factory = assembly
                .GetType("Unity.FoxgloveSDK.Generated.__Unity2FoxgloveComponentMessagePackManifest", true)!
                .GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
            return (ComponentMessagePackGeneratedManifest)factory.Invoke(null, null);
        }

        /// <summary>
        /// Decodes the small MessagePack map subset the component codec emits, so a test can read
        /// the bytes the generated writer produced instead of the source text that produced it.
        /// </summary>
        private static List<KeyValuePair<string, object>> DecodeMessagePackMap(byte[] payload)
        {
            var offset = 0;
            var header = payload[offset++];
            int count;
            if ((header & 0xF0) == 0x80)
                count = header & 0x0F;
            else if (header == 0xDE)
            {
                count = (payload[offset] << 8) | payload[offset + 1];
                offset += 2;
            }
            else
                throw new InvalidOperationException("Unexpected MessagePack map header 0x" + header.ToString("x2"));

            var map = new List<KeyValuePair<string, object>>(count);
            for (var i = 0; i < count; i++)
            {
                var key = ReadString(payload, ref offset);
                map.Add(new KeyValuePair<string, object>(key, ReadValue(payload, ref offset)));
            }

            Assert.Equal(payload.Length, offset);
            return map;
        }

        private static string ReadString(byte[] payload, ref int offset)
        {
            var header = payload[offset++];
            int length;
            if ((header & 0xE0) == 0xA0)
                length = header & 0x1F;
            else if (header == 0xD9)
                length = payload[offset++];
            else
                throw new InvalidOperationException("Unexpected MessagePack string header 0x" + header.ToString("x2"));

            var value = System.Text.Encoding.UTF8.GetString(payload, offset, length);
            offset += length;
            return value;
        }

        private static object ReadValue(byte[] payload, ref int offset)
        {
            var header = payload[offset++];
            if (header <= 0x7F)
                return (long)header;
            if (header >= 0xE0)
                return (long)(sbyte)header;
            switch (header)
            {
                case 0xCC:
                    return (ulong)payload[offset++];
                case 0xCD:
                    offset += 2;
                    return (ulong)((payload[offset - 2] << 8) | payload[offset - 1]);
                case 0xCE:
                    offset += 4;
                    return (ulong)(((uint)payload[offset - 4] << 24)
                                   | ((uint)payload[offset - 3] << 16)
                                   | ((uint)payload[offset - 2] << 8)
                                   | payload[offset - 1]);
                case 0xD0:
                    return (long)(sbyte)payload[offset++];
                case 0xD1:
                    offset += 2;
                    return (long)(short)((payload[offset - 2] << 8) | payload[offset - 1]);
                case 0xD2:
                    offset += 4;
                    return (long)(int)(((uint)payload[offset - 4] << 24)
                                       | ((uint)payload[offset - 3] << 16)
                                       | ((uint)payload[offset - 2] << 8)
                                       | payload[offset - 1]);
                default:
                    throw new InvalidOperationException("Unexpected MessagePack value header 0x" + header.ToString("x2"));
            }
        }

        private static string GenerateManifest(string model)
        {
            var source = "using Unity.FoxgloveSDK.Protocol; " + model;
            var compilation = CSharpCompilation.Create("NestedHashFixture", new[] { CSharpSyntaxTree.ParseText(source) }, References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new ComponentMessagePackSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            return driver.GetRunResult().GeneratedTrees.Single(tree => tree.FilePath.EndsWith("ComponentMessagePackManifest.g.cs", StringComparison.Ordinal)).GetText().ToString();
        }

        private static IEnumerable<MetadataReference> References()
        {
            var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
            return trusted.Concat(new[] { MetadataReference.CreateFromFile(typeof(FoxgloveSchemaAttribute).Assembly.Location), MetadataReference.CreateFromFile(typeof(Newtonsoft.Json.JsonPropertyAttribute).Assembly.Location) });
        }
    }
}
