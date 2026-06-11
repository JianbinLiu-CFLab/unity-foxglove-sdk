// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-80 source-shape regression coverage for ROS2/R2FU smoke script optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_80Validation.
    /// </summary>
    public static class Phase140_80Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-80: ROS2/R2FU Smoke and Bridge Scripts Optimization ===");
            _passed = 0;

            VerifyBridgePayloadViewAlreadyAvoidsPayloadCopy();
            VerifyWebSocketProbesAvoidHeaderSlices();
            VerifyRvizWindowPollingCachesWin32Interop();
            VerifyA31FrameCopyOptimizationRemains();
            VerifyDeferredLowValueItemsRemainDeferred();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-80: {_passed} checks passed.");
        }

        private static void VerifyBridgePayloadViewAlreadyAvoidsPayloadCopy()
        {
            var source = Read("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");
            var payload = Slice(source, "PayloadView payload_for_publish", "class BridgeNode");
            Check(source.Contains("struct PayloadView", StringComparison.Ordinal)
                  && payload.Contains("return PayloadView{frame.payload.data(), frame.payload.size()};", StringComparison.Ordinal)
                  && payload.Contains("return PayloadView{frame.payload.data() + 4, frame.payload.size() - 4};", StringComparison.Ordinal)
                  && !payload.Contains("return frame.payload;", StringComparison.Ordinal)
                  && !payload.Contains("std::vector<uint8_t>(frame.payload.begin() + 4", StringComparison.Ordinal),
                "140-80A-1: C++ bridge payload publish path already uses a non-owning PayloadView");
        }

        private static void VerifyWebSocketProbesAvoidHeaderSlices()
        {
            VerifyProbe("Scripts/smoke/topic_rate_probe.py", removePayloadLengthSlice: true);
            VerifyProbe("Scripts/smoke/pointcloud_qos_probe.py", removePayloadLengthSlice: false);
            VerifyProbe("Scripts/smoke/compressed_pointcloud_draco_probe.py", removePayloadLengthSlice: false);
        }

        private static void VerifyProbe(string relativePath, bool removePayloadLengthSlice)
        {
            var source = Read(relativePath);
            Check(source.Contains("struct.unpack_from(\"<I\", frame, SUBSCRIPTION_ID_START)", StringComparison.Ordinal)
                  && source.Contains("struct.unpack_from(\"<Q\", frame, LOG_TIME_START)", StringComparison.Ordinal)
                  && !source.Contains("struct.unpack(\"<I\", frame[SUBSCRIPTION_ID_START:SUBSCRIPTION_ID_END])", StringComparison.Ordinal)
                  && !source.Contains("struct.unpack(\"<Q\", frame[LOG_TIME_START:LOG_TIME_END])", StringComparison.Ordinal),
                "140-80B: " + relativePath + " decodes MessageData headers without slice allocations");
            if (removePayloadLengthSlice)
            {
                Check(source.Contains("total_payload_bytes += max(len(frame) - MESSAGE_PAYLOAD_START, 0)", StringComparison.Ordinal)
                      && !source.Contains("payload = frame[MESSAGE_PAYLOAD_START:]", StringComparison.Ordinal),
                    "140-80B: topic rate probe avoids payload slice when only length is needed");
            }
        }

        private static void VerifyRvizWindowPollingCachesWin32Interop()
        {
            var source = Read("Scripts/smoke/_ros2_windows_env.py");
            var method = Slice(source, "def visible_windows_for_pid", "def launch_rviz");
            Check(source.Contains("_ENUM_WINDOWS_PROC_TYPE = ctypes.WINFUNCTYPE", StringComparison.Ordinal)
                  && source.Contains("_USER32 = ctypes.windll.user32", StringComparison.Ordinal)
                  && method.Contains("if _USER32 is None or _ENUM_WINDOWS_PROC_TYPE is None:", StringComparison.Ordinal)
                  && method.Contains("_USER32.EnumWindows(_ENUM_WINDOWS_PROC_TYPE(callback), 0)", StringComparison.Ordinal)
                  && !method.Contains("ctypes.WINFUNCTYPE", StringComparison.Ordinal)
                  && !method.Contains("ctypes.windll.user32", StringComparison.Ordinal),
                "140-80C-1: RViz visible-window polling reuses cached Win32 interop objects");
        }

        private static void VerifyA31FrameCopyOptimizationRemains()
        {
            var source = Read("Scripts/smoke/phase139_e2e_integration_smoke.py");
            Check(source.Contains("data = frame if isinstance(frame, bytes) else bytes(frame)", StringComparison.Ordinal),
                "140-80D-1: phase139 MessageData collection keeps the A-31 bytes-frame zero-copy path");
        }

        private static void VerifyDeferredLowValueItemsRemainDeferred()
        {
            var bridge = Read("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");
            var topicRate = Read("Scripts/smoke/topic_rate_probe.py");
            Check(bridge.Contains("buffer.assign(count, 0);", StringComparison.Ordinal)
                  && topicRate.Contains("ordered = sorted(values)", StringComparison.Ordinal),
                "140-80E-1: low-value bridge read buffer and percentile sort changes remain deferred");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_80Validation.cs", StringComparison.Ordinal),
                "140-80F-1: test project compiles Phase140_80Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-80\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_80Validation.Validate", StringComparison.Ordinal),
                "140-80F-2: validation registry exposes --phase140-80");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static string Slice(string source, string startText, string endText)
        {
            var start = source.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Could not locate source slice start: " + startText);
            var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;
            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
