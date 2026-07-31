// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-28 validation for R2FU native bridge lifecycle hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_28Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-28: R2FU Native Bridge Lifecycle ===");
            _passed = 0;

            NativeBridgeShutdownFlagsAreVolatile();
            NativeBridgeShutdownIsIdempotent();
            NativeRuntimeFailuresAreMutedDuringShutdown();
            CameraPublisherRetryLoopsAbortDuringShutdown();
            CameraMessageBuilderToleratesNullCollections();
            R2fuProviderRejectsUntypedPayloads();
            NativeCallbackThreadAssumptionsRemainAuditable();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-28: {_passed} checks passed.");
        }

        private static void NativeBridgeShutdownFlagsAreVolatile()
        {
            var lifecycle = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityNativeBridgeLifecycleGate.cs");
            foreach (var path in NativeBridgePaths())
            {
                var source = ReadRepoText(path);
                Check(lifecycle.Contains("private static volatile bool _applicationQuitting;", StringComparison.Ordinal)
                      && lifecycle.Contains("private static volatile bool _nativeReloadWindow;", StringComparison.Ordinal)
                      && lifecycle.Contains("private static volatile bool _isStablePlayModeScene;", StringComparison.Ordinal)
                      && lifecycle.Contains("private static volatile bool _editorEnteredPlayMode;", StringComparison.Ordinal)
                      && lifecycle.Contains("private static volatile bool _editorQuitting;", StringComparison.Ordinal)
                      && source.Contains("Ros2ForUnityNativeBridgeLifecycleGate", StringComparison.Ordinal),
                    "163-28A: " + Path.GetFileName(path) + " delegates volatile editor/runtime shutdown state to the shared lifecycle gate");
            }
        }

        private static void NativeBridgeShutdownIsIdempotent()
        {
            foreach (var path in NativeBridgePaths())
            {
                var beginShutdown = ExtractMethod(ReadRepoText(path), "BeginShutdown");
                Check(beginShutdown.Contains("if (_isStopping)", StringComparison.Ordinal)
                      && beginShutdown.Contains("return;", StringComparison.Ordinal)
                      && beginShutdown.IndexOf("if (_isStopping)", StringComparison.Ordinal)
                         < beginShutdown.IndexOf("_isStopping = true;", StringComparison.Ordinal),
                    "163-28B: " + Path.GetFileName(path) + " BeginShutdown is idempotent before clearing bindings");
            }
        }

        private static void NativeRuntimeFailuresAreMutedDuringShutdown()
        {
            var imu = ExtractMethod(
                ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs"),
                "EnsureRos2UnityReady");
            var camera = ExtractMethod(
                ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs"),
                "EnsureRos2UnityReady");

            Check(Count(imu, "if (!IsShuttingDown)") >= 2
                  && Count(imu, "RecordRos2Failure(") == 2,
                "163-28C-1: IMU native bridge does not log runtime-not-ready warnings during shutdown");
            Check(Count(camera, "if (!IsShuttingDown)") >= 2
                  && Count(camera, "RecordRos2Failure(") == 2,
                "163-28C-2: Camera native bridge matches shutdown-muted runtime failure logging");
        }

        private static void CameraPublisherRetryLoopsAbortDuringShutdown()
        {
            foreach (var path in new[]
                     {
                         "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraCompressedImageBinding.cs",
                         "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraRawImageBinding.cs",
                         "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraInfoBinding.cs"
                     })
            {
                var source = ReadRepoText(path);
                Check(source.Contains("CleanupRos2();", StringComparison.Ordinal)
                      && source.Contains("if (Owner.IsShuttingDown)", StringComparison.Ordinal)
                      && source.Contains("return false;", StringComparison.Ordinal)
                      && source.IndexOf("CleanupRos2();", StringComparison.Ordinal)
                         < source.IndexOf("if (Owner.IsShuttingDown)", StringComparison.Ordinal),
                    "163-28D: " + Path.GetFileName(path) + " stops retrying publisher creation once shutdown starts");
            }
        }

        private static void CameraMessageBuilderToleratesNullCollections()
        {
            var builder = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraMessageBuilder.cs");
            Check(builder.Contains("if (values == null)", StringComparison.Ordinal)
                  && builder.Contains("return Array.Empty<double>();", StringComparison.Ordinal),
                "163-28E-1: CameraInfo distortion collection copy handles null as an empty ROS2 array");
            Check(builder.Contains("if (source == null || destination == null)", StringComparison.Ordinal),
                "163-28E-2: CameraInfo matrix copy skips null source or destination arrays without throwing");
        }

        private static void R2fuProviderRejectsUntypedPayloads()
        {
            var provider = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2TransportProvider.cs");
            var binding = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomPublisherBinding.cs");
            Check(provider.Contains(
                      "R2FU routes are emitted as generated typed ROS2 bindings, not untyped byte payloads.",
                      StringComparison.Ordinal)
                  && provider.Contains("FoxRunTransportPublishResult.Rejected", StringComparison.Ordinal)
                  && binding.Contains("if (ReferenceEquals(mapped, null))", StringComparison.Ordinal)
                  && binding.Contains("return false;", StringComparison.Ordinal)
                  && !binding.Contains("payload ?? Array.Empty<byte>()", StringComparison.Ordinal),
                "163-28F: the R2FU Provider rejects untyped payloads and typed bindings skip null mapped messages");
        }

        private static void NativeCallbackThreadAssumptionsRemainAuditable()
        {
            var imu = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var pointCloud = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.PackedPointCloud.cs");

            Check(ExtractMethod(imu, "Update").Contains("nativeFrameHandler.Invoke(nativeFrame)", StringComparison.Ordinal),
                "163-28G-1: VirtualImu native frame handoff remains on the Update drain path");
            Check(ExtractMethod(pointCloud, "PublishCompletedPackedPointCloudPayload").Contains("PublishPackedPointCloudFrameReady", StringComparison.Ordinal),
                "163-28G-2: PointCloud2 native frame handoff remains explicit after worker payload completion");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_28Validation.cs", StringComparison.Ordinal),
                "163-28H-1: runtime test project compiles Phase163_28Validation");
            Check(registry.Contains("--phase163-28", StringComparison.Ordinal)
                  && registry.Contains("Phase163_28Validation.Validate", StringComparison.Ordinal),
                "163-28H-2: validation registry exposes --phase163-28");
        }

        private static string[] NativeBridgePaths()
            => new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs"
            };

        private static string ExtractMethod(string source, string methodName)
        {
            var signature = -1;
            foreach (var prefix in new[] { "private void ", "private bool ", "private static void ", "public void ", "public bool " })
            {
                signature = source.IndexOf(prefix + methodName + "(", StringComparison.Ordinal);
                if (signature >= 0)
                    break;
            }

            if (signature < 0)
                return string.Empty;

            while (signature > 0 && source[signature - 1] != '\n' && source[signature - 1] != '\r')
                signature--;

            var bodyStart = source.IndexOf('{', signature);
            if (bodyStart < 0)
                return string.Empty;

            var depth = 0;
            for (var i = bodyStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(bodyStart, i - bodyStart + 1);
                }
            }

            return source.Substring(bodyStart);
        }

        private static int Count(string source, string value)
        {
            var count = 0;
            var index = source.IndexOf(value, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal);
            }

            return count;
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
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
