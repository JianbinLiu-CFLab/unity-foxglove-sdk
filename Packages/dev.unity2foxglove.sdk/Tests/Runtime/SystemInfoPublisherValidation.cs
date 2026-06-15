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

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 145: System Info Publisher ===");
            _passed = 0;

            PublisherComponentUsesExistingPublisherArchitecture();
            PublisherAdvertisesStructuredJsonTelemetry();
            PublisherClampsMinimumInterval();
            SchemaRegistryContainsSystemInfoSchema();
            ValidationRegistryWiresPhase145();

            Console.WriteLine($"Phase 145: {_passed} checks passed.");
        }

        private static void PublisherComponentUsesExistingPublisherArchitecture()
        {
            var source = ReadRepoText(PublisherPath);

            Check(source.Contains("class FoxgloveSystemInfoPublisher", StringComparison.Ordinal),
                "145A-1: System Info publisher component exists with product-facing name");
            Check(source.Contains("FoxglovePublisher<", StringComparison.Ordinal)
                  || source.Contains("FoxglovePublisherBase", StringComparison.Ordinal),
                "145A-2: publisher reuses existing Foxglove publisher architecture");
            Check(source.Contains("_topic = \"/sysinfo\"", StringComparison.Ordinal),
                "145A-3: default topic is /sysinfo");
            Check(source.Contains("SupportsProtobufEncoding => false", StringComparison.Ordinal),
                "145A-4: publisher is not advertised as protobuf-capable");
            Check(source.Contains("SupportsRos2Encoding => false", StringComparison.Ordinal),
                "145A-5: publisher is not advertised as ROS2-capable");
        }

        private static void PublisherAdvertisesStructuredJsonTelemetry()
        {
            var source = ReadRepoText(PublisherPath);

            Check(source.Contains("unity2foxglove.SystemInfo", StringComparison.Ordinal),
                "145B-1: publisher uses explicit unity2foxglove.SystemInfo schema");
            Check(!source.Contains("foxglove.Log", StringComparison.Ordinal),
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
                Check(source.Contains(field, StringComparison.Ordinal),
                    $"145B-field: publisher exposes {field}");
            }
        }

        private static void PublisherClampsMinimumInterval()
        {
            var source = ReadRepoText(PublisherPath);

            Check(source.Contains("MaxPublishRateHz", StringComparison.Ordinal),
                "145C-1: publisher names the maximum allowed publish rate");
            Check(source.Contains("5f", StringComparison.Ordinal),
                "145C-2: publisher clamps to 5 Hz maximum rate / 200 ms minimum interval");
            Check(source.Contains("Mathf.Min", StringComparison.Ordinal)
                  || source.Contains("Math.Min", StringComparison.Ordinal),
                "145C-3: effective rate is clamped before scheduling");
        }

        private static void SchemaRegistryContainsSystemInfoSchema()
        {
            var source = ReadRepoText(SchemaRegistryPath);

            Check(source.Contains("SystemInfoSchemaName", StringComparison.Ordinal),
                "145D-1: schema registry exposes System Info schema constant");
            Check(source.Contains("unity2foxglove.SystemInfo", StringComparison.Ordinal),
                "145D-2: schema registry includes unity2foxglove.SystemInfo");
            Check(source.Contains("frameTimeMs", StringComparison.Ordinal)
                  && source.Contains("fps", StringComparison.Ordinal)
                  && source.Contains("unityVersion", StringComparison.Ordinal),
                "145D-3: schema registry includes key System Info JSON fields");
        }

        private static void ValidationRegistryWiresPhase145()
        {
            var source = ReadRepoText(RegistryPath);

            Check(source.Contains("Ci(\"--phase145\", \"Phase 145\", SystemInfoPublisherValidation.Validate", StringComparison.Ordinal),
                "145E-1: validation registry wires --phase145 to SystemInfoPublisherValidation");
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            Check(File.Exists(path), $"145-file: {relativePath} exists");
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath);

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
