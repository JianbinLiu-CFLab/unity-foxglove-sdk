// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 145 validation for the structured System Info publisher.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class SystemInfoPublisherValidation
    {
        private const string PublisherPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxgloveSystemInfoPublisher.cs";
        private const string SchemaRegistryPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Registry/FoxgloveSchemaDefinitions.cs";
        private const string RegistryPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs";

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 145: System Info Publisher ===");
            var checks = new CheckCounter();

            PublisherComponentUsesExistingPublisherArchitecture(checks);
            PublisherAdvertisesStructuredJsonTelemetry(checks);
            PublisherClampsMinimumInterval(checks);
            SchemaRegistryContainsSystemInfoSchema(checks);
            ValidationRegistryWiresPhase145(checks);

            Console.WriteLine($"Phase 145: {checks.Passed} checks passed.");
        }

        private static void PublisherComponentUsesExistingPublisherArchitecture(CheckCounter checks)
        {
            var source = ReadRepoText(PublisherPath, checks);

            checks.Check(source.Contains("class FoxgloveSystemInfoPublisher", StringComparison.Ordinal),
                "145A-1: System Info publisher component exists with product-facing name");
            checks.Check(source.Contains("FoxglovePublisher<", StringComparison.Ordinal)
                  || source.Contains("FoxglovePublisherBase", StringComparison.Ordinal),
                "145A-2: publisher reuses existing Foxglove publisher architecture");
            checks.Check(source.Contains("_topic = \"/sysinfo\"", StringComparison.Ordinal),
                "145A-3: default topic is /sysinfo");
            checks.Check(source.Contains("SupportsProtobufEncoding => false", StringComparison.Ordinal),
                "145A-4: publisher is not advertised as protobuf-capable");
            checks.Check(source.Contains("SupportsRos2Encoding => false", StringComparison.Ordinal),
                "145A-5: publisher is not advertised as ROS2-capable");
            checks.Check(source.Contains("protected override void OnValidate()", StringComparison.Ordinal)
                  && source.Contains("base.OnValidate();", StringComparison.Ordinal),
                "145A-6: publisher validates through the base publisher OnValidate override");
        }

        private static void PublisherAdvertisesStructuredJsonTelemetry(CheckCounter checks)
        {
            var source = ReadRepoText(PublisherPath, checks);

            checks.Check(source.Contains("unity2foxglove.SystemInfo", StringComparison.Ordinal),
                "145B-1: publisher uses explicit unity2foxglove.SystemInfo schema");
            checks.Check(!source.Contains("foxglove.Log", StringComparison.Ordinal),
                "145B-2: publisher does not misuse foxglove.Log for structured metrics");

            foreach (var field in new[]
            {
                "timestamp",
                "frameTimeMs",
                "fps",
                "gcMemoryMB",
                "monoUsedMemoryMB",
                "totalAllocatedMemoryMB",
                "totalReservedMemoryMB",
                "systemMemorySizeMB",
                "processorCount",
                "processorType",
                "graphicsDeviceName",
                "graphicsMemorySizeMB",
                "platform",
                "unityVersion"
            })
            {
                checks.Check(source.Contains(field, StringComparison.Ordinal),
                    $"145B-field: publisher exposes {field}");
            }
        }

        private static void PublisherClampsMinimumInterval(CheckCounter checks)
        {
            var source = ReadRepoText(PublisherPath, checks);

            checks.Check(source.Contains("MaxPublishRateHz", StringComparison.Ordinal),
                "145C-1: publisher names the maximum allowed publish rate");
            checks.Check(source.Contains("private const float MaxPublishRateHz = 5f;", StringComparison.Ordinal),
                "145C-2: publisher names the 5 Hz maximum rate / 200 ms minimum interval");
            checks.Check(source.Contains("Mathf.Min", StringComparison.Ordinal)
                  || source.Contains("Math.Min", StringComparison.Ordinal),
                "145C-3: effective rate is clamped before scheduling");
        }

        private static void SchemaRegistryContainsSystemInfoSchema(CheckCounter checks)
        {
            var source = ReadRepoText(SchemaRegistryPath, checks);

            checks.Check(source.Contains("SystemInfoSchemaName", StringComparison.Ordinal),
                "145D-1: schema registry exposes System Info schema constant");
            checks.Check(source.Contains("unity2foxglove.SystemInfo", StringComparison.Ordinal),
                "145D-2: schema registry includes unity2foxglove.SystemInfo");
            checks.Check(source.Contains("frameTimeMs", StringComparison.Ordinal)
                  && source.Contains("fps", StringComparison.Ordinal)
                  && source.Contains("unityVersion", StringComparison.Ordinal),
                "145D-3: schema registry includes key System Info JSON fields");
        }

        private static void ValidationRegistryWiresPhase145(CheckCounter checks)
        {
            var source = ReadRepoText(RegistryPath, checks);

            checks.Check(source.Contains("Ci(\"--phase145\", \"Phase 145: validation for the structured System Info publisher\", SystemInfoPublisherValidation.Validate", StringComparison.Ordinal),
                "145E-1: validation registry wires --phase145 to SystemInfoPublisherValidation");
        }

        private static string ReadRepoText(string relativePath, CheckCounter checks)
        {
            var path = RepoPath(relativePath);
            checks.Check(File.Exists(path), $"145-file: {relativePath} exists");
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot()
                ?? throw new InvalidOperationException("Could not find repository root from " + AppContext.BaseDirectory);
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private sealed class CheckCounter
        {
            public int Passed { get; private set; }

            public void Check(bool condition, string message)
            {
                if (!condition)
                    throw new Exception("[FAIL] " + message);
                Passed++;
                Console.WriteLine("[PASS] " + message);
            }
        }
    }
}
