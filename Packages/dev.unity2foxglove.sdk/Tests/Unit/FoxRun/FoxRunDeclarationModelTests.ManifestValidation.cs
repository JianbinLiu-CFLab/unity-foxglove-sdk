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
        public void ManifestGroupsIdenticalContractsWithOrdinalKeys()
        {
            var manifest = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember("_speed", "/phase157/state", "speed"),
                ManifestMember("_state", "/phase157/state", "state"),
                ManifestMember("_speedUpper", "/phase157/State", "speedUpper")
            });

            var contracts = manifest.Sections.FoxRun.Types[0].Contracts;

            Assert.Equal(2, contracts.Count);
            Assert.Contains(contracts, contract => contract.Topic == "/phase157/state" && contract.Fields.Count == 2);
            Assert.Contains(contracts, contract => contract.Topic == "/phase157/State" && contract.Fields.Count == 1);
        }

        [Fact]
        public void ManifestPolicyHashInputCanonicalizesNonFiniteFloats()
        {
            var hashInput = FoxRunManifestJsonWriter.WritePolicyHashInput(new FoxRunManifestPolicy(
                "Change",
                float.NaN,
                float.PositiveInfinity));

            Assert.Contains("\"hz\":0", hashInput, StringComparison.Ordinal);
            Assert.Contains("\"tolerance\":0", hashInput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"rateHz\"", hashInput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"changeEpsilon\"", hashInput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"forceIntervalSeconds\"", hashInput, StringComparison.Ordinal);
        }

        [Fact]
        public void InboundValidationRejectsJsonArraysWithoutLegacyIgnoredOptionWarning()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingSamples", "field", "System.Single[]",
                    false, true, "System.Single", "/phase157/samples", 10f, "",
                    1, 0.1f, "UnitTest", 0, "",
                    mode: (int)FoxRunFlow.Subscribe)
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN200" && diagnostic.Severity == "Error");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN201");
        }

        [Fact]
        public void InboundValidationAllowsExplicitProtobufArrays()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingSamples", "field", "System.Single[]",
                    false, true, "System.Single", "/phase175/samples", 10f, "",
                    1, 0.1f, "UnitTest", 0, "",
                    mode: (int)FoxRunFlow.Subscribe,
                    encoding: "protobuf",
                    typeShape: FoxRunTypeShape.Canonical("float32"))
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN200" && diagnostic.Severity == "Error");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN201");
        }

        [Fact]
        public void RoslynGeneratorDoesNotEmitNullableProtobufWriterSyntaxOrConversionErrors()
        {
            var output = RunGeneratorAndUpdateCompilation(@"
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public sealed class OptionalPayload
    {
        public int? OptionalCount;
        public List<int?> Samples = new List<int?>();
    }

    public partial class NullablePublisher
    {
        [FoxRun(""/phase175/optional-root"", Encoding = FoxRunEncoding.Protobuf)]
        public int? OptionalRoot;

        [FoxRun(""/phase175/optional-payload"", Encoding = FoxRunEncoding.Protobuf)]
        public OptionalPayload Payload = new OptionalPayload();
    }
}");

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Id == "CS1001" || diagnostic.Id == "CS1503");
        }

        [Fact]
        public void RoslynGeneratorRejectsReadOnlyInboundProperty()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        [FoxRun(""/phase157/cmd"", Mode = FoxRunFlow.Subscribe)]
        private float IncomingCommand => 0;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN203");
        }

        [Fact]
        public void SourceEmitterRejectsMalformedInboundMembers()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "CommandInput",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "CommandInput", "", "field", "System.Single",
                        false, false, "", "/phase173/input", 10f, "",
                        0, 0f, "UnitTest", 0, "",
                        mode: (int)FoxRunFlow.Subscribe)
                });

            var ex = Assert.Throws<ArgumentException>(() => FoxgloveSourceEmitter.EmitClass(type));

            Assert.Contains("TopicMember has empty MemberName", ex.Message, StringComparison.Ordinal);
        }

        private static FoxRunGenerationModel ModelWithMode(FoxRunFlow mode)
        {
            return FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingVelocity", "field", "UnityEngine.Vector3",
                    true, false, "", "/phase157/cmd_vel", 10f, "",
                    1, 0f, "UnitTest", 0, "",
                    mode: (int)mode)
            });
        }

        private static void AssertGeneratedBooleanMethod(
            IEnumerable<MethodDeclarationSyntax> methods,
            string methodName)
        {
            var method = Assert.Single(
                methods,
                candidate => candidate.Identifier.ValueText == methodName);

            Assert.Contains(method.Modifiers, modifier =>
                modifier.IsKind(SyntaxKind.PublicKeyword));
            Assert.Equal("bool", method.ReturnType.ToString());
            Assert.Empty(method.ParameterList.Parameters);
        }

        private static object ReadReflectionAttributeSnapshot(FieldInfo field)
        {
            Assert.NotNull(field);
            var reader = typeof(FoxrunCodeGenerator).GetMethod(
                "ReadFoxRunAttributeSnapshots",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(reader);
            var snapshots = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                reader.Invoke(null, new object[] { field }));
            return Assert.Single(snapshots.Cast<object>());
        }

        private static object ReadReflectionMessageAttributeSnapshot(Type type)
        {
            var reader = typeof(FoxrunCodeGenerator).GetMethod(
                "ReadFoxRunMessageAttributeSnapshot",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(reader);
            var snapshot = reader.Invoke(null, new object[] { type });
            Assert.NotNull(snapshot);
            return snapshot;
        }

        private static IReadOnlyDictionary<string, FoxRunConditionMemberKind> ScanReflectionConditionKinds(
            Type type)
        {
            const BindingFlags flags = BindingFlags.Public
                                       | BindingFlags.NonPublic
                                       | BindingFlags.Instance
                                       | BindingFlags.DeclaredOnly;
            return type.GetFields(flags)
                .Where(field => field.GetCustomAttribute<FoxRunAttribute>() != null)
                .ToDictionary(
                    field => field.Name,
                    field =>
                    {
                        var snapshot = ReadReflectionAttributeSnapshot(field);
                        return FoxRunReflectionConditionMemberResolver.Resolve(
                            type,
                            ReadField<string>(snapshot, "OnlyIf"),
                            (FoxRunNamedArgumentPresence)ReadInt64Field(
                                snapshot,
                                "NamedArgumentPresence"));
                    },
                    StringComparer.Ordinal);
        }

        private static T ReadField<T>(object value, string name)
        {
            var field = value.GetType().GetField(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return Assert.IsType<T>(field.GetValue(value));
        }

        private static long ReadInt64Field(object value, string name)
        {
            var field = value.GetType().GetField(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return Convert.ToInt64(field.GetValue(value));
        }

        private static FoxRunManifestMember ManifestMember(FoxRunFlow mode)
        {
            return new FoxRunManifestMember(
                "Demo",
                "CommandInput",
                "_incomingVelocity",
                "field",
                "UnityEngine.Vector3",
                true,
                false,
                "",
                "/phase157/cmd_vel",
                10f,
                "",
                1,
                0f,
                flow: (int)mode);
        }

        private static FoxRunManifestMember ManifestMember(string memberName, string topic, string jsonFieldName)
        {
            return new FoxRunManifestMember(
                "Demo",
                "CommandInput",
                memberName,
                "field",
                "System.Single",
                true,
                false,
                "",
                topic,
                10f,
                "",
                1,
                0f,
                jsonFieldName: jsonFieldName);
        }

        private static MetadataReference[] BasicReferences()
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
            var trusted = trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => MetadataReference.CreateFromFile(path));

            return trusted
                .Concat(new[]
                {
                    MetadataReference.CreateFromFile(typeof(UnityEngine.Vector3).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(FoxRunAttribute).Assembly.Location)
                })
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }



    }
}
