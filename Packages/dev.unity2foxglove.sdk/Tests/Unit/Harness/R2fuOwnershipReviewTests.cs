// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Keep native R2FU bridge and sensor ownership fail-closed.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "187")]
    [Trait("Domain", "R2FU ownership")]
    public sealed class R2fuOwnershipReviewTests
    {
        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void SensorParticipantCreationRejectsAnExistingPublisher(string distro)
        {
            var method = Method(
                $"Packages/dev.unity2foxglove.ros2forunity.runtime.{distro}.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs",
                "CreateROSParticipants");
            var publisherCreation = method.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation => invocation.Expression.ToString().Contains("CreateSensorPublisher", StringComparison.Ordinal));
            var ownershipGuard = method.DescendantNodes()
                .OfType<IfStatementSyntax>()
                .FirstOrDefault(statement =>
                    statement.Condition.ToString().Contains("publisher", StringComparison.Ordinal)
                    && statement.Statement.DescendantNodesAndSelf().OfType<ThrowStatementSyntax>().Any());

            Assert.NotNull(ownershipGuard);
            Assert.True(ownershipGuard.SpanStart < publisherCreation.SpanStart);
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void SensorPublishNeverExecutesInsideReadingsLock(string distro)
        {
            var method = Method(
                $"Packages/dev.unity2foxglove.ros2forunity.runtime.{distro}.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs",
                "ExecutorThreadSensorPublishAction");
            var publish = method.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation =>
                    invocation.Expression.ToString().EndsWith(
                        ".Publish",
                        StringComparison.Ordinal));

            Assert.Empty(publish.Ancestors().OfType<LockStatementSyntax>());
        }

        [Fact]
        public void JazzySensorDefersPublisherRemovalUntilPublishReturns()
        {
            const string path =
                "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs";
            var executor = Method(path, "ExecutorThreadSensorPublishAction");
            var dispose = Method(path, "DisposeRosParticipants");
            var completion = Method(path, "CompletePublisherCall");

            Assert.Contains(
                "CompletePublisherCall();",
                executor.ToFullString(),
                StringComparison.Ordinal);
            Assert.NotEmpty(
                executor.DescendantNodes().OfType<FinallyClauseSyntax>());
            Assert.Contains(
                "publisherRetirementPending = true;",
                dispose.ToFullString(),
                StringComparison.Ordinal);
            var remove = completion.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation => invocation.Expression.ToString()
                    .Contains("RemovePublisherSafely", StringComparison.Ordinal));
            Assert.Empty(remove.Ancestors().OfType<LockStatementSyntax>());
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void SensorBufferedReadingCanBeClearedDuringTeardown(string distro)
        {
            var root = CSharpSyntaxTree.ParseText(TestSources.Text(
                    $"Packages/dev.unity2foxglove.ros2forunity.runtime.{distro}.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs"))
                .GetCompilationUnitRoot();
            var sensor = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == "Sensor");
            var constraints = sensor.ConstraintClauses.Single().ToFullString();
            var probe = $@"
namespace ROS2
{{
    public interface MessageWithHeader {{ }}

    public sealed class SensorProbe<T> {constraints}
    {{
        private T readings;

        public void Clear()
        {{
            readings = null;
        }}
    }}
}}";
            var trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            Assert.False(string.IsNullOrEmpty(trusted));
            var compilation = CSharpCompilation.Create(
                $"{distro}-sensor-teardown-probe",
                new[] { CSharpSyntaxTree.ParseText(probe) },
                trusted.Split(Path.PathSeparator)
                    .Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.True(
                errors.Length == 0,
                string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        }

        [Theory]
        [InlineData("Ros2ForUnityCameraNativeBridge.cs")]
        [InlineData("Ros2ForUnityImuNativeBridge.cs")]
        [InlineData("Ros2ForUnityPackedPointCloudBridge.cs")]
        [InlineData("Ros2ForUnityTransformNativeBridge.cs")]
        public void NativeBridgeNeverRebindsToAnUnrelatedRos2Component(string fileName)
        {
            var method = Method(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/" + fileName,
                "TryGetExistingRos2Unity");
            var source = method.ToFullString();

            Assert.DoesNotContain("FindFirstObjectByType", source, StringComparison.Ordinal);
            Assert.Contains("GetComponent<ROS2UnityComponent>()", source, StringComparison.Ordinal);
            Assert.Contains("BeginShutdown()", source, StringComparison.Ordinal);
        }

        private static MethodDeclarationSyntax Method(string path, string name)
        {
            var parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(
                "UNITY2FOXGLOVE_ROS2_FOR_UNITY");
            var root = CSharpSyntaxTree.ParseText(TestSources.Text(path), parseOptions)
                .GetCompilationUnitRoot();
            return root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == name && method.Body != null);
        }
    }
}
