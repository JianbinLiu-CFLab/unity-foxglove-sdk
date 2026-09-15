// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    [Trait("Phase", "189A")]
    [Trait("Domain", "TypedMessagePack")]
    public sealed class SharedTypedMessagePackCoreTests
    {
        [Fact]
        public void CompleteFoxRunOutputIsFrozenBeforeExtraction()
        {
            var root = FindRepoRoot();
            var fixture = Path.Combine(root, "Packages", "dev.unity2foxglove.sdk", "Tests", "Unit", "FoxRun", "Fixtures");
            var expectedSource = File.ReadAllText(Path.Combine(fixture, "Phase189A_PreExtraction_FoxRun.g.cs.txt"));
            var actualSource = File.ReadAllText(Path.Combine(root, "Unity2Foxglove", "Assets", "Scripts", "Generated", "TestLog_FoxRun.g.cs"));
            Assert.Equal(expectedSource, actualSource);

            using var payloads = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "Phase189A_PreExtraction_Payloads.json")));
            Assert.Equal(6, payloads.RootElement.EnumerateObject().Count());
            foreach (var property in payloads.RootElement.EnumerateObject())
            {
                var hex = property.Value.GetString();
                Assert.False(string.IsNullOrWhiteSpace(hex));
                Assert.True(hex.Length % 2 == 0);
                Assert.All(hex, character => Assert.True(Uri.IsHexDigit(character)));
            }

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "Phase189A_PreExtraction_Manifest.json")));
            Assert.Equal("e072d43f7846f16193b2830ac45c04d5b9ab843c", manifest.RootElement.GetProperty("base_sha").GetString());
            Assert.Equal("dd60ced22ff4027ef665085964eeca7e46d90ef76e320ce00e76461f8cda96d0", manifest.RootElement.GetProperty("source_sha256").GetString());
            Assert.False(string.IsNullOrWhiteSpace(manifest.RootElement.GetProperty("payload_sha256").GetString()));

            var caller = CSharpSyntaxTree.ParseText(
                "using System.Text; using Unity.FoxgloveSDK.Editor; "
                + "public static class SharedCoreCaller { public static string Build() { "
                + "var sb = new StringBuilder(); "
                + "TypedMessagePackWriterEmitter.EmitValue(sb, FoxRunTypeShape.Canonical(\"int32\"), \"value\", \"writer\", \"\"); "
                + "return sb.ToString(); } }");
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(FoxRunTypeShape).Assembly.Location) });
            var compilation = CSharpCompilation.Create(
                "Phase189A_SharedCoreCaller",
                new[] { caller },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var generated = assembly.GetType("SharedCoreCaller", throwOnError: true)!
                .GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null) as string;
            Assert.Contains("WriteInt32((int)value)", generated, StringComparison.Ordinal);
        }

        private static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md")) && Directory.Exists(Path.Combine(directory.FullName, "Packages", "dev.unity2foxglove.sdk")))
                    return directory.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate the Unity2Foxglove repository root.");
        }
    }
}
