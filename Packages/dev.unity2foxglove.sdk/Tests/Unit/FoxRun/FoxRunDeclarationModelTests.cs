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
        public void FoxRunAttributeDefaultsToPublishFlow()
        {
            var attr = new FoxRunAttribute("/phase157/default");

            Assert.Equal(FoxRunFlow.Publish, attr.Mode);
        }

        [Fact]
        public void GenerationMemberConstructorsInferOmittedAndExplicitJsonEncodingPresence()
        {
            var omitted = new[]
            {
                new FoxRunGenerationMember(
                    ns: "Demo",
                    className: "Defaults",
                    memberName: "_first",
                    memberKind: "field",
                    rawTypeName: "System.Int32",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/phase184/defaults/first",
                    hz: -1f,
                    schemaName: "",
                    policy: 1,
                    tolerance: 0f,
                    hostKind: "UnitTest",
                    rawMemberOrder: 0,
                    conditionalSymbols: ""),
                new FoxRunGenerationMember(
                    ns: "Demo",
                    className: "Defaults",
                    memberName: "_second",
                    memberKind: "field",
                    rawObservedTypeName: "System.Int32",
                    emissionTypeName: "int",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/phase184/defaults/second",
                    hz: -1f,
                    schemaName: "",
                    policy: 1,
                    tolerance: 0f,
                    hostKind: "UnitTest",
                    rawMemberOrder: 1,
                    conditionalSymbols: ""),
                new FoxRunGenerationMember(
                    ns: "Demo",
                    className: "Defaults",
                    memberName: "_third",
                    memberKind: "field",
                    rawObservedTypeName: "System.Int32",
                    emissionTypeName: "int",
                    canonicalType: "int32",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/phase184/defaults/third",
                    hz: -1f,
                    schemaName: "",
                    policy: 1,
                    tolerance: 0f,
                    hostKind: "UnitTest",
                    rawMemberOrder: 2,
                    conditionalSymbols: "")
            };
            var explicitJson = new FoxRunGenerationMember(
                ns: "Demo",
                className: "Defaults",
                memberName: "_json",
                memberKind: "field",
                rawTypeName: "System.Int32",
                isValueType: true,
                isArray: false,
                elementTypeName: "",
                topic: "/phase184/defaults/json",
                hz: -1f,
                schemaName: "",
                policy: 1,
                tolerance: 0f,
                hostKind: "UnitTest",
                rawMemberOrder: 3,
                conditionalSymbols: "",
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding);

            Assert.All(omitted, member =>
            {
                Assert.Equal(FoxRunGenerationDescriptorConstants.InheritEncoding, member.Encoding);
                Assert.Equal(FoxRunNamedArgumentPresence.None, member.NamedArgumentPresence);
                Assert.False(member.HasNamedArgument(FoxRunNamedArgumentPresence.Encoding));
            });
            Assert.Equal(FoxRunGenerationDescriptorConstants.JsonEncoding, explicitJson.Encoding);
            Assert.True(explicitJson.HasNamedArgument(FoxRunNamedArgumentPresence.Encoding));
            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(
                    FoxRunGenerationModel.FromMembers(omitted.Append(explicitJson).ToArray())),
                diagnostic => diagnostic.Severity == "Error");
        }

        [Fact]
        public void SharedTopicMemberDefaultsAndBlankEncodingToInheritedContract()
        {
            var omitted = new FoxgloveSourceEmitter.TopicMember(
                "_omitted",
                "System.Int32",
                "/phase184/defaults/topic-member-omitted",
                10f,
                "");
            var blank = new FoxgloveSourceEmitter.TopicMember(
                "_blank",
                "System.Int32",
                "/phase184/defaults/topic-member-blank",
                10f,
                "",
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                encoding: " ");

            Assert.Equal(FoxRunGenerationDescriptorConstants.InheritEncoding, omitted.Encoding);
            Assert.Equal(FoxRunGenerationDescriptorConstants.InheritEncoding, blank.Encoding);
        }



        [Fact]
        public void GeneratedTopicMetadataDistinguishesInheritedAndExplicitPublishRates()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class PublishRates
    {
        [FoxRun(""/phase184/rate/inherited"")]
        public int Inherited;

        [FoxRun(""/phase184/rate/explicit"", Hz = 7f)]
        public int Explicit;

        [FoxRun(""/phase184/rate/mixed"")]
        public int MixedInherited;

        [FoxRun(""/phase184/rate/mixed"", Hz = 7f)]
        public int MixedExplicit;
    }
}");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class PublishRates", StringComparison.Ordinal));
            var topicLines = generated
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("new FoxgloveLogTopicInfo", StringComparison.Ordinal))
                .ToArray();
            var inherited = topicLines.Single(line =>
                line.Contains("\"/phase184/rate/inherited\"", StringComparison.Ordinal));
            var explicitRate = topicLines.Single(line =>
                line.Contains("\"/phase184/rate/explicit\"", StringComparison.Ordinal));
            var mixedRate = topicLines.Single(line =>
                line.Contains("\"/phase184/rate/mixed\"", StringComparison.Ordinal));

            Assert.Contains("hasExplicitHz: false", inherited, StringComparison.Ordinal);
            Assert.DoesNotContain("hasExplicitHz: false", explicitRate, StringComparison.Ordinal);
            Assert.Contains(", 7f,", mixedRate, StringComparison.Ordinal);
            Assert.DoesNotContain("hasExplicitHz: false", mixedRate, StringComparison.Ordinal);
        }

        [Fact]
        public void FreshDeclarationEnumsExposeOnlyNewNonZeroFlowAndPolicyValues()
        {
            var assembly = typeof(FoxRunAttribute).Assembly;
            var flowType = assembly.GetType("Unity.FoxgloveSDK.Components.FoxRunFlow");
            var policyType = assembly.GetType("Unity.FoxgloveSDK.Components.FoxRunPolicy");

            Assert.NotNull(flowType);
            Assert.NotNull(policyType);
            Assert.True(flowType.IsEnum);
            Assert.True(policyType.IsEnum);
            Assert.Equal(
                new[] { "Publish", "Subscribe", "PublishAndSubscribe" },
                Enum.GetNames(flowType));
            Assert.Equal(
                new[] { "FixedRate", "Change", "Trigger" },
                Enum.GetNames(policyType));
            Assert.Equal(1, Convert.ToInt32(Enum.Parse(flowType, "Publish")));
            Assert.Equal(2, Convert.ToInt32(Enum.Parse(flowType, "Subscribe")));
            Assert.Equal(3, Convert.ToInt32(Enum.Parse(flowType, "PublishAndSubscribe")));
            Assert.Equal(1, Convert.ToInt32(Enum.Parse(policyType, "FixedRate")));
            Assert.Equal(2, Convert.ToInt32(Enum.Parse(policyType, "Change")));
            Assert.Equal(4, Convert.ToInt32(Enum.Parse(policyType, "Trigger")));
        }

        [Fact]
        public void InvalidPolicyDiagnosticNamesTheSupportedPolicies()
        {
            var message = Diags.InvalidPolicy.MessageFormat.ToString();

            Assert.Contains("FixedRate", message, StringComparison.Ordinal);
            Assert.Contains("Change", message, StringComparison.Ordinal);
            Assert.Contains("Trigger", message, StringComparison.Ordinal);
            Assert.DoesNotContain("ChangeOrInterval", message, StringComparison.Ordinal);
            Assert.DoesNotContain("between 0 and 3", message, StringComparison.Ordinal);
        }

        [Fact]
        public void ShortSchedulingDeclarationGrammarCompiles()
        {
            var output = CreateCompilation(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class SchedulingGrammar
    {
        private bool TelemetryEnabled => true;

        [FoxRun(""/phase184/change"", Policy = Change, Hz = 10f,
            Tolerance = 0.01f, OnlyIf = nameof(TelemetryEnabled))]
        private float _changed;

        [FoxRun(""/phase184/subscribe"", Mode = Subscribe, Hz = 20f,
            OnlyIf = nameof(TelemetryEnabled))]
        private int _subscribed;
    }
}");

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void StaticImportDeclarationGrammarCompilesAllFlowsAndFixedRatePolicy()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class DeclarationGrammar
    {
        [FoxRun(""/phase183/default"")]
        private float _defaultValue;

        [FoxRun(""/phase183/publish"", Mode = Publish, Policy = FixedRate)]
        private float _publishedValue;

        [FoxRun(""/phase183/subscribe"", Mode = Subscribe, Policy = FixedRate)]
        private float _subscribedValue;

        [FoxRun(""/phase183/full-duplex"", Mode = PublishAndSubscribe,
            Policy = FixedRate, Encoding = FoxRunEncoding.Protobuf)]
        private float _sharedValue;
    }
}");

            Assert.DoesNotContain(result.Diagnostics, diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
            var generated = string.Join(
                Environment.NewLine,
                result.GeneratedTrees.Select(tree => tree.GetText().ToString()));
            Assert.Contains("/phase183/publish", generated, StringComparison.Ordinal);
            Assert.Contains("/phase183/subscribe", generated, StringComparison.Ordinal);
            Assert.Contains("/phase183/full-duplex", generated, StringComparison.Ordinal);
        }

    }
}
