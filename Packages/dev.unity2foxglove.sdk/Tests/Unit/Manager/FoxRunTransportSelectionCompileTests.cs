// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Manager
// Purpose: Compile the real capture-failure selection expression without implicit imports.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Manager
{
    [Trait("Phase", "187")]
    public sealed class FoxRunTransportSelectionCompileTests
    {
        [Fact]
        public void CaptureFailureSelectionCompilesWithItsOwnImportsAndPreservesSelection()
        {
            var source = CSharpSyntaxTree.ParseText(TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs"))
                .GetCompilationUnitRoot();
            var method = source.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "BeginFoxRunTransportSessionIfNeeded");
            var statement = method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
                .Single(node => node.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "configuredText"));
            // Compile the production statement verbatim with only its file's imports.
            // Unity namespaces are empty because this expression uses BCL types only.
            var probe = string.Concat(source.Usings.Select(node => node.ToFullString())) + @"
namespace Unity.FoxgloveSDK.IO {}
namespace UnityEngine {}
public static class CaptureSelectionProbe
{
    public static string Read(bool _enableFoxRunInbound, string _foxRunSubscribeTransportId, string[] _foxRunPublishTransportIds)
    {
" + statement.ToFullString() + @"
        return configuredText;
    }
}";
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "CaptureSelectionProbe_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(probe, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9)) },
                references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine,
                emit.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
            var read = Assembly.Load(image.ToArray()).GetType("CaptureSelectionProbe").GetMethod("Read");
            Assert.Equal("subscribe", Read(read, true, "subscribe", new[] { "publish", "second" }));
            Assert.Equal("publish", Read(read, false, "subscribe", new[] { "publish", "second" }));
            Assert.Null(Read(read, false, "subscribe", Array.Empty<string>()));
            Assert.Null(Read(read, false, "subscribe", null));
            Assert.Null(Read(read, true, null, new[] { "publish" }));
        }

        private static string Read(MethodInfo method, bool inbound, string subscribe, string[] publish)
            => (string)method.Invoke(null, new object[] { inbound, subscribe, publish });
    }
}
