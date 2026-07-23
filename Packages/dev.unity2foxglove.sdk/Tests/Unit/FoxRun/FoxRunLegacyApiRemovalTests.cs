// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Negative compilation evidence that the pre-Phase183 declaration API is absent.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunLegacyApiRemovalTests
    {
        public static IEnumerable<object[]> RemovedSpellings()
        {
            yield return Case("FoxRunMode", "Mode = FoxRunMode.SubscribeOnly");
            yield return Case("FoxRunPublishMode", "Policy = FoxRunPublishMode.OnChange");
            yield return Case("PublishOnly", "Mode = PublishOnly",
                "using static Unity.FoxgloveSDK.Components.FoxRunFlow;");
            yield return Case("SubscribeOnly", "Mode = SubscribeOnly",
                "using static Unity.FoxgloveSDK.Components.FoxRunFlow;");
            yield return Case("PublishMode", "PublishMode = FoxRunPolicy.Change");
            yield return Case("OnChange", "Policy = OnChange",
                "using static Unity.FoxgloveSDK.Components.FoxRunPolicy;");
            yield return Case("OnTrigger", "Policy = OnTrigger",
                "using static Unity.FoxgloveSDK.Components.FoxRunPolicy;");
        }

        [Theory]
        [MemberData(nameof(RemovedSpellings))]
        public void RemovedDeclarationSpellingFailsAsOrdinaryCSharp(
            string spelling,
            string declaration)
        {
            var compilation = CSharpCompilation.Create(
                "Phase183LegacyRemoval_" + spelling,
                new[] { CSharpSyntaxTree.ParseText(declaration) },
                CompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.NotEmpty(errors);
            Assert.All(errors, diagnostic => Assert.StartsWith("CS", diagnostic.Id));
            Assert.Contains(errors, diagnostic =>
                diagnostic.GetMessage().Contains(spelling, StringComparison.Ordinal));
        }

        private static object[] Case(string spelling, string attributeArguments, string extraUsing = "")
            => new object[]
            {
                spelling,
                @"using Unity.FoxgloveSDK.Components;
" + extraUsing + @"
namespace Demo
{
    public sealed class RemovedDeclaration
    {
        [FoxRun(""/phase183/removed"", " + attributeArguments + @")]
        private float _value;
    }
}"
            };

        private static IEnumerable<MetadataReference> CompilationReferences()
        {
            var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                ?.Split(Path.PathSeparator)
                ?? Array.Empty<string>();
            return trusted
                .Append(typeof(FoxRunAttribute).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path));
        }
    }
}
