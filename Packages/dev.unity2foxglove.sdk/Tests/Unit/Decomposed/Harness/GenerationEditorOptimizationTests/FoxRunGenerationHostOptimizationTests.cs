// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-68/69/70/71/72/73 generation and editor optimization checks.

using System;
using System.IO;
using System.Text.Json;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{

    [Trait("Phase", "140-69")]
    [Trait("Domain", "Harness")]
    public sealed class FoxRunGenerationHostOptimizationTests
    {
        [Fact]
        public void RoslynGeneratorHotPathsAvoidPerCandidateLinq()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var models = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.Models.cs");
            var candidate = TestSources.Slice(source, "private static bool IsFoxRunCandidate", "private static bool IsServiceCandidate");
            var extractMember = TestSources.Slice(source, "private static MemberData ExtractMember", "private static TopicEntry ReadTopic");
            var generate = TestSources.Slice(source, "private static void Generate", "private static string DiagnosticDeclaringType");
            var toRoslynMembers = TestSources.Slice(models, "ToRoslynMembers()", "    internal sealed class TopicEntry");

            Assert.Contains("AttributeLists.Count > 0", candidate, StringComparison.Ordinal);
            Assert.Contains("attribute.AttributeClass?.ToDisplayString()", extractMember, StringComparison.Ordinal);
            Assert.Contains("AttrFullName", extractMember, StringComparison.Ordinal);
            Assert.Contains("FieldAttrFullName", extractMember, StringComparison.Ordinal);
            Assert.DoesNotContain(".Where(", candidate, StringComparison.Ordinal);
            Assert.DoesNotContain(".ToList()", candidate, StringComparison.Ordinal);
            Assert.DoesNotContain(".Where(a => a.AttributeClass?.ToDisplayString() == AttrFullName)", extractMember, StringComparison.Ordinal);
            Assert.DoesNotContain(".ToList()", extractMember, StringComparison.Ordinal);
            Assert.Contains("AppendRoslynMembers", generate, StringComparison.Ordinal);
            Assert.DoesNotContain("items.Where", generate, StringComparison.Ordinal);
            Assert.DoesNotContain("SelectMany(m => m.ToRoslynMembers()).ToList()", generate, StringComparison.Ordinal);
            Assert.DoesNotContain(".GroupBy(m => (m.Ns, m.ClassName))", generate, StringComparison.Ordinal);
            Assert.Contains("AppendRoslynMembers", toRoslynMembers, StringComparison.Ordinal);
            Assert.DoesNotContain("Topics.Select", toRoslynMembers, StringComparison.Ordinal);
            Assert.DoesNotContain(".ToList()", toRoslynMembers, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorReviewFixesStayPinned()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var descriptor = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxRunDescriptorCarrierEmitter.cs");
            var generate = TestSources.Slice(source, "private static void Generate", "private static string DiagnosticDeclaringType");
            var services = TestSources.Slice(source, "private static void GenerateServices", "private static string DeclaringTypeName");
            var locationFor = TestSources.ExtractMethod(source, "private static Location LocationFor");
            var chunkedDescriptor = TestSources.ExtractMethod(descriptor, "public static string ChunkedDescriptorCarrierSource");
            var escape = TestSources.ExtractMethod(descriptor, "public static string EscapeStringLiteral");

            var readFloat = TestSources.ExtractMethod(source, "private static bool TryReadFloatConstant");
            var readInt = TestSources.ExtractMethod(source, "private static bool TryReadIntConstant");
            Assert.Contains("exception is OverflowException", readFloat, StringComparison.Ordinal);
            Assert.Contains("InvalidCastException", readFloat, StringComparison.Ordinal);
            Assert.Contains("FormatException", readFloat, StringComparison.Ordinal);
            Assert.Contains("exception is OverflowException", readInt, StringComparison.Ordinal);
            Assert.Contains("var members =", generate, StringComparison.Ordinal);
            Assert.Contains("item.AppendRoslynMembers(members)", generate, StringComparison.Ordinal);
            Assert.Contains("var servicesByName = new Dictionary<string, List<ServiceMethodData>>(StringComparer.Ordinal);", services, StringComparison.Ordinal);
            Assert.DoesNotContain(".GroupBy(item => item.ServiceName", services, StringComparison.Ordinal);
            Assert.Contains("DiagnosticDeclaringType(diagnostic)", locationFor, StringComparison.Ordinal);
            Assert.Contains("location ?? Location.None", locationFor, StringComparison.Ordinal);
            Assert.Contains("public static readonly string DescriptorJson = string.Concat(", chunkedDescriptor, StringComparison.Ordinal);
            Assert.DoesNotContain("public static string DescriptorJson => string.Concat(", chunkedDescriptor, StringComparison.Ordinal);
            Assert.Contains("char.IsHighSurrogate(ch)", escape, StringComparison.Ordinal);
            Assert.Contains("char.IsLowSurrogate(ch)", escape, StringComparison.Ordinal);
        }

        [Fact]
        public void EditorGeneratorPreservesLoadedAssemblyDiscovery()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var scanner = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunAssemblyScanner.cs");
            var collectTypes = TestSources.Slice(source, "public static List<(string AsmName, string Ns, string ClassName)> CollectFoxRunTypes()", "        /// <summary>\r\n        /// Generate an IL2CPP link.xml snippet");
            var scanMembers = TestSources.Slice(scanner, "private static FoxRunScanResult ScanFoxRunMembers", "private static void VisitLoadedFoxRunComponentTypes");
            var sharedTraversal = TestSources.Slice(scanner, "private static void VisitLoadedFoxRunComponentTypes", "        /// <summary>\r\n        /// Documents the build-time partial-class enforcement boundary");
            var validate = TestSources.Slice(source, "private static void ValidateGenerationModel", "private static string GetManifestOutputDirectory");
            var emitSourceFile = TestSources.Slice(source, "public static string EmitSourceFile(MemberData[] members)", "public static string EmitSourceFile(FoxRunGenerationType type)");

            Assert.Contains("AppDomain.CurrentDomain.GetAssemblies()", source, StringComparison.Ordinal);
            Assert.Contains("typeof(MonoBehaviour).IsAssignableFrom(type)", source, StringComparison.Ordinal);
            Assert.Contains("ReflectionTypeLoadException", source, StringComparison.Ordinal);
            Assert.DoesNotContain("TypeCache.GetTypesDerivedFrom<MonoBehaviour>()", source, StringComparison.Ordinal);
            Assert.Contains("AppDomain.CurrentDomain.GetAssemblies()", collectTypes, StringComparison.Ordinal);
            Assert.Contains("typeof(MonoBehaviour).IsAssignableFrom(type)", collectTypes, StringComparison.Ordinal);
            Assert.Contains("ReflectionTypeLoadException", scanner, StringComparison.Ordinal);
            Assert.Contains("VisitLoadedFoxRunComponentTypes(ignoreReflectionTypeLoadExceptions", scanMembers, StringComparison.Ordinal);
            Assert.DoesNotContain("AppDomain.CurrentDomain.GetAssemblies()", scanMembers, StringComparison.Ordinal);
            Assert.Contains("AppDomain.CurrentDomain.GetAssemblies()", sharedTraversal, StringComparison.Ordinal);
            Assert.Contains("typeof(MonoBehaviour).IsAssignableFrom(type)", sharedTraversal, StringComparison.Ordinal);
            Assert.DoesNotContain("members.Select(member => member.ToManifestMember())", scanMembers, StringComparison.Ordinal);
            Assert.DoesNotContain("members.Select(member => member.ToReflectionMember())", scanMembers, StringComparison.Ordinal);
            Assert.DoesNotContain("diagnostics.Where", validate, StringComparison.Ordinal);
            Assert.DoesNotContain("members.Select(member => member.ToReflectionMember()).ToList()", emitSourceFile, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "187-R2-E02-003")]
        public void PlayerSourceGenerationUsesFailClosedAssemblyDiscovery()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var generate = TestSources.Slice(
                source,
                "public static List<string> GenerateSourceFiles(",
                "        /// <summary>\r\n        /// Refresh canonical FoxRun manifest artifacts");

            Assert.Contains(
                "ScanFoxRunMembersAndServices(ignoreReflectionTypeLoadExceptions: false)",
                generate,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14069MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_69Validation.cs", "--phase140-69", "Phase140_69Validation.Validate");
    }
}
