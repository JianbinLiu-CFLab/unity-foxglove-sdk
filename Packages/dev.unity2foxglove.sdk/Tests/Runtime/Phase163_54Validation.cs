// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-54 review follow-up guard for Phase 138 sensor validations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Source-shape validation for Phase 163-54 review fixes.
    /// </summary>
    public static class Phase163_54Validation
    {
        private static int _passed;

        /// <summary>
        /// Validates that Phase 138 sensor validation hardening remains in place.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-54: Phase 138 Sensor Validation Robustness ===");
            _passed = 0;

            VerifyRayValidationCounters();
            VerifyRepoRootAnchoredSourceReads();
            VerifyFloatingPointAndYamlTolerance();
            VerifyImuPropertyExtractionIsBounded();
            VerifySmokeCleanupWarnings();
            VerifyPointCloud2SmokeBuilderExists();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine($"Phase 163-54: {_passed} checks passed.");
        }

        private static void VerifyRayValidationCounters()
        {
            var phase138 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138Validation.cs");
            Check(phase138.Contains("var validatedCount = 0;", StringComparison.Ordinal)
                  && phase138.Contains("validatedCount++;", StringComparison.Ordinal)
                  && phase138.Contains("validatedCount == gen1.RayCount", StringComparison.Ordinal),
                "163-54A-1: Phase138 ray unit-length validation cannot pass without returned rays");

            var phase138b = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138BValidation.cs");
            Check(phase138b.Contains("var validatedDirections = 0;", StringComparison.Ordinal)
                  && phase138b.Contains("validatedDirections == limit", StringComparison.Ordinal)
                  && phase138b.Contains("TryGetRay returned every sampled Mid-360 ray", StringComparison.Ordinal),
                "163-54A-2: Phase138B direction loops assert sampled rays were actually returned");
        }

        private static void VerifyRepoRootAnchoredSourceReads()
        {
            foreach (var relativePath in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138JValidation.cs",
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138KValidation.cs",
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138LValidation.cs",
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138MValidation.cs",
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138QValidation.cs",
            })
            {
                var source = ReadRepoText(relativePath);
                Check(source.Contains("Phase16Validation.FindRepoRoot()", StringComparison.Ordinal)
                      && source.Contains("relativePath.Replace('/', Path.DirectorySeparatorChar)", StringComparison.Ordinal),
                    "163-54B-1: source reads are repository-root anchored in " + Path.GetFileName(relativePath));
            }

            var phase138m = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138MValidation.cs");
            Check(phase138m.Contains("AppDomain.CurrentDomain.GetAssemblies()", StringComparison.Ordinal)
                  && !phase138m.Contains("Type.GetType(typeName + \", FoxgloveSdk.Tests\")", StringComparison.Ordinal),
                "163-54B-2: Phase138M no longer hardcodes the test assembly name for reflection");
        }

        private static void VerifyFloatingPointAndYamlTolerance()
        {
            var phase138f = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138FValidation.cs");
            Check(phase138f.Contains("private const double SampleTimeToleranceSeconds = 1e-9;", StringComparison.Ordinal)
                  && !phase138f.Contains("<= 1e-12", StringComparison.Ordinal),
                "163-54C-1: Phase138F uses a practical floating-point tolerance");

            var phase138i = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138IValidation.cs");
            Check(phase138i.Contains("smokeScene.Contains(\"m_Layer: 2\", StringComparison.Ordinal)", StringComparison.Ordinal)
                  && phase138i.Contains("smokeScene.Contains(\"m_Name: Vehicle\", StringComparison.Ordinal)", StringComparison.Ordinal)
                  && !phase138i.Contains(@"m_Layer:\s*2\s+m_Name:\s*Vehicle", StringComparison.Ordinal),
                "163-54C-2: Phase138I does not depend on Unity YAML field order for vehicle layer checks");
            Check(phase138i.Contains("ReadPointCloudPublisherSources()", StringComparison.Ordinal)
                  && phase138i.Contains("FoxglovePointCloudPublisher*.cs", StringComparison.Ordinal),
                "163-54C-3: Phase138I validates point-cloud publisher partial sources together");
        }

        private static void VerifyImuPropertyExtractionIsBounded()
        {
            var phase138s = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138SValidation.cs");
            Check(phase138s.Contains("var semicolon = source.IndexOf(';', index + signature.Length);", StringComparison.Ordinal)
                  && phase138s.Contains("var brace = source.IndexOf('{', index + signature.Length);", StringComparison.Ordinal)
                  && phase138s.Contains("depth--", StringComparison.Ordinal),
                "163-54D-1: Phase138S ExtractProperty is bounded by expression-bodied or braced property syntax");
        }

        private static void VerifySmokeCleanupWarnings()
        {
            foreach (var relativePath in new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/Virtual LiDAR PointCloud2 Digital Twin/Phase138VirtualLidarPointCloud2Smoke.cs",
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/Virtual LiDAR PointCloud2 Digital Twin/Phase138VirtualLidarPointCloud2Smoke.cs",
            })
            {
                var source = ReadRepoText(relativePath);
                Check(source.Contains("RecordCleanupFailure(\"removing TF publisher\", ex)", StringComparison.Ordinal)
                      && source.Contains("RecordCleanupFailure(\"removing PointCloud2 publisher\", ex)", StringComparison.Ordinal)
                      && source.Contains("RecordCleanupFailure(\"removing ROS2 node\", ex)", StringComparison.Ordinal)
                      && source.Contains("Debug.LogWarning(LogPrefix + \" \" + message)", StringComparison.Ordinal)
                      && !source.Contains("catch (Exception) { }", StringComparison.Ordinal),
                    "163-54E-1: ROS2 sample cleanup failures are logged in " + Path.GetFileName(relativePath));
            }
        }

        private static void VerifyPointCloud2SmokeBuilderExists()
        {
            foreach (var relativePath in new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/Virtual LiDAR PointCloud2 Digital Twin/Phase138CPointCloud2MessageBuilder.cs",
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/Virtual LiDAR PointCloud2 Digital Twin/Phase138CPointCloud2MessageBuilder.cs",
            })
            {
                var source = ReadRepoText(relativePath);
                Check(source.Contains("public static class Phase138CPointCloud2MessageBuilder", StringComparison.Ordinal)
                      && source.Contains("Build(PackedPointCloudFrame frame", StringComparison.Ordinal),
                    "163-54F-1: Phase138C PointCloud2 smoke builder exists: " + Path.GetFileName(relativePath));
            }
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_54Validation.cs", StringComparison.Ordinal)
                  && registry.Contains("Ci(\"--phase163-54\", \"Phase 163-54\", Phase163_54Validation.Validate, includeInDefault: false)", StringComparison.Ordinal),
                "163-54G-1: Phase163-54 validation is compiled and registered");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot()
                ?? throw new InvalidOperationException("Could not find repository root.");
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path);
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
