// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Negative compilation evidence that the pre-Phase183 declaration API is absent.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunLegacyApiRemovalTests
    {
        [Fact]
        public void LegacyEndpointEncodingTypesAndAliasesAreAbsent()
        {
            var assembly = typeof(FoxRunAttribute).Assembly;

            Assert.Null(assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxRunSubscriptionProvider",
                throwOnError: false));
            Assert.Null(assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxRunWireEncoding",
                throwOnError: false));

            Assert.Null(typeof(FoxRunInputRouter).GetProperty(
                "DefaultWireEncoding",
                BindingFlags.Instance | BindingFlags.Public));
            var managerSource = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            Assert.DoesNotContain("public FoxRunEncoding DefaultFoxRunEncoding", managerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("public FoxRunEncoding ActiveFoxRunDefaultWireEncoding", managerSource, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "public FoxRunEncoding ResolveFoxRunEncoding(FoxRunEncoding declaredEncoding)",
                managerSource,
                StringComparison.Ordinal);
        }

        [Fact]
        public void LegacyRos2QosTypesAreAbsent()
        {
            var assembly = typeof(FoxRunAttribute).Assembly;
            foreach (var typeName in new[]
                     {
                         "Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset",
                         "Unity.FoxgloveSDK.Components.FoxRunRos2QosDiagnosticCode",
                         "Unity.FoxgloveSDK.Components.FoxRunRos2QosResolution",
                         "Unity.FoxgloveSDK.Components.FoxRunRos2QosResolver",
                         "Unity.FoxgloveSDK.Ros2Bridge.Ros2BridgeQosProfile",
                     })
            {
                Assert.Null(assembly.GetType(typeName, throwOnError: false));
            }
        }

        [Fact]
        public void ManagerSerializedFieldsRetainRealLegacyNamesAndCompatibleValues()
        {
            var managerSource = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var root = CSharpSyntaxTree.ParseText(managerSource).GetRoot();
            var encodingField = ExtractField(root, "_defaultFoxRunEncoding");
            var sourceField = ExtractField(root, "_defaultFoxRunSubscriptionSource");
            var fixtureSource = @"
using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HideInInspector : Attribute { }
}
namespace UnityEngine.Serialization
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public sealed class FormerlySerializedAsAttribute : Attribute
    {
        public FormerlySerializedAsAttribute(string oldName) { OldName = oldName; }
        public string OldName { get; }
    }
}
namespace Unity.FoxgloveSDK.Components
{
    public enum FoxRunEncoding { Protobuf = 1, JSON = 2 }
    [Flags]
    public enum FoxRunEndpoint { Foxglove = 1, Ros2Native = 2, Ros2Bridge = 4 }
    public sealed class SerializedFieldFixture
    {
" + encodingField.ToFullString() + sourceField.ToFullString() + @"
    }
}";
            var compilation = CSharpCompilation.Create(
                "FoxRunSerializedFieldMigration_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(fixtureSource) },
                CompilationReferences().Where(reference =>
                    !string.Equals(reference.Display, typeof(FoxRunAttribute).Assembly.Location, StringComparison.OrdinalIgnoreCase)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            var fixtureType = Assembly.Load(image.ToArray()).GetType(
                "Unity.FoxgloveSDK.Components.SerializedFieldFixture",
                throwOnError: true);

            var reflectedEncoding = fixtureType.GetField(
                "_defaultFoxRunEncoding",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var reflectedSource = fixtureType.GetField(
                "_defaultFoxRunSubscriptionSource",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(reflectedEncoding);
            Assert.NotNull(reflectedSource);
            Assert.Equal(
                new[] { "_defaultFoxRunWireEncoding" },
                LegacySerializedNames(reflectedEncoding));
            Assert.Equal(
                new[] { "_defaultFoxRunEndpoint", "_defaultFoxRunSubscriptionProvider" },
                LegacySerializedNames(reflectedSource).OrderBy(value => value, StringComparer.Ordinal));

            var fixture = Activator.CreateInstance(fixtureType);
            Assert.Equal(1, Convert.ToInt32(reflectedEncoding.GetValue(fixture)));
            Assert.Equal(1, Convert.ToInt32(reflectedSource.GetValue(fixture)));
            Assert.Equal(1, (int)FoxRunEncoding.Protobuf);
            Assert.Equal(2, (int)FoxRunEncoding.JSON);
            Assert.Equal(1, (int)FoxRunEndpoint.Foxglove);
            Assert.Equal(2, (int)FoxRunEndpoint.Ros2Native);
        }

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
            yield return Case("RateHz", "RateHz = 10f");
            yield return Case("ChangeEpsilon", "ChangeEpsilon = 0.01f");
            yield return Case("ForceIntervalSeconds", "ForceIntervalSeconds = 1f");
            yield return Case("When", "When = nameof(Enabled)");
            yield return Case("Unless", "Unless = nameof(Enabled)");
            yield return Case("ChangeOrInterval", "Policy = ChangeOrInterval",
                "using static Unity.FoxgloveSDK.Components.FoxRunPolicy;");
        }

        [Fact]
        public void LegacyGeneratedTriggerMethodNameIsUnresolved()
        {
            var compilation = CSharpCompilation.Create(
                "Phase184LegacyTriggerRemoval",
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class RemovedTrigger
    {
        [FoxRun(""/phase184/removed-trigger"", Policy = Trigger)]
        private float _value;

        public bool InvokeLegacyName() => FoxRun_Trigger_value();
    }
}")
                },
                CompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGenerators(compilation);

            var runResult = driver.GetRunResult();
            Assert.DoesNotContain(
                runResult.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Contains(
                runResult.GeneratedTrees,
                tree => tree.ToString().Contains(
                    "partial class RemovedTrigger",
                    StringComparison.Ordinal));

            var generatedPublicMethods = runResult.GeneratedTrees
                .SelectMany(tree => tree.GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>())
                .Where(method =>
                    method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword))
                    && method.ReturnType.ToString() == "bool"
                    && method.ParameterList.Parameters.Count == 0)
                .Select(method => method.Identifier.ValueText)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(methodName => methodName, StringComparer.Ordinal)
                .ToArray();
            Assert.Contains("FoxRun_Publish_value", generatedPublicMethods);
            var generatedApiStub = @"
namespace Demo
{
    public partial class RemovedTrigger
    {
" + string.Join(
                    Environment.NewLine,
                    generatedPublicMethods.Select(methodName =>
                        "        public bool " + methodName + "() => false;")) + @"
    }
}";
            var consumerCompilation = compilation.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(generatedApiStub));
            var errors = consumerCompilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.DoesNotContain(errors, diagnostic =>
                diagnostic.Id != "CS0103");
            var unresolved = Assert.Single(errors);
            Assert.Equal("CS0103", unresolved.Id);
            Assert.Contains("FoxRun_Trigger_value", unresolved.GetMessage(), StringComparison.Ordinal);
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
        private bool Enabled => true;

        [FoxRun(""/phase183/removed"", " + attributeArguments + @")]
        private float _value;
    }
}"
            };

        private static FieldDeclarationSyntax ExtractField(SyntaxNode root, string fieldName)
            => root.DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .Single(field => field.Declaration.Variables.Any(variable =>
                    string.Equals(variable.Identifier.ValueText, fieldName, StringComparison.Ordinal)));

        private static string[] LegacySerializedNames(FieldInfo field)
            => field.GetCustomAttributesData()
                .Where(attribute =>
                    string.Equals(
                        attribute.AttributeType.Name,
                        "FormerlySerializedAsAttribute",
                        StringComparison.Ordinal))
                .Select(attribute => Assert.IsType<string>(attribute.ConstructorArguments.Single().Value))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

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
