// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-39 ROS2 bridge sidecar and launch tooling review closure.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_39Validation
    {
        public static void Validate()
        {
            var repoRoot = Phase16Validation.FindRepoRoot()
                           ?? throw new DirectoryNotFoundException("Could not locate repository root.");

            VerifySidecarProtocol(repoRoot);
            VerifySidecarTestTarget(repoRoot);
            VerifyLaunchAndPackaging(repoRoot);
            VerifyDocumentation(repoRoot);
            VerifyWiring(repoRoot);

            Console.WriteLine("Phase 163-39: ROS2 bridge sidecar checks passed.");
        }

        private static void VerifySidecarProtocol(string repoRoot)
        {
            var source = Read(repoRoot, "Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");

            Check(source.Contains("class ClientClosedException", StringComparison.Ordinal)
                  && source.Contains("class ClientReadTimeoutException", StringComparison.Ordinal),
                "163-39A-1: sidecar uses typed client disconnect and read-timeout exceptions");
            Check(source.Contains("constexpr auto kReadStallTimeout = std::chrono::seconds(5);", StringComparison.Ordinal)
                  && source.Contains("now - stalled_since >= kReadStallTimeout", StringComparison.Ordinal),
                "163-39A-2: read_exact closes stalled mid-frame clients after a bounded timeout");
            Check(source.Contains("PayloadView payload_for_publish(", StringComparison.Ordinal)
                  && source.Contains("std::vector<uint8_t> & scratch", StringComparison.Ordinal)
                  && source.Contains("scratch.assign(std::begin(kCdrLittleEndianHeader), std::end(kCdrLittleEndianHeader));", StringComparison.Ordinal)
                  && source.Contains("scratch.insert(scratch.end(), frame.payload.begin(), frame.payload.end());", StringComparison.Ordinal)
                  && source.Contains("return PayloadView{scratch.data(), scratch.size()};", StringComparison.Ordinal),
                "163-39A-3: cdr-body-only prepends encapsulation before ROS2 publish");
            Check(!source.Contains("return PayloadView{frame.payload.data() + 4, frame.payload.size() - 4};", StringComparison.Ordinal)
                  && source.Contains("cdr-body-only expects payload without CDR encapsulation header", StringComparison.Ordinal),
                "163-39A-4: sidecar no longer strips encapsulation before serialized publish");
            Check(source.Contains("SO_REUSEADDR failed, rapid restart may fail", StringComparison.Ordinal),
                "163-39A-5: sidecar warns if address reuse setup fails");
            Check(source.Contains("reused with different schemaName or QoS: was [", StringComparison.Ordinal)
                  && source.Contains("] got [", StringComparison.Ordinal),
                "163-39A-6: topic QoS mismatch diagnostics include old and new signatures");
            Check(source.Contains("#ifndef UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING", StringComparison.Ordinal),
                "163-39A-7: sidecar source can be included by protocol unit tests without main");
        }

        private static void VerifySidecarTestTarget(string repoRoot)
        {
            var test = Read(repoRoot, "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/test_bridge_smoke.cpp");
            var cmake = Read(repoRoot, "Tools/ros2_bridge/unity2foxglove_ros2_bridge/CMakeLists.txt");

            Check(test.Contains("#include \"../src/unity2foxglove_ros2_bridge.cpp\"", StringComparison.Ordinal)
                  && test.Contains("Include the production translation unit directly", StringComparison.Ordinal)
                  && test.Contains("UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING", StringComparison.Ordinal),
                "163-39B-1: gtest target includes sidecar logic under the testing guard");
            Check(test.Contains("PrependsEncapsulationForBodyOnlyPayload", StringComparison.Ordinal)
                  && test.Contains("RejectsEncapsulatedBodyOnlyPayload", StringComparison.Ordinal)
                  && test.Contains("RejectsNonFoxgloveSchemas", StringComparison.Ordinal)
                  && test.Contains("QoSSignatureCapturesSchemaAndProfile", StringComparison.Ordinal)
                  && !test.Contains("SUCCEED();", StringComparison.Ordinal),
                "163-39B-2: sidecar gtest covers protocol behavior rather than only build smoke");
            Check(cmake.Contains("ament_target_dependencies(test_bridge_smoke", StringComparison.Ordinal)
                  && cmake.Contains("target_link_libraries(test_bridge_smoke", StringComparison.Ordinal)
                  && cmake.Contains("nlohmann_json::nlohmann_json", StringComparison.Ordinal),
                "163-39B-3: sidecar gtest links the same ROS2/json dependencies as product code");
        }

        private static void VerifyLaunchAndPackaging(string repoRoot)
        {
            var launch = Read(repoRoot, "Tools/ros2_bridge/unity2foxglove_ros2_bridge/launch/unity2foxglove_bridge.launch.py");
            var packageXml = Read(repoRoot, "Tools/ros2_bridge/unity2foxglove_ros2_bridge/package.xml");

            Check(launch.Contains("choices=[\"cdr-with-encapsulation\", \"cdr-body-only\"]", StringComparison.Ordinal),
                "163-39C-1: launch argument validates payload_format choices");
            Check(packageXml.Contains("<build_depend>nlohmann_json</build_depend>", StringComparison.Ordinal)
                  && !packageXml.Contains("<depend>nlohmann_json</depend>", StringComparison.Ordinal),
                "163-39C-2: header-only nlohmann_json is declared as a build dependency");
        }

        private static void VerifyDocumentation(string repoRoot)
        {
            var readme = Read(repoRoot, "Tools/ros2_bridge/unity2foxglove_ros2_bridge/README.md");

            Check(readme.Contains("publishes only `foxglove_msgs/msg/*` schemas", StringComparison.Ordinal)
                  && readme.Contains("dev.unity2foxglove.ros2forunity", StringComparison.Ordinal),
                "163-39D-1: README documents the foxglove_msgs-only bridge boundary");
            Check(readme.Contains("malformed frame", StringComparison.Ordinal)
                  && readme.Contains("closes the client connection", StringComparison.Ordinal)
                  && readme.Contains("cannot safely resynchronize", StringComparison.Ordinal),
                "163-39D-2: README documents raw stream resynchronization limits");
            Check(readme.Contains("sidecar prepends the little-endian encapsulation header", StringComparison.Ordinal)
                  && readme.Contains("Do not use `cdr-body-only` for normal Unity2Foxglove payloads", StringComparison.Ordinal),
                "163-39D-3: README documents body-only payload semantics");
        }

        private static void VerifyWiring(string repoRoot)
        {
            var project = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_39Validation.cs", StringComparison.Ordinal),
                "163-39E-1: runtime test project compiles Phase163_39Validation");
            Check(registry.Contains("Ci(\"--phase163-39\", \"Phase 163-39: phase163-39 ROS2 bridge sidecar and launch tooling review closure\", Phase163_39Validation.Validate", StringComparison.Ordinal),
                "163-39E-2: validation registry exposes --phase163-39");
        }

        private static string Read(string repoRoot, string relativePath)
            => File.ReadAllText(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception("[FAIL] " + description);

            Console.WriteLine("[PASS] " + description);
        }
    }
}
