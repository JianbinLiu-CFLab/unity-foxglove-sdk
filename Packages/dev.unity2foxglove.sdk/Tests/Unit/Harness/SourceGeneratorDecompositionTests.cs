// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 174-002 source generator decomposition checks.

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "174-002")]
    [Trait("Domain", "Harness")]
    public sealed class SourceGeneratorDecompositionTests
    {
        [Fact]
        public void SourceGeneratorCompileSurfacesIncludeSplitFiles()
        {
            var runtimeProject = TestSources.Runtime("FoxgloveSdk.Tests.csproj");
            var testSurface = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/FoxgloveSdk.TestSurface.props");

            Assert.Contains("Editor/SourceGenerators/src/**/*.cs", runtimeProject.Replace('\\', '/'), StringComparison.Ordinal);
            Assert.Contains("Editor/SourceGenerators/src/**/*.cs", testSurface.Replace('\\', '/'), StringComparison.Ordinal);
        }

        [Fact]
        public void SourceGeneratorCoreResponsibilitiesAreSplit()
        {
            var main = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var models = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.Models.cs");
            var diagnostics = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.Diagnostics.cs");
            var descriptor = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxRunDescriptorCarrierEmitter.cs");
            var dto = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxService/FoxServiceRoslynDtoValidator.cs");
            var schema = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxService/FoxServiceRoslynSchemaBuilder.cs");

            Assert.DoesNotContain("private sealed class ServiceMethodData", main, StringComparison.Ordinal);
            Assert.DoesNotContain("private sealed class MemberData", main, StringComparison.Ordinal);
            Assert.DoesNotContain("private static class Diags", main, StringComparison.Ordinal);
            Assert.DoesNotContain("private static string DescriptorCarrierSource", main, StringComparison.Ordinal);
            Assert.DoesNotContain("private static IEnumerable<ServiceDiagnostic> ValidateServiceDtoType", main, StringComparison.Ordinal);
            Assert.DoesNotContain("private static FoxServiceSchemaModel BuildServiceSchema", main, StringComparison.Ordinal);

            Assert.Contains("internal sealed class ServiceMethodData", models, StringComparison.Ordinal);
            Assert.Contains("public void AppendRoslynMembers", models, StringComparison.Ordinal);
            Assert.Contains("internal static class Diags", diagnostics, StringComparison.Ordinal);
            Assert.Contains("UnknownFoxRunDiagnostic", diagnostics, StringComparison.Ordinal);
            Assert.Contains("internal static class FoxRunDescriptorCarrierEmitter", descriptor, StringComparison.Ordinal);
            Assert.Contains("EscapeStringLiteral", descriptor, StringComparison.Ordinal);
            Assert.Contains("internal static class FoxServiceRoslynDtoValidator", dto, StringComparison.Ordinal);
            Assert.Contains("validatedTypes", dto, StringComparison.Ordinal);
            Assert.Contains("internal static class FoxServiceRoslynSchemaBuilder", schema, StringComparison.Ordinal);
            Assert.Contains("FoxServiceSchemaModel Build", schema, StringComparison.Ordinal);
        }
    }
}
