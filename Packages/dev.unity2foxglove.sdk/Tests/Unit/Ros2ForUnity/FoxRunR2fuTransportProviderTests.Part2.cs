// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Phase186A R2FU Provider identity, ownership, and lifecycle contract.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.Ros2ForUnity
{
    public sealed partial class FoxRunR2fuTransportProviderTests
    {
        [Fact]
        public void R2fuAnalyzerPreservesNeutralDeliveryPolicyEnumValues()
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols:
                    new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var source = @"
using Unity.FoxgloveSDK.Components;
namespace vendor_msgs.msg
{
    public sealed class Command : ROS2.Message
    {
        public Command() { }
        public int Value;
    }
}
namespace Demo
{
    public partial class Receiver
    {
        [FoxRun(""/phase186/r2fu/qos"",
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId = ""unity2foxglove.r2fu"",
            Reliability = FoxRunDeliveryReliability.Reliable,
            Durability = FoxRunDeliveryDurability.TransientLocal,
            History = FoxRunDeliveryHistory.KeepLast,
            Depth = 7)]
        private vendor_msgs.msg.Command _incoming;
    }
}";
            var references = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Concat(new[]
                {
                    typeof(FoxRunAttribute).Assembly.Location,
                    typeof(FoxRunRos2TransportProvider).Assembly.Location
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
            var compilation = CSharpCompilation.Create(
                "phase186_r2fu_neutral_qos",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.R2fuOnly(),
                parseOptions: parseOptions);
            var run = driver.RunGenerators(compilation).GetRunResult();
            var errors = run.Diagnostics
                .Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.Empty(errors);
            var generated = Assert.Single(
                    run.Results.SelectMany(result => result.GeneratedSources))
                .SourceText
                .ToString();
            Assert.Contains(
                "FoxRunQosReliability.Reliable",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunQosDurability.TransientLocal",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunQosHistory.KeepLast",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(
                "                7,",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        public void R2fuAnalyzerReportsProviderOwnedShapeDiagnostics()
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols:
                    new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var source = @"
using Unity.FoxgloveSDK.Components;
namespace invalid_msgs.msg
{
    public sealed class Recursive : ROS2.Message
    {
        public Recursive() { }
        public Recursive Next;
    }
}
namespace Demo
{
    public partial class Receiver
    {
        [FoxRun(""/phase186/r2fu/invalid"",
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId = ""unity2foxglove.r2fu"")]
        private invalid_msgs.msg.Recursive _incoming;
    }
}";
            var references = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Concat(new[]
                {
                    typeof(FoxRunAttribute).Assembly.Location,
                    typeof(FoxRunRos2TransportProvider).Assembly.Location
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
            var compilation = CSharpCompilation.Create(
                "phase186_r2fu_diagnostic_ownership",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.R2fuOnly(),
                parseOptions: parseOptions);
            var run = driver.RunGenerators(compilation).GetRunResult();
            var diagnostics = run.Diagnostics
                .Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Id == "FOXR2F006");
            Assert.DoesNotContain(
                diagnostics,
                diagnostic => diagnostic.Id.StartsWith(
                    "FOXRUN",
                    StringComparison.Ordinal));
        }

        [Fact]
        public void RejectedDtoMemberDoesNotReserveItsRosFieldName()
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols:
                    new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var source = @"
using Unity.FoxgloveSDK.Components;
namespace Demo
{
    public sealed class State
    {
        public int Foo { get; }
        public int foo;
    }

    public partial class Receiver
    {
        [FoxRun(""/phase187/r2fu/diagnostics"",
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId = ""unity2foxglove.r2fu"")]
        private State _incoming;
    }
}";
            var references = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Concat(new[]
                {
                    typeof(FoxRunAttribute).Assembly.Location,
                    typeof(FoxRunRos2TransportProvider).Assembly.Location
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
            var compilation = CSharpCompilation.Create(
                "phase187_r2fu_diagnostic_reservation",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.R2fuOnly(),
                parseOptions: parseOptions);
            var run = driver.RunGenerators(compilation).GetRunResult();
            var diagnostics = run.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.Single(diagnostics);
            Assert.Equal("FOXR2F011", diagnostics[0].Id);
            Assert.DoesNotContain(
                "collides",
                diagnostics[0].GetMessage(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(
                        current.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Unity2Foxglove repository root.");
        }
    }
}
#endif
