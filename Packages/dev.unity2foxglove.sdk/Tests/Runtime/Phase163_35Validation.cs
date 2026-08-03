// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-35 validation for demo sensor and manual smoke review boundaries.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_35Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-35: Demo Sensor and Manual Smoke Boundaries ===");
            _passed = 0;

            SensorAssemblyIsAutoReferencedAndPlatformScoped();
            CoordinateConverterFloat3IsPureStatic();
            ExperimentalOpenH264RuntimeScriptsStayEditorFree();
            DemoSensorSourceShapeCoverageExists();
            HotPathAllocationCoverageRemainsFocused();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-35: {_passed} checks passed.");
        }

        private static void SensorAssemblyIsAutoReferencedAndPlatformScoped()
        {
            var asmdef = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Unity.FoxgloveSDK.Sensors.asmdef");

            Check(asmdef.Contains("\"name\": \"Unity.FoxgloveSDK.Sensors\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"Unity.FoxgloveSDK\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"Unity.FoxgloveSDK.Proto\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"autoReferenced\": true", StringComparison.Ordinal)
                  && asmdef.Contains("\"WebGL\"", StringComparison.Ordinal),
                "163-35A: sensor runtime assembly is auto-referenced and excludes WebGL");
        }

        private static void CoordinateConverterFloat3IsPureStatic()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/CoordinateConverterFloat3.cs");
            var syntax = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithPreprocessorSymbols("UNITY_5_3_OR_NEWER"));
            var converter = syntax.GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(candidate => candidate.Identifier.ValueText == "CoordinateConverterFloat3");

            Check(converter != null
                  && source.Contains("public static class CoordinateConverterFloat3", StringComparison.Ordinal)
                  && source.Contains("public static float3 UnityToFoxglovePosition(float3 pos)", StringComparison.Ordinal)
                  && source.Contains("public static float3 FoxgloveToUnityPosition(float3 pos)", StringComparison.Ordinal)
                  && source.Contains("public static float4x4 ToFloat4x4(this Matrix4x4 matrix)", StringComparison.Ordinal)
                  && !source.Contains("[SerializeField]", StringComparison.Ordinal)
                  && !converter.Members.OfType<FieldDeclarationSyntax>().Any(),
                "163-35B: float3 coordinate converter is stateless static math");
        }

        private static void ExperimentalOpenH264RuntimeScriptsStayEditorFree()
        {
            foreach (var script in new[]
            {
                "Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs",
                "Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs"
            })
            {
                var source = ReadRepoText(script);
                Check(!source.Contains("using UnityEditor", StringComparison.Ordinal)
                      && !source.Contains("UnityEditor.", StringComparison.Ordinal),
                    "163-35C: " + script + " has no runtime UnityEditor dependency");
            }

            var tests = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/Architecture/UnityDemoEditorExperimentalTests.cs");
            Check(tests.Contains("OpenH264ProbePublisher.cs", StringComparison.Ordinal)
                  && tests.Contains("OpenH264ProbeSidecar.cs", StringComparison.Ordinal)
                  && tests.Contains("ProbePublisherHidesInternalCaptureCamera", StringComparison.Ordinal)
                  && tests.Contains("ProbeSidecarStopCapturesLifecycleStateAtomically", StringComparison.Ordinal),
                "163-35D: experimental OpenH264 scripts are covered by source architecture tests");
        }

        private static void DemoSensorSourceShapeCoverageExists()
        {
            var tests = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/Architecture/UnityDemoSamplesAssetsTests.cs");

            Check(tests.Contains("MazeDemoBootstrapAndSceneBuilderUseTheSameSensorFieldOverrides", StringComparison.Ordinal)
                  && tests.Contains("MazeDemoOverviewCameraPublisherHasExplicitManager", StringComparison.Ordinal)
                  && tests.Contains("MazeDemoVehicleAutoWanderDoesNotRaycastAgainstItself", StringComparison.Ordinal)
                  && tests.Contains("MazeDemoPrimitiveColoringDoesNotCloneMaterials", StringComparison.Ordinal),
                "163-35E: demo sensor sample source-shape coverage guards the manual smoke component paths");
        }

        private static void HotPathAllocationCoverageRemainsFocused()
        {
            var tests = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/Sensors/PointCloudHotPathAllocationTests.cs");

            Check(tests.Contains("PointCloud2BuilderUsesExactOwnedArray", StringComparison.Ordinal)
                  && tests.Contains("PointCloud2BuilderPacksOnlyValidPointsIntoExactSizedData", StringComparison.Ordinal)
                  && tests.Contains("DracoEncoderUsesPooledXyzWithoutSizingOutputFromRentalLength", StringComparison.Ordinal)
                  && tests.Contains("DoesNotContain(\"stream.ToArray()\"", StringComparison.Ordinal)
                  && tests.Contains("ArrayPool<float>.Shared.Return", StringComparison.Ordinal),
                "163-35F: point-cloud allocation tests cover the encoder and packed-data hot paths");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_35Validation.cs", StringComparison.Ordinal),
                "163-35G: runtime test project compiles Phase163_35Validation");
            Check(registry.Contains("Ci(\"--phase163-35\", \"Phase 163-35: validation for demo sensor and manual smoke review boundaries\", Phase163_35Validation.Validate", StringComparison.Ordinal),
                "163-35H: validation registry exposes --phase163-35");
        }

        private static string ReadRepoText(string relativePath) => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
