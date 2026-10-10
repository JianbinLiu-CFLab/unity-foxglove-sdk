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
        [Trait("Phase", "187-R4-C4")]
        public void RoslynGeneratorDoesNotInspectOrdinaryAttributedMembersAsFoxRunHosts()
        {
            var result = RunGenerator(@"
using System;

namespace Phase187R4C4
{
    public class Outer
    {
        public class Inner
        {
            [Obsolete]
            public int Value;
        }
    }
}");

            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN623");
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void RoslynGeneratorPreservesHostIdentityReasonForFoxRunAndFoxService()
        {
            const string foxRunSource = @"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public partial class Outer
    {
        public partial class Inner
        {
            [FoxRun(""/phase187/r4/c4/reason-run"")]
            private int _value;
        }
    }
}";
            var foxRun = RunGenerator(foxRunSource);
            var foxRunDiagnostic = Assert.Single(
                foxRun.Diagnostics.Where(
                    diagnostic => diagnostic.Id == "FOXRUN623"));
            Assert.Contains(
                "nested",
                foxRunDiagnostic.GetMessage(),
                StringComparison.OrdinalIgnoreCase);

            var foxService = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public class Outer
    {
        public partial class Inner
        {
            [FoxService(""/phase187/r4/c4/reason-service"", Type = ""Phase187R4C4.Service"", RequestSchemaName = ""Phase187R4C4.Request"", ResponseSchemaName = ""Phase187R4C4.Response"")]
            private void Invoke() { }
        }
    }
}");
            var serviceDiagnostic = Assert.Single(
                foxService.Diagnostics.Where(
                    diagnostic => diagnostic.Id == "FOXSERVICE010"));
            Assert.Contains(
                "nested",
                serviceDiagnostic.GetMessage(),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void RoslynGeneratorRejectsUnsupportedFoxServiceHostTypeShapesWithReason()
        {
            var cases = new[]
            {
                ("record", @"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public partial record RecordServiceHost
    {
        [FoxService(""/phase187/r4/c4/service-record"", Type = ""Phase187R4C4.ServiceRecord"", RequestSchemaName = ""Phase187R4C4.RequestRecord"", ResponseSchemaName = ""Phase187R4C4.ResponseRecord"")]
        private void Invoke() { }
    }
}"),
                ("struct", @"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public partial struct StructServiceHost
    {
        [FoxService(""/phase187/r4/c4/service-struct"", Type = ""Phase187R4C4.ServiceStruct"", RequestSchemaName = ""Phase187R4C4.RequestStruct"", ResponseSchemaName = ""Phase187R4C4.ResponseStruct"")]
        private void Invoke() { }
    }
}"),
                ("interface", @"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public partial interface InterfaceServiceHost
    {
        [FoxService(""/phase187/r4/c4/service-interface"", Type = ""Phase187R4C4.ServiceInterface"", RequestSchemaName = ""Phase187R4C4.RequestInterface"", ResponseSchemaName = ""Phase187R4C4.ResponseInterface"")]
        void Invoke();
    }
}")
            };

            foreach (var testCase in cases)
            {
                var result = RunGenerator(testCase.Item2);
                var diagnostic = Assert.Single(
                    result.Diagnostics.Where(
                        candidate => candidate.Id == "FOXSERVICE010"));
                Assert.Contains(
                    testCase.Item1,
                    diagnostic.GetMessage(),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void RoslynGeneratorTreatsCaseOnlyFoxRunAndFoxServiceHintsAsCollisions()
        {
            var foxRun = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public partial class CaseHost
    {
        [FoxRun(""/phase187/r4/c4/case-run-one"")]
        private int _one;
    }

    public partial class casehost
    {
        [FoxRun(""/phase187/r4/c4/case-run-two"")]
        private int _two;
    }
}");
            var foxRunDiagnostics = foxRun.Diagnostics
                .Where(diagnostic => diagnostic.Id == "FOXRUN623")
                .ToArray();
            Assert.Equal(2, foxRunDiagnostics.Length);
            Assert.All(
                foxRunDiagnostics,
                diagnostic => Assert.Contains(
                    "CaseHost",
                    diagnostic.GetMessage(),
                    StringComparison.OrdinalIgnoreCase));

            var foxService = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
namespace Phase187R4C4
{
    public partial class CaseServiceHost
    {
        [FoxService(""/phase187/r4/c4/case-service-one"", Type = ""Phase187R4C4.ServiceOne"", RequestSchemaName = ""Phase187R4C4.RequestOne"", ResponseSchemaName = ""Phase187R4C4.ResponseOne"")]
        private void InvokeOne() { }
    }

    public partial class caseservicehost
    {
        [FoxService(""/phase187/r4/c4/case-service-two"", Type = ""Phase187R4C4.ServiceTwo"", RequestSchemaName = ""Phase187R4C4.RequestTwo"", ResponseSchemaName = ""Phase187R4C4.ResponseTwo"")]
        private void InvokeTwo() { }
    }
}");
            var foxServiceDiagnostics = foxService.Diagnostics
                .Where(diagnostic => diagnostic.Id == "FOXSERVICE010")
                .ToArray();
            Assert.Equal(2, foxServiceDiagnostics.Length);
            Assert.All(
                foxServiceDiagnostics,
                diagnostic => Assert.Contains(
                    "generated service source hint",
                    diagnostic.GetMessage(),
                    StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                foxService.Diagnostics,
                diagnostic => diagnostic.Id == "CS8785");
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void RoslynGeneratorReportsBothOwnersAndHintInCollisionDiagnostics()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace A.B
{
    public partial class C
    {
        [FoxRun(""/phase187/r4/c4/collision-one"")]
        private int _value;
    }
}

namespace A
{
    public partial class B_C
    {
        [FoxRun(""/phase187/r4/c4/collision-two"")]
        private int _value;
    }
}");

            var diagnostics = result.Diagnostics
                .Where(diagnostic => diagnostic.Id == "FOXRUN623")
                .ToArray();
            Assert.Equal(2, diagnostics.Length);
            Assert.All(
                diagnostics,
                diagnostic => Assert.Contains(
                    "generated source hint",
                    diagnostic.GetMessage(),
                    StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        [Trait("Phase", "187-R4-C4")]
        public void ReflectionPreservesUserTypesNamedLikeCSharpAliases()
        {
            foreach (var alias in new[] { "int", "dynamic", "nint", "nuint", "string" })
            {
                var type = DefineDynamicGlobalType(alias);

                Assert.False(type.IsPrimitive);
                Assert.Equal(
                    "@" + alias,
                    FoxRunEmissionTypeNameFormatter.FromReflectionType(type));

                var shape = FoxRunReflectionTypeShapeBuilder.Build(type);
                Assert.Equal(FoxRunTypeShapeKind.Object, shape.Kind);
                Assert.Equal("@" + alias, shape.TypeName);
            }
        }

        private static Type DefineDynamicGlobalType(string name)
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("Phase187R4C4Alias" + Guid.NewGuid().ToString("N")),
                AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule("Main");
            return module.DefineType(
                    name,
                    TypeAttributes.Public | TypeAttributes.Class)
                .CreateType();
        }

    }
}
