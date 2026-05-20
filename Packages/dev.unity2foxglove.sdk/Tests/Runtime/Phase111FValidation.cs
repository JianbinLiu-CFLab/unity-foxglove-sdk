// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 111F R2FU runtime lifecycle and review-fix validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase111FValidation
    {
        private const string RuntimeScripts =
            "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts";
        private const string SamplePath =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 For Unity External Adapter";
        private const string ImportedSamplePath =
            "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/ROS2 For Unity External Adapter";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 111F: R2FU Runtime And Lifecycle Review Fixes ===");
            _passed = 0;

            VerifyEncoderStopHygiene();
            VerifyBridgeLifecycle();
            VerifyAdapterLifecycle();
            VerifyManualAcceptanceLifecycle();
            VerifyVendoredRuntimeLifecycle();
            VerifyRuntimeGeneratorAndValidation();
            VerifyBuildOrchestratorCleanup();
            VerifyImportedSampleSync();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 111F: {_passed} checks passed.");
        }

        private static void VerifyEncoderStopHygiene()
        {
            foreach (var relativePath in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs",
            })
            {
                var source = ReadRepoText(relativePath);
                Check(source.Contains("Stop(clearOutputQueue: true)", StringComparison.Ordinal)
                      && source.Contains("Stop(clearOutputQueue: false)", StringComparison.Ordinal),
                    "111F-A1: encoder separates hard stop from publisher tail disposal: " + relativePath);
            Check(source.Contains("DrainOutputQueue()", StringComparison.Ordinal)
                  && (source.Contains("_outputCount = 0", StringComparison.Ordinal)
                      || source.Contains("Volatile.Write(ref _outputCount, 0)", StringComparison.Ordinal)
                      || source.Contains("Interlocked.Exchange(ref _outputCount, 0)", StringComparison.Ordinal)),
                "111F-A2: encoder hard stop clears output queue/count: " + relativePath);
            }
        }

        private static void VerifyBridgeLifecycle()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeRuntime.cs");
            Check(source.Contains("auto-connect is disabled", StringComparison.Ordinal)
                  && source.Contains("return false", StringComparison.Ordinal),
                "111F-B1: Ros2Bridge rejects sends when auto-connect is disabled");
            Check(source.Contains("CloseSink(sinkToClose)", StringComparison.Ordinal)
                  && source.Contains("worker.Join(joinTimeoutMs)", StringComparison.Ordinal)
                  && source.Contains("Math.Max(1000, _sendTimeoutMs + 250)", StringComparison.Ordinal),
                "111F-B2: Ros2Bridge closes sink before bounded worker join");
            Check(source.Contains("countFrameFailure: false", StringComparison.Ordinal),
                "111F-B3: Ros2Bridge connection failures do not inflate frame failures");
            Check(source.Contains("IRos2BridgeSink sink;", StringComparison.Ordinal)
                  && source.Contains("sink = _sink;", StringComparison.Ordinal)
                  && source.Contains("sink.Send(frame, _sendTimeoutMs)", StringComparison.Ordinal),
                "111F-B4: Ros2Bridge worker snapshots sink before send");
        }

        private static void VerifyAdapterLifecycle()
        {
            var context = ReadRepoText(SamplePath + "/Phase110Ros2ForUnityContext.cs");
            var smoke = ReadRepoText(SamplePath + "/Phase110Ros2ForUnityStringSmoke.cs");
            Check(context.Contains("MaxPendingCallbacks", StringComparison.Ordinal)
                  && context.Contains("while (_pending.Count >= MaxPendingCallbacks)", StringComparison.Ordinal)
                  && context.Contains("_droppedCallbacks", StringComparison.Ordinal),
                "111F-C1: Phase110 adapter subscription queue is bounded");
            Check(context.Contains("lock (_gate)", StringComparison.Ordinal)
                  && context.Contains("_disposed = true", StringComparison.Ordinal)
                  && context.Contains("RemoveSubscription<std_msgs.msg.String>", StringComparison.Ordinal)
                  && context.Contains("RemovePublisher<std_msgs.msg.String>", StringComparison.Ordinal),
                "111F-C2: Phase110 adapter disposes native endpoints deterministically");
            Check(context.Contains("_ownsRos2UnityComponent", StringComparison.Ordinal)
                  && context.Contains("UnityEngine.Object.Destroy(_ros2Unity)", StringComparison.Ordinal),
                "111F-C3: Phase110 adapter destroys only auto-created ROS2UnityComponent");
            Check(smoke.Contains("QueueDirectStringReceived", StringComparison.Ordinal)
                  && smoke.Contains("DrainDirectReceived", StringComparison.Ordinal)
                  && smoke.Contains("RecordReceived", StringComparison.Ordinal)
                  && smoke.IndexOf("DrainDirectReceived", StringComparison.Ordinal)
                     < smoke.IndexOf("PublishIfDue", StringComparison.Ordinal),
                "111F-C4: Phase110 direct callbacks are drained on the Unity update thread");
            Check(smoke.Contains("InspectorName(\"Use Direct Runtime\")", StringComparison.Ordinal),
                "111F-C5: Phase110 inspector label is product-facing");
        }

        private static void VerifyManualAcceptanceLifecycle()
        {
            var phase106 = ReadRepoText("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase106Ros2ForUnityAcceptance.cs");
            Check(phase106.Contains("OnDisable()", StringComparison.Ordinal)
                  && phase106.Contains("OnDestroy()", StringComparison.Ordinal)
                  && phase106.Contains("DisposeRos2Endpoints", StringComparison.Ordinal)
                  && phase106.Contains("RemoveSubscription<std_msgs.msg.String>", StringComparison.Ordinal)
                  && phase106.Contains("RemovePublisher<std_msgs.msg.String>", StringComparison.Ordinal)
                  && phase106.Contains("_ownsRos2UnityComponent", StringComparison.Ordinal),
                "111F-D1: Phase106 manual acceptance tears down ROS2 resources");

            var phase109 = ReadRepoText("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase109Ros2ForUnityContext.cs");
            Check(phase109.Contains("MaxPendingCallbacks", StringComparison.Ordinal)
                  && phase109.Contains("RemovePublisher<std_msgs.msg.String>", StringComparison.Ordinal)
                  && phase109.Contains("RemoveSubscription<std_msgs.msg.String>", StringComparison.Ordinal)
                  && phase109.Contains("_ownsRos2UnityComponent", StringComparison.Ordinal),
                "111F-D2: Phase109 manual acceptance has bounded queues and native teardown");
        }

        private static void VerifyVendoredRuntimeLifecycle()
        {
            var runtime = ReadRepoText(RuntimeScripts + "/ROS2ForUnity.cs");
            Check(runtime.Contains("#if UNITY_EDITOR", StringComparison.Ordinal)
                  && runtime.Contains("using UnityEditor;", StringComparison.Ordinal)
                  && runtime.Contains("PackageInfo.FindForAssetPath", StringComparison.Ordinal),
                "111F-E1: ROS2ForUnity keeps Editor-only package lookup guarded");
            Check(!runtime.Contains("~ROS2ForUnity", StringComparison.Ordinal)
                  && runtime.Contains("ownerCount", StringComparison.Ordinal)
                  && runtime.Contains("UnregisterCallbacks()", StringComparison.Ordinal),
                "111F-E2: ROS2ForUnity uses deterministic ownership instead of finalizer shutdown");

            var node = ReadRepoText(RuntimeScripts + "/ROS2Node.cs");
            Check(node.Contains("class ROS2Node : IDisposable", StringComparison.Ordinal)
                  && !node.Contains("~ROS2Node", StringComparison.Ordinal)
                  && node.Contains("Ros2cs.RemoveNode", StringComparison.Ordinal),
                "111F-E3: ROS2Node has deterministic Dispose and no native finalizer cleanup");

            var component = ReadRepoText(RuntimeScripts + "/ROS2UnityComponent.cs");
            Check(component.Contains("private volatile bool quitting", StringComparison.Ordinal)
                  && component.Contains("private Thread spinThread", StringComparison.Ordinal)
                  && component.Contains("OnDestroy()", StringComparison.Ordinal)
                  && !component.Contains("OnDisable()", StringComparison.Ordinal)
                  && component.Contains("threadToJoin.Join(1000)", StringComparison.Ordinal)
                  && component.Contains("node.Dispose()", StringComparison.Ordinal),
                "111F-E4: ROS2UnityComponent stops, joins, and disposes nodes deterministically");

            var core = ReadRepoText(RuntimeScripts + "/ROS2UnityCore.cs");
            Check(core.Contains("IDisposable", StringComparison.Ordinal)
                  && core.Contains("private volatile bool quitting", StringComparison.Ordinal)
                  && core.Contains("threadToJoin.Join(1000)", StringComparison.Ordinal),
                "111F-E5: ROS2UnityCore has deterministic shutdown");

            var dotnetTime = ReadRepoText(RuntimeScripts + "/Time/DotnetTimeSource.cs");
            Check(dotnetTime.Contains("/ Stopwatch.Frequency", StringComparison.Ordinal),
                "111F-E6: DotnetTimeSource converts Stopwatch ticks to seconds");

            var timeUtils = ReadRepoText(RuntimeScripts + "/Time/TimeUtils.cs");
            Check(timeUtils.Contains("Math.Floor(secondsIn)", StringComparison.Ordinal)
                  && timeUtils.Contains("normalizedNanoseconds < 0", StringComparison.Ordinal)
                  && !timeUtils.Contains("(uint)(nanosec %", StringComparison.Ordinal),
                "111F-E7: TimeUtils normalizes negative nanoseconds before uint conversion");

            var sensor = ReadRepoText(RuntimeScripts + "/Sensor.cs");
            Check(sensor.Contains("publisher != null && publishing", StringComparison.Ordinal)
                  && sensor.Contains("UnregisterExecutable", StringComparison.Ordinal)
                  && sensor.Contains("if (readings != null)", StringComparison.Ordinal)
                  && !sensor.Contains("clockSubscriber", StringComparison.Ordinal),
                "111F-E8: Sensor null/lifecycle hazards are patched");

            foreach (var relative in PatchedVendorFiles())
            {
                var source = ReadRepoText(RuntimeScripts + "/" + relative);
                Check(source.Contains("Modifications Copyright (c) 2026 Jianbin Liu", StringComparison.Ordinal),
                    "111F-E9: modified vendored file carries modifications copyright: " + relative);
            }

            foreach (var example in new[]
            {
                "ROS2TalkerExample.cs",
                "ROS2ListenerExample.cs",
                "ROS2ClientExample.cs",
                "ROS2ServiceExample.cs",
                "ROS2PerformanceTest.cs",
                "PostInstall.cs"
            })
            {
                Check(!RepoFileExists(RuntimeScripts + "/" + example),
                    "111F-E10: leaky upstream example is not shipped: " + example);
            }
        }

        private static void VerifyRuntimeGeneratorAndValidation()
        {
            var generator = ReadRepoText("Scripts/release/build_r2fu_runtime_package.py");
            foreach (var token in new[]
            {
                "collect_local_patch_overlays",
                "apply_local_patch_overlays",
                "LEAKY_UPSTREAM_EXAMPLES",
                "runtime_asmdef",
                "make_writable",
                "PackageInfo.FindForAssetPath",
                "UNITY_EDITOR"
            })
            {
                Check(generator.Contains(token, StringComparison.Ordinal),
                    "111F-F1: runtime generator preserves package hardening token: " + token);
            }

            var validator = ReadRepoText("Scripts/release/validate_r2fu_runtime_package.py");
            Check(validator.Contains("check_runtime_source_patches", StringComparison.Ordinal)
                  && validator.Contains("check_generator_alignment", StringComparison.Ordinal)
                  && validator.Contains("Stopwatch.Frequency", StringComparison.Ordinal)
                  && validator.Contains("leaky upstream examples pruned", StringComparison.Ordinal),
                "111F-F2: runtime package validator covers source patches and generator drift");
        }

        private static void VerifyBuildOrchestratorCleanup()
        {
            var script = ReadRepoText("Scripts/smoke/phase137b_r2fu_jazzy_windows_build.py");
            Check(script.Contains("kill_process_tree_windows", StringComparison.Ordinal)
                  && script.Contains("taskkill", StringComparison.Ordinal)
                  && script.Contains("timed_out", StringComparison.Ordinal)
                  && script.Contains("exit_code = 124 if timed_out", StringComparison.Ordinal),
                "111F-G1: build orchestrator kills process trees and reports timeout as 124");
            Check(script.Contains("is_contaminating_python_path", StringComparison.Ordinal)
                  && script.Contains("miniconda", StringComparison.Ordinal)
                  && script.Contains("probe.unlink()", StringComparison.Ordinal),
                "111F-G2: build orchestrator filters Python contamination and cleans CL probes");
        }

        private static void VerifyImportedSampleSync()
        {
            foreach (var fileName in new[] { "Phase110Ros2ForUnityContext.cs", "Phase110Ros2ForUnityStringSmoke.cs" })
            {
                var packageSample = Normalize(ReadRepoText(SamplePath + "/" + fileName));
                var importedSample = Normalize(ReadRepoText(ImportedSamplePath + "/" + fileName));
                Check(packageSample == importedSample,
                    "111F-H1: imported Unity sample matches package sample: " + fileName);
            }
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(program.Contains("--phase111f", StringComparison.Ordinal)
                  && program.Contains("RunPhase111FOnly", StringComparison.Ordinal)
                  && program.Contains("Phase111FValidation.Validate()", StringComparison.Ordinal),
                "111F-I1: Program.cs wires --phase111f");
            Check(project.Contains("Phase111FValidation.cs", StringComparison.Ordinal),
                "111F-I2: test project compiles Phase111FValidation");
        }

        private static IEnumerable<string> PatchedVendorFiles()
        {
            return new[]
            {
                "ROS2ForUnity.cs",
                "ROS2Node.cs",
                "ROS2UnityComponent.cs",
                "ROS2UnityCore.cs",
                "Sensor.cs",
                "Time/DotnetTimeSource.cs",
                "Time/ROS2Clock.cs",
                "Time/ROS2ScalableTimeSource.cs",
                "Time/ROS2TimeSource.cs",
                "Time/TimeUtils.cs"
            };
        }

        private static string Normalize(string text)
        {
            return text.Replace("\r\n", "\n").TrimEnd();
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase111F file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase111F validation.");
            return root;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
