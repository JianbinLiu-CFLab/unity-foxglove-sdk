// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Keep native R2FU bridge and sensor ownership fail-closed.

using System;
using System.Linq;
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
