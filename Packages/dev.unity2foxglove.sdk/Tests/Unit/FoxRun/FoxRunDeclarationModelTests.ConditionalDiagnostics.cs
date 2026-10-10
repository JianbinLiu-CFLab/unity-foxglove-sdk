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
        public void RoslynGeneratorRejectsTriggerWithExplicitHzUsingReservedDiagnostic()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class TriggerState
    {
        [FoxRun(""/phase184/trigger"", Policy = Trigger, Hz = 10f)]
        private int _count;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN609");
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN000");
        }

        [Theory]
        [InlineData("0f")]
        [InlineData("-1f")]
        [InlineData("float.NaN")]
        [InlineData("float.PositiveInfinity")]
        public void RoslynGeneratorRejectsTriggerWithEveryExplicitHzValue(string hzExpression)
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class TriggerState
    {
        [FoxRun(""/phase184/trigger"", Policy = Trigger, Hz = " + hzExpression + @")]
        private int _count;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN609");
        }

        [Fact]
        public void RoslynGeneratorSupportsZeroArgumentBoolMethodOnlyIf()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class ConditionalState
    {
        private bool CanSend() => true;

        [FoxRun(""/phase184/conditional"", OnlyIf = nameof(CanSend))]
        private int _count;
    }
}");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class ConditionalState", StringComparison.Ordinal));

            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN015" || diagnostic.Id == "FOXRUN016");
            Assert.Contains("CanSend()", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorCompilesEveryAccessibleInheritedOnlyIfShape()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public class ConditionalBase
    {
        public bool PublicField;
        protected bool ProtectedProperty => true;
        protected internal bool ProtectedInternalMethod() => true;
        internal bool InternalField;
        private protected bool PrivateProtectedProperty => true;
    }

    public partial class ConditionalState : ConditionalBase
    {
        private bool CurrentPrivateMethod() => true;

        [FoxRun(""/phase184/conditional/public-field"", OnlyIf = ""PublicField"",
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _publicField;

        [FoxRun(""/phase184/conditional/protected-property"", OnlyIf = ""ProtectedProperty"",
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _protectedProperty;

        [FoxRun(""/phase184/conditional/protected-internal-method"", OnlyIf = ""ProtectedInternalMethod"",
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _protectedInternalMethod;

        [FoxRun(""/phase184/conditional/internal-field"", OnlyIf = ""InternalField"",
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _internalField;

        [FoxRun(""/phase184/conditional/private-protected-property"", OnlyIf = ""PrivateProtectedProperty"",
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _privateProtectedProperty;

        [FoxRun(""/phase184/conditional/current-private-method"", OnlyIf = nameof(CurrentPrivateMethod),
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _currentPrivateMethod;
    }
}";
            var result = RunGenerator(source);

            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN015" || diagnostic.Id == "FOXRUN016");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class ConditionalState", StringComparison.Ordinal));
            Assert.Contains("ProtectedProperty", generated, StringComparison.Ordinal);
            Assert.Contains("ProtectedInternalMethod()", generated, StringComparison.Ordinal);
            Assert.Contains("PrivateProtectedProperty", generated, StringComparison.Ordinal);
            Assert.Contains("CurrentPrivateMethod()", generated, StringComparison.Ordinal);
            Assert.DoesNotContain(
                RunGeneratorAndUpdateCompilation(source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void RoslynAndReflectionRejectPrivateBaseOnlyIf()
        {
            var roslyn = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public class ConditionalBase
    {
        private bool Hidden => true;
    }

    public partial class ConditionalState : ConditionalBase
    {
        [FoxRun(""/phase184/conditional/private-base"", OnlyIf = ""Hidden"")]
        private int _value;
    }
}");
            var reflection = ScanReflectionConditionKinds(typeof(ReflectionInheritedConditionFixture));

            Assert.Contains(roslyn.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN015");
            Assert.Equal(
                FoxRunConditionMemberKind.Missing,
                reflection[nameof(ReflectionInheritedConditionFixture.PrivateBase)]);
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        public void GeneratedBarePublisherObserverSideChannelCompilesWithoutCaptureSequenceState()
        {
            var output = RunGeneratorAndUpdateCompilation(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class BarePublisher
    {
        [FoxRun(""/phase184/bare-observer"")]
        private float _value;
    }
}");

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
#endif

        [Fact]
        public void ReflectionScannerMatchesAccessibleInheritedOnlyIfShapes()
        {
            var members = ScanReflectionConditionKinds(typeof(ReflectionInheritedConditionFixture));

            Assert.Equal(
                FoxRunConditionMemberKind.Field,
                members[nameof(ReflectionInheritedConditionFixture.PublicFieldProbe)]);
            Assert.Equal(
                FoxRunConditionMemberKind.Property,
                members[nameof(ReflectionInheritedConditionFixture.ProtectedPropertyProbe)]);
            Assert.Equal(
                FoxRunConditionMemberKind.Method,
                members[nameof(ReflectionInheritedConditionFixture.ProtectedInternalMethodProbe)]);
            Assert.Equal(
                FoxRunConditionMemberKind.Field,
                members[nameof(ReflectionInheritedConditionFixture.InternalFieldProbe)]);
            Assert.Equal(
                FoxRunConditionMemberKind.Property,
                members[nameof(ReflectionInheritedConditionFixture.PrivateProtectedPropertyProbe)]);
            Assert.Equal(
                FoxRunConditionMemberKind.Method,
                members[nameof(ReflectionInheritedConditionFixture.CurrentPrivateMethodProbe)]);
        }

        [Fact]
        public void RoslynAndReflectionDoNotBypassInvalidInheritedOnlyIfShadow()
        {
            var roslyn = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public class ConditionalGrandBase
    {
        protected bool Gate => true;
    }

    public class ConditionalBase : ConditionalGrandBase
    {
        public new int Gate;
    }

    public partial class ConditionalState : ConditionalBase
    {
        [FoxRun(""/phase184/conditional/invalid-shadow"", OnlyIf = ""Gate"")]
        private int _value;
    }
}");
            var reflection = ScanReflectionConditionKinds(typeof(ReflectionInheritedConditionFixture));

            Assert.Contains(roslyn.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN016");
            Assert.DoesNotContain(roslyn.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN015");
            Assert.Equal(
                FoxRunConditionMemberKind.Invalid,
                reflection[nameof(ReflectionInheritedConditionFixture.InvalidShadowProbe)]);
        }

        [Fact]
        public void RoslynGeneratorRejectsExplicitEmptyOnlyIf()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class ConditionalState
    {
        [FoxRun(""/phase184/conditional"", OnlyIf = """")]
        private int _count;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN015");
        }

        [Fact]
        public void RoslynAndReflectionRejectWhitespacePaddedOnlyIf()
        {
            var roslyn = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class ConditionalState
    {
        private bool Enabled => true;

        [FoxRun(""/phase184/conditional"", OnlyIf = "" Enabled "")]
        private int _count;
    }
}");
            var snapshot = ReadReflectionAttributeSnapshot(
                typeof(ReflectionArgumentsFixture).GetField(
                    nameof(ReflectionArgumentsFixture.WhitespaceCondition)));
            var onlyIf = ReadField<string>(snapshot, "OnlyIf");
            var presence = (FoxRunNamedArgumentPresence)ReadInt64Field(
                snapshot,
                "NamedArgumentPresence");
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(
                new[]
                {
                    new FoxRunReflectionGenerationMember(
                        "Demo", "ReflectionArgumentsFixture", "WhitespaceCondition",
                        "field", "System.Single", "float",
                        true, false, "", "/phase184/reflection/whitespace-condition",
                        "", -1f, 1, 0f, 0, "",
                        onlyIf: onlyIf,
                        namedArgumentPresence: presence,
                        conditionMemberKind: FoxRunConditionMemberKind.Missing)
                });

            Assert.Contains(roslyn.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN015");
            Assert.Equal(" Enabled ", onlyIf);
            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(reflection),
                diagnostic => diagnostic.Id == "FOXRUN015");
        }

        [Fact]
        public void GeneratedMethodNameCollisionsReportStableFoxRunDiagnostic()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class TriggerCollisions
    {
        [FoxRun(""/phase184/publish-a"", Policy = Trigger)]
        private int _command;

        [FoxRun(""/phase184/publish-b"", Policy = Trigger)]
        private int command;

        [FoxRun(""/phase184/subscribe"", Mode = Subscribe, Policy = Trigger,
            Encoding = FoxRunEncoding.JSON)]
        private int _incoming;

        public bool FoxRun_Publish_command_2() => false;
        public bool FoxRun_PublishAll() => false;
        public bool FoxRun_Apply_incoming() => false;
        public bool FoxRun_ApplyAll() => false;
    }
}";
            var compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var output,
                out _);
            var messages = driver.GetRunResult().Diagnostics
                .Where(diagnostic => diagnostic.Id == "FOXRUN610")
                .Select(diagnostic => diagnostic.GetMessage())
                .ToArray();

            Assert.Contains(messages, message =>
                message.Contains("FoxRun_Publish_command_2", StringComparison.Ordinal));
            Assert.Contains(messages, message =>
                message.Contains("FoxRun_PublishAll", StringComparison.Ordinal));
            Assert.Contains(messages, message =>
                message.Contains("FoxRun_Apply_incoming", StringComparison.Ordinal));
            Assert.Contains(messages, message =>
                message.Contains("FoxRun_ApplyAll", StringComparison.Ordinal));
            Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0111");
        }

        [Fact]
        public void RoslynGeneratorPreservesAggregateInheritedWirePolicyAndFieldNumber()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    [FoxRunMessage(""/phase175/aggregate"")]
    public partial class AggregateState
    {
        [FoxRunField(""count"", ProtobufFieldNumber = 23)]
        private int _count;
    }
}");
            var descriptor = result.Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();

            Assert.Contains("\\\"encoding\\\":\\\"inherit\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains(
                "\\\"protobuf\\\":{\\\"fieldNumber\\\":23",
                descriptor,
                StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorAcceptsNestedDtoForProtobufContract()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public sealed class VehicleTelemetry
    {
        public string Label;
        public Pose Pose;
    }

    public sealed class Pose
    {
        public float X;
        public float Y;
    }

    public partial class WireState
    {
        [FoxRun(""/phase175/dto"", Encoding = FoxRunEncoding.Protobuf)]
        private VehicleTelemetry _telemetry;
    }
}");

            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN006");
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

    }
}
