// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-52 review closure for real-project and R2FU smoke validations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_52Validation
    {
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifyPhase127RuntimeRootAndCleanup();
            VerifySmokeLifecycle("Phase128", "RViz2 Standard Visualization Acceptance/Phase128Rviz2TfLaserScanSmoke.cs");
            VerifySmokeLifecycle("Phase129", "RViz2 PointCloud2 Acceptance/Phase129Rviz2PointCloud2Smoke.cs");
            VerifySmokeLifecycle("Phase130", "RViz2 MarkerArray Acceptance/Phase130Rviz2MarkerArraySmoke.cs");
            VerifySmokeLifecycle("Phase132", "ROS2 Standard Message Expansion/Phase132StandardMessagesSmoke.cs");
            VerifyCameraInfoDistortionContract();
            VerifyRos2SinkThreadSafety();
            VerifyValidationRegistered();

            Console.WriteLine($"Phase 163-52: {_passed} real-project/R2FU smoke checks passed.");
        }

        private static void VerifyPhase127RuntimeRootAndCleanup()
        {
            var source = Read("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase127R2FURealProjectSmoke.cs");
            Check(source.Contains("dev.unity2foxglove.ros2forunity.runtime.", StringComparison.Ordinal)
                  && source.Contains("/runtime/ros2forunity", StringComparison.Ordinal)
                  && !source.Contains("ros2forunity.runtime.jazzy.win64/runtime/ros2forunity", StringComparison.Ordinal),
                "163-52A-1: Phase127 real-project smoke accepts any package R2FU runtime distro");
            Check(source.Contains("private bool _cleanedUp = true;", StringComparison.Ordinal)
                  && source.Contains("if (_cleanedUp)", StringComparison.Ordinal)
                  && source.Contains("WarnCleanupFailure(\"subscription\"", StringComparison.Ordinal),
                "163-52A-2: Phase127 cleanup is idempotent and logs teardown failures");
        }

        private static void VerifySmokeLifecycle(string label, string samplePath)
        {
            var packageSource = Read("Packages/dev.unity2foxglove.ros2forunity/Samples~/" + samplePath);
            var importedSource = Read("Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/" + samplePath);

            Check(HasRunInBackgroundRestore(packageSource) && HasRunInBackgroundRestore(importedSource),
                "163-52B-" + label + "-1: " + label + " restores Application.runInBackground in package and imported sample copies");
            Check(HasSeparatedRetryGates(packageSource) && HasSeparatedRetryGates(importedSource),
                "163-52B-" + label + "-2: " + label + " keeps ready and endpoint retry gates separate");
            Check(packageSource.Contains("TryEnsurePostReadySetup()", StringComparison.Ordinal)
                  && importedSource.Contains("TryEnsurePostReadySetup()", StringComparison.Ordinal),
                "163-52B-" + label + "-3: " + label + " wraps endpoint setup in retryable error handling");
            Check(packageSource.Contains("WarnCleanupFailure", StringComparison.Ordinal)
                  && importedSource.Contains("WarnCleanupFailure", StringComparison.Ordinal),
                "163-52B-" + label + "-4: " + label + " logs cleanup exceptions instead of swallowing them");

            if (label == "Phase129" || label == "Phase130")
            {
                Check(packageSource.Contains("dev.unity2foxglove.ros2forunity.runtime.", StringComparison.Ordinal)
                      && packageSource.Contains("/runtime/ros2forunity", StringComparison.Ordinal)
                      && !packageSource.Contains("ros2forunity.runtime.jazzy.win64/runtime/ros2forunity", StringComparison.Ordinal),
                    "163-52B-" + label + "-5: " + label + " runtime root check is distro-agnostic");
            }
        }

        private static void VerifyCameraInfoDistortionContract()
        {
            var packageSource = Read("Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 Standard Message Expansion/Phase132StandardCameraSource.cs");
            var importedSource = Read("Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/ROS2 Standard Message Expansion/Phase132StandardCameraSource.cs");

            Check(packageSource.Contains("D = new double[5]", StringComparison.Ordinal)
                  && importedSource.Contains("D = new double[5]", StringComparison.Ordinal)
                  && !packageSource.Contains("D = Array.Empty<double>()", StringComparison.Ordinal),
                "163-52C-1: Phase132 CameraInfo publishes five zero plumb_bob distortion coefficients");
        }

        private static void VerifyRos2SinkThreadSafety()
        {
            var sink = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Ros2R2FUTopicSink.cs");
            Check(sink.Contains("private readonly object _gate = new object();", StringComparison.Ordinal)
                  && sink.Contains("lock (_gate)", StringComparison.Ordinal)
                  && sink.Contains("new List<IRos2TopicPublisher>(_publishers.Values)", StringComparison.Ordinal),
                "163-52D-1: Ros2R2FUTopicSink guards publisher and report-once state");
            Check(sink.Contains("IRos2TopicPublisher publisher;", StringComparison.Ordinal)
                  && sink.Contains("publisher.TryPublish(payload, timestampNs, out var error)", StringComparison.Ordinal),
                "163-52D-2: Ros2R2FUTopicSink publishes outside the dictionary lock");
        }

        private static void VerifyValidationRegistered()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase163_52Validation.cs", StringComparison.Ordinal)
                  && registry.Contains("--phase163-52", StringComparison.Ordinal)
                  && registry.Contains("Phase163_52Validation.Validate", StringComparison.Ordinal),
                "163-52E-1: validation registry exposes --phase163-52");
        }

        private static bool HasRunInBackgroundRestore(string source)
            => source.Contains("_previousRunInBackground = Application.runInBackground;", StringComparison.Ordinal)
               && source.Contains("Application.runInBackground = _previousRunInBackground;", StringComparison.Ordinal);

        private static bool HasSeparatedRetryGates(string source)
            => source.Contains("_readyInitializationBlocked", StringComparison.Ordinal)
               && source.Contains("_nextReadyRetryTime", StringComparison.Ordinal)
               && source.Contains("_postReadyInitializationBlocked", StringComparison.Ordinal)
               && source.Contains("_nextPostReadyRetryTime", StringComparison.Ordinal);

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidDataException("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
