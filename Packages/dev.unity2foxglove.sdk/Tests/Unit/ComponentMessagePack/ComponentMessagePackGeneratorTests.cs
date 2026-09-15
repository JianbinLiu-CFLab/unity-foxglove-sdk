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
