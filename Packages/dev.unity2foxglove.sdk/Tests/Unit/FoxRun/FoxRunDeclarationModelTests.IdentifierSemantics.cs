// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunDeclarationModelTests
    {
        [Fact]
        [Trait("Phase", "187-R2-E02-002")]
        public void RoslynGeneratorRejectsCollidingFoxServiceHostHintsBeforeAddSource()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Service.A.B
{
    public partial class C
    {
        [FoxService(""/phase187/e02/service-one"", Type = ""Service.Type"", RequestSchemaName = ""Service.Request"", ResponseSchemaName = ""Service.Response"")]
        private void InvokeOne() { }
    }
}

namespace Service.A
{
    public partial class B_C
    {
        [FoxService(""/phase187/e02/service-two"", Type = ""Service.Type"", RequestSchemaName = ""Service.Request"", ResponseSchemaName = ""Service.Response"")]
        private void InvokeTwo() { }
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXSERVICE010");
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "CS8785");
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void RoslynGeneratorAcceptsContextualKeywordHostIdentity()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace Telemetry.on
{
    public partial class record
    {
        [FoxRun(""/phase187/r4/c4/contextual"")]
        private int _value;
    }
}";

            var result = RunGenerator(source);
            AssertGeneratedSourcesParse(result, CSharpParseOptions.Default);
            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN623");
            Assert.Contains(
                result.Results
                    .SelectMany(run => run.GeneratedSources),
                generated => generated.HintName.Contains(
                    "Telemetry_on_record_FoxRun",
                    StringComparison.Ordinal));
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void RoslynGeneratorEscapesContextualNamespaceInGeneratedDtoTypeReferences()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace @record
{
    public sealed class Payload
    {
        public int Value;
    }

    public partial class Host
    {
        [FoxRun(""/phase187/r4/c4/contextual-dto"", Encoding = FoxRunEncoding.Protobuf)]
        private Payload _value;
    }
}");

            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN623");
            AssertGeneratedSourcesParse(result, CSharpParseOptions.Default);
            Assert.Contains(
                result.Results
                    .SelectMany(run => run.GeneratedSources)
                    .SelectMany(source => source.SourceText.ToString().Split('\n')),
                line => line.Contains(
                    "@record.Payload",
                    StringComparison.Ordinal));
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void RoslynGeneratorEscapesContextualTypeNamesAcrossLanguageVersions()
        {
            foreach (var parseOptions in new[]
                     {
                         new CSharpParseOptions(LanguageVersion.CSharp9),
                         new CSharpParseOptions(LanguageVersion.Preview)
                     })
            {
                foreach (var typeName in new[] { "file", "required", "scoped" })
                {
                    var result = RunGenerator(parseOptions, $@"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{{
    public partial class @{typeName}
    {{
        [FoxRun(""/phase187/r4/c4/contextual-{typeName}"")]
        private int _value;
    }}
}}");
                    AssertGeneratedSourcesParse(result, parseOptions);

                    Assert.DoesNotContain(
                        result.Diagnostics,
                        diagnostic => diagnostic.Id == "FOXRUN623");
                    Assert.Contains(
                        result.Results
                            .SelectMany(run => run.GeneratedSources),
                        generated => generated.SourceText.ToString().Contains(
                            "partial class @" + typeName,
                            StringComparison.Ordinal));
                }
            }
        }

        [Theory]
        [Trait("Phase", "187-R4-C4")]
        [InlineData("int", "int")]
        [InlineData("N.int", "N.@int")]
        [InlineData("N . int", "N . @int")]
        [InlineData("global::N.int", "global::N.@int")]
        [InlineData("System.Collections.Generic.List<int>", "System.Collections.Generic.List<int>")]
        [InlineData("System.Collections.Generic.List<N.int>", "System.Collections.Generic.List<N.@int>")]
        public void IdentifierUtilsEscapesReservedAliasesOnlyInsideQualifiedTypeNames(
            string input,
            string expected)
        {
            Assert.Equal(
                expected,
                IdentifierUtils.EscapeTypeName(input));
        }

        [Theory]
        [Trait("Phase", "187-R4-C4")]
        [InlineData("dynamic", "dynamic")]
        [InlineData("N.dynamic", "N.dynamic")]
        [InlineData("System.Collections.Generic.List<dynamic>", "System.Collections.Generic.List<dynamic>")]
        [InlineData("N.nint", "N.nint")]
        public void IdentifierUtilsPreservesContextualTypeAliases(
            string input,
            string expected)
        {
            Assert.Equal(
                expected,
                IdentifierUtils.EscapeTypeName(input));
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void ReflectionFormatterPreservesRuntimePrimitiveAliases()
        {
            Assert.Equal(
                "int",
                FoxRunEmissionTypeNameFormatter.FromReflectionType(typeof(int)));
            Assert.Equal(
                "string",
                FoxRunEmissionTypeNameFormatter.FromReflectionType(typeof(string)));
            Assert.Equal(
                "int[]",
                FoxRunEmissionTypeNameFormatter.FromReflectionType(typeof(int[])));
            Assert.Equal(
                "int?",
                FoxRunEmissionTypeNameFormatter.FromReflectionType(typeof(int?)));
            Assert.Equal(
                "System.Collections.Generic.List<int>",
                FoxRunEmissionTypeNameFormatter.FromReflectionType(
                    typeof(List<int>)));
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void RoslynGeneratorEscapesReservedNamespaceAndClassIdentifiers()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
namespace @namespace
{
    public partial class @event
    {
        [FoxRun(""/phase187/r4/c4/escaped-reserved"")]
        private int _value;
    }
}");
            AssertGeneratedSourcesParse(result, CSharpParseOptions.Default);

            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN623");
            var generated = result.Results
                .SelectMany(run => run.GeneratedSources)
                .Select(source => source.SourceText.ToString())
                .Single(source => source.Contains(
                    "partial class @event",
                    StringComparison.Ordinal));
            Assert.Contains(
                "namespace @namespace",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void RoslynGeneratorRejectsEveryUnsupportedHostTypeShapeWithReason()
        {
            var cases = new[]
            {
                ("record", @"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public partial record RecordHost
    {
        [FoxRun(""/phase187/r4/c4/record"")]
        private int _value;
    }
}"),
                ("generic", @"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public partial class GenericHost<T>
    {
        [FoxRun(""/phase187/r4/c4/generic"")]
        private int _value;
    }
}"),
                ("struct", @"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public partial struct StructHost
    {
        [FoxRun(""/phase187/r4/c4/struct"")]
        private int _value;
    }
}"),
                ("static", @"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public static partial class StaticHost
    {
        [FoxRun(""/phase187/r4/c4/static"")]
        private static int _value;
    }
}"),
                ("interface", @"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public partial interface InterfaceHost
    {
        [FoxRun(""/phase187/r4/c4/interface"")]
        int Value { get; set; }
    }
}")
            };

            foreach (var testCase in cases)
            {
                var result = RunGenerator(testCase.Item2);
                var diagnostic = Assert.Single(
                    result.Diagnostics.Where(
                        candidate => candidate.Id == "FOXRUN623"));
                Assert.Contains(
                    testCase.Item1,
                    diagnostic.GetMessage(),
                    StringComparison.OrdinalIgnoreCase);
            }

            // The pinned test Roslyn (4.2) parses C# 10 by default and cannot
            // construct a file-local type declaration. The production guard
            // intentionally recognizes the modifier token by ValueText so a
            // newer compiler rejects that shape; modern-parser coverage is
            // recorded separately in the C4 review evidence.
        }

    }
}
