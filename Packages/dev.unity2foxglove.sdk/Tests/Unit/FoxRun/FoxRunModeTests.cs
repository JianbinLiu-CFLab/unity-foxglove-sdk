// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunModeTests
    {
        [Fact]
        public void FoxRunAttributeDefaultsToPublishOnlyMode()
        {
            var attr = new FoxRunAttribute("/phase157/default");

            Assert.Equal(FoxRunMode.PublishOnly, attr.Mode);
        }

        [Fact]
        public void SubscribeOnlyMembersStayOutOfGeneratedPublishDispatch()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "CommandInput",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "CommandInput", "_status", "field", "System.String",
                        true, false, "", "/phase157/status", 10f, "",
                        0, 0f, 0f, "UnitTest", 0, ""),
                    new FoxRunGenerationMember(
                        "Demo", "CommandInput", "_incomingVelocity", "field", "UnityEngine.Vector3",
                        true, false, "", "/phase157/cmd_vel", 10f, "",
                        0, 0f, 0f, "UnitTest", 1, "",
                        mode: (int)FoxRunMode.SubscribeOnly)
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("FoxgloveLog_TopicCount => 1", source, StringComparison.Ordinal);
            Assert.Contains("/phase157/status", source, StringComparison.Ordinal);
            Assert.Contains("FoxgloveInputTopicInfo(\"/phase157/cmd_vel\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("mgr.PublishJson(\"/phase157/cmd_vel\"", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorLowersSubscribeOnlyModeWithoutPublishingTopic()
        {
            var source = @"
using UnityEngine;
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        [FoxRun(""/phase157/status"")]
        private string _status;

        [FoxRun(""/phase157/cmd_vel"", Mode = FoxRunMode.SubscribeOnly)]
        private Vector3 _incomingVelocity;
    }
}";
            var result = RunGenerator(source);
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .SingleOrDefault(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.True(
                generated != null,
                "Expected CommandInput generated source. Diagnostics: " +
                string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            Assert.Contains("/phase157/status", generated, StringComparison.Ordinal);
            Assert.Contains("FoxgloveInputTopicInfo(\"/phase157/cmd_vel\"", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("mgr.PublishJson(\"/phase157/cmd_vel\"", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("router.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(1)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorEmitsTypedSubscribeOnlyAssignment()
        {
            var source = @"
using UnityEngine;
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        [FoxRun(""/phase157/cmd_vel"", Mode = FoxRunMode.SubscribeOnly)]
        private Vector3 _incomingVelocity;
    }
}";
            var result = RunGenerator(source);
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.Contains("partial class CommandInput : IFoxgloveInputSource", generated, StringComparison.Ordinal);
            Assert.Contains("int IFoxgloveInputSource.FoxgloveInput_TopicCount => 1", generated, StringComparison.Ordinal);
            Assert.Contains("new FoxgloveInputTopicInfo(\"/phase157/cmd_vel\", \"json\", FoxRunMode.SubscribeOnly)", generated, StringComparison.Ordinal);
            Assert.Contains("FoxRunInboundJson.TryRead(payload, \"incomingVelocity\", out global::UnityEngine.Vector3 __value", generated, StringComparison.Ordinal);
            Assert.Contains("this._incomingVelocity = __value", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("IFoxgloveLogSource", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorEmitsPublishAndSubscribeOnBothSurfaces()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class SharedState
    {
        [FoxRun(""/phase157/state"", Mode = FoxRunMode.PublishAndSubscribe)]
        private string _state;
    }
}";
            var result = RunGenerator(source);
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class SharedState", StringComparison.Ordinal));

            Assert.Contains("IFoxgloveLogSource", generated, StringComparison.Ordinal);
            Assert.Contains("IFoxgloveInputSource", generated, StringComparison.Ordinal);
            Assert.Contains("this._state = __value", generated, StringComparison.Ordinal);
            Assert.Contains("__foxRunSuppressNextPublish_0 = true", generated, StringComparison.Ordinal);
            Assert.Contains("if (__foxRunSuppressNextPublish_0)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorReadsFoxRunModeFromSemanticConstant()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        private const FoxRunMode Inbound = FoxRunMode.SubscribeOnly;

        [FoxRun(""/phase157/cmd_vel"", Mode = Inbound)]
            private float _incomingVelocity;
    }
}";
            var result = RunGenerator(source);
            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .SingleOrDefault(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.True(
                generated != null,
                "Expected CommandInput generated source. Diagnostics: " +
                string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            Assert.Contains("FoxgloveInputTopicInfo(\"/phase157/cmd_vel\"", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("mgr.PublishJson(\"/phase157/cmd_vel\"", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorEmitsPrimitiveInboundAssignmentsWithValidTypeName()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        [FoxRun(""/phase157/target-speed"", Mode = FoxRunMode.SubscribeOnly)]
        private float requestedTargetSpeed;
    }
}");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.Contains("FoxRunInboundJson.TryRead(payload, \"requestedTargetSpeed\", out float __value", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("out global::float __value", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorScopesInboundAssignmentLocalsPerTopic()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        [FoxRun(""/phase157/shared-state"", Mode = FoxRunMode.PublishAndSubscribe)]
        private float sharedState;

        [FoxRun(""/phase157/target-speed"", Mode = FoxRunMode.SubscribeOnly)]
        private float requestedTargetSpeed;
    }
}");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString().Replace("\r\n", "\n", StringComparison.Ordinal))
                .Single(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.Contains("case 0:\n                    {", generated, StringComparison.Ordinal);
            Assert.Contains("case 1:\n                    {", generated, StringComparison.Ordinal);
            Assert.Contains("FoxRunInboundJson.TryRead(payload, \"requestedTargetSpeed\", out float __value", generated, StringComparison.Ordinal);
            Assert.Contains("FoxRunInboundJson.TryRead(payload, \"sharedState\", out float __value", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynAttributeDataExposesFoxRunModeConstant()
        {
            var compilation = CreateCompilation(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        private const FoxRunMode Inbound = FoxRunMode.SubscribeOnly;

        [FoxRun(""/phase157/cmd_vel"", Mode = Inbound)]
        private float _incomingVelocity;
    }
}");
            Assert.DoesNotContain(
                compilation.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var member = compilation.GetTypeByMetadataName("Demo.CommandInput")
                .GetMembers("_incomingVelocity")
                .Single();
            var mode = member.GetAttributes()
                .Single()
                .NamedArguments
                .Single(argument => argument.Key == "Mode")
                .Value;

            Assert.Equal(1, Convert.ToInt32(mode.Value));
        }

        [Fact]
        public void DescriptorJsonIncludesExplicitFoxRunMode()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingVelocity", "field", "UnityEngine.Vector3",
                    true, false, "", "/phase157/cmd_vel", 10f, "",
                    0, 0f, 0f, "UnitTest", 0, "",
                    mode: (int)FoxRunMode.SubscribeOnly)
            });

            var json = FoxRunGenerationDescriptorJsonWriter.Write(model);

            Assert.Contains("\"mode\":\"SubscribeOnly\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void DescriptorComparerTreatsFoxRunModeAsSemanticState()
        {
            var publishOnly = ModelWithMode(FoxRunMode.PublishOnly);
            var subscribeOnly = ModelWithMode(FoxRunMode.SubscribeOnly);

            var comparison = FoxRunGenerationDescriptorComparer.Compare(publishOnly, subscribeOnly);

            Assert.False(comparison.IsSemanticEqual);
            Assert.Contains(
                comparison.SemanticDifferences,
                difference => difference.Contains("mode", StringComparison.Ordinal));
        }

        [Fact]
        public void ManifestRecordsInboundFlowWithoutChangingDefaultCanonicalShape()
        {
            var publishOnly = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunMode.PublishOnly)
            });
            var subscribeOnly = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunMode.SubscribeOnly)
            });
            var publishAndSubscribe = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunMode.PublishAndSubscribe)
            });

            var publishJson = FoxRunManifestJsonWriter.WriteCanonical(publishOnly);
            var subscribeJson = FoxRunManifestJsonWriter.WriteCanonical(subscribeOnly);
            var publishAndSubscribeJson = FoxRunManifestJsonWriter.WriteCanonical(publishAndSubscribe);

            Assert.DoesNotContain("\"flowMode\"", publishJson, StringComparison.Ordinal);
            Assert.Contains("\"flowMode\":\"SubscribeOnly\"", subscribeJson, StringComparison.Ordinal);
            Assert.Contains("\"flowMode\":\"PublishAndSubscribe\"", publishAndSubscribeJson, StringComparison.Ordinal);
            Assert.NotEqual(
                publishOnly.Sections.FoxRun.Types[0].Contracts[0].ContractHash,
                subscribeOnly.Sections.FoxRun.Types[0].Contracts[0].ContractHash);
            Assert.NotEqual(
                publishOnly.Sections.FoxRun.Types[0].Contracts[0].ContractHash,
                publishAndSubscribe.Sections.FoxRun.Types[0].Contracts[0].ContractHash);
        }

        [Fact]
        public void ManifestRejectsUnknownPublishMode()
        {
            var member = new FoxRunManifestMember(
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
                99,
                0f,
                0f);

            var ex = Assert.Throws<InvalidOperationException>(() => FoxRunManifestBuilder.Build(new[] { member }));

            Assert.Contains("publish mode", ex.Message, StringComparison.Ordinal);
            Assert.Contains("0..3", ex.Message, StringComparison.Ordinal);
        }

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
                "OnChange",
                float.NaN,
                float.PositiveInfinity,
                float.NegativeInfinity));

            Assert.Contains("\"rateHz\":0", hashInput, StringComparison.Ordinal);
            Assert.Contains("\"changeEpsilon\":0", hashInput, StringComparison.Ordinal);
            Assert.Contains("\"forceIntervalSeconds\":0", hashInput, StringComparison.Ordinal);
        }

        [Fact]
        public void InboundValidationRejectsArraysAndWarnsAboutPublishOptions()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingSamples", "field", "System.Single[]",
                    false, true, "System.Single", "/phase157/samples", 10f, "",
                    1, 0.1f, 2f, "UnitTest", 0, "",
                    mode: (int)FoxRunMode.SubscribeOnly)
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN024" && diagnostic.Severity == "Error");
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN025" && diagnostic.Severity == "Warning");
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
        [FoxRun(""/phase157/cmd"", Mode = FoxRunMode.SubscribeOnly)]
        private float IncomingCommand => 0;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN028");
        }

        private static FoxRunGenerationModel ModelWithMode(FoxRunMode mode)
        {
            return FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingVelocity", "field", "UnityEngine.Vector3",
                    true, false, "", "/phase157/cmd_vel", 10f, "",
                    0, 0f, 0f, "UnitTest", 0, "",
                    mode: (int)mode)
            });
        }

        private static FoxRunManifestMember ManifestMember(FoxRunMode mode)
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
                0,
                0f,
                0f,
                mode: (int)mode);
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
                0,
                0f,
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

        private static GeneratorDriverRunResult RunGenerator(string source)
        {
            var compilation = CreateCompilation(source);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGenerators(compilation);
            return driver.GetRunResult();
        }

        private static CSharpCompilation CreateCompilation(string source)
        {
            return CSharpCompilation.Create(
                "Phase157GeneratorProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                BasicReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
