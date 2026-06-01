// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138I validation for full-fidelity OS-2-128 10Hz point-cloud throughput.

using System;
using System.IO;
using System.Text.RegularExpressions;
using Foxglove.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Sensors.Lidar;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Regression checks for the Phase 138I full-fidelity OS-2-128 throughput path.
    /// </summary>
    public static class Phase138IValidation
    {
        private const int FullFidelityPointCount = 128 * 2048;
        private const string VirtualLidarRelativePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs";
        private const string PointCloudPublisherRelativePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs";
        private const string PointCloudPublisherEditorRelativePath =
            "Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs";
        private const string DemoEditorRelativePath =
            "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs";
        private const string DemoBootstrapRelativePath =
            "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs";
        private const string DemoReadmeRelativePath =
            "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/README.md";
        private const string ImportedDemoEditorRelativePath =
            "Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs";
        private const string ImportedDemoBootstrapRelativePath =
            "Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs";
        private const string SmokeSceneRelativePath =
            "Unity2Foxglove/Assets/Scenes/Phase138_Foxglove_MCAP_Smoke.unity";

        /// <summary>Run all Phase 138I checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138I: Full-Fidelity OS-2-128 10Hz PointCloud Throughput ===");

            VerifyOs2128FullFidelityPattern();
            VerifyFullFidelityMazeProfile();
            VerifyPointCloudQosPassThrough();
            VerifyOptInDiagnostics();
            VerifyAsyncRaycastStateMachine();
            VerifyDracoDocumentationIsCurrent();
            VerifyValidationWiring();
            VerifyUnityDebugAliases();
            VerifyVirtualLidarMainThreadIsolation();
            VerifyVirtualLidarStableSourceRateCap();
            VerifyVirtualLidarDracoBypassesManagedPointAppend();
            VerifyVirtualLidarPointSnapshotAsmdefBoundary();
            VerifyMetadataOnlyNativeDracoFrameCountsValidPoints();
            VerifyVirtualLidarNativeDracoPublishRateCap();
            VerifyManagedDracoRejectsMetadataOnlyFramesBeforePinning();

            Console.WriteLine("Phase 138I: all checks passed.");
            Console.WriteLine();
        }

        private static void VerifyOs2128FullFidelityPattern()
        {
            Check(LidarModelRegistry.TryGet(LidarVendor.Ouster, "OS-2-128", out var spec),
                "138I-1: OS-2-128 resolves from the model registry");

            var fullPattern = LidarScanPatternFactory.Create(spec, "2048x10", 1);
            Check(fullPattern.RayCount == FullFidelityPointCount,
                "138I-2: OS-2-128 2048x10 columnStep=1 produces 262144 rays");
            Check(Math.Abs(fullPattern.ScanRateHz - 10.0) < 0.0001,
                "138I-3: OS-2-128 2048x10 scan mode runs at 10Hz");

            var reducedPattern = LidarScanPatternFactory.Create(spec, "2048x10", 4);
            Check(reducedPattern.RayCount < FullFidelityPointCount,
                "138I-4: validation catches the old columnStep=4 reduced-fidelity path");
        }

        private static void VerifyFullFidelityMazeProfile()
        {
            var editor = ReadRepoText(DemoEditorRelativePath);
            var bootstrap = ReadRepoText(DemoBootstrapRelativePath);

            Check(ContainsFullFidelityProfile(editor),
                "138I-5: editor maze builder applies a named full-fidelity OS-2-128 2048x10 stress profile");
            Check(ContainsFullFidelityProfile(bootstrap),
                "138I-6: runtime maze bootstrap applies the same full-fidelity stress profile");
            Check(editor.Contains("EditorApplication.isPlayingOrWillChangePlaymode", StringComparison.Ordinal),
                "138I-7: editor maze builder refuses to rebuild/dirty the scene during Play Mode");
            Check(bootstrap.Contains("SetPrivateField(publisher, \"_publishRateHz\", 10f)", StringComparison.Ordinal),
                "138I-8: runtime bootstrap pins point-cloud publish rate to 10Hz");
            Check(editor.Contains("SetField(publisher, \"_publishRateHz\", 10f)", StringComparison.Ordinal),
                "138I-9: editor builder pins point-cloud publish rate to 10Hz");
        }

        private static bool ContainsFullFidelityProfile(string source)
        {
            return source.Contains("OS-2-128", StringComparison.Ordinal)
                   && source.Contains("2048x10", StringComparison.Ordinal)
                   && Regex.IsMatch(source, @"Set(?:Private)?Field\(lidar,\s*""_columnStep"",\s*1\)")
                   && Regex.IsMatch(source, @"Set(?:Private)?Field\(lidar,\s*""_maxRaysPerScan"",\s*0\)")
                   && Regex.IsMatch(source, @"Set(?:Private)?Field\(publisher,\s*""_maxPoints"",\s*(?:FullFidelityPointCount|262144)\)")
                   && Regex.IsMatch(source, @"Set(?:Private)?Field\(publisher,\s*""_maxPackedBytes"",\s*0\)")
                   && source.Contains("PointCloudOutputMode.Draco", StringComparison.Ordinal);
        }

        private static void VerifyPointCloudQosPassThrough()
        {
            var publisher = ReadRepoText(PointCloudPublisherRelativePath);
            var passThroughPattern =
                @"if \(!useVoxelGrid && !forceUniformFallback && frame\.UnixNs != 0 && !string\.IsNullOrEmpty\(frame\.FrameId\) && pointCount <= pointBudget\)\s*\{[^}]*return frame;";
            Check(Regex.IsMatch(publisher, passThroughPattern, RegexOptions.Singleline),
                "138I-10: PointCloud QoS returns the original frame when full fidelity is within budget");
            Check(publisher.Contains("Math.Max(0, _maxPackedBytes)", StringComparison.Ordinal),
                "138I-11: Max Packed Bytes 0 disables byte-budget truncation");
        }

        private static void VerifyOptInDiagnostics()
        {
            var lidar = ReadRepoText(VirtualLidarRelativePath);
            var publisher = ReadRepoText(PointCloudPublisherRelativePath);

            Check(lidar.Contains("_logPerformanceDiagnostics", StringComparison.Ordinal)
                  && lidar.Contains("[LidarDiag]", StringComparison.Ordinal)
                  && lidar.Contains("LogOption.NoStacktrace", StringComparison.Ordinal)
                  && lidar.Contains("completeMs", StringComparison.Ordinal)
                  && lidar.Contains("overrun", StringComparison.Ordinal),
                "138I-12: VirtualLidar exposes opt-in no-stacktrace throughput diagnostics");
            Check(publisher.Contains("_logPerformanceDiagnostics", StringComparison.Ordinal)
                  && publisher.Contains("[PointCloudDiag]", StringComparison.Ordinal)
                  && publisher.Contains("cloneMs", StringComparison.Ordinal)
                  && publisher.Contains("encodeMs", StringComparison.Ordinal)
                  && publisher.Contains("drop", StringComparison.Ordinal),
                "138I-13: point-cloud publisher exposes opt-in clone/encode/drop diagnostics");
            Check(!lidar.Contains("SetField(lidar, \"_logPerformanceDiagnostics\", true", StringComparison.Ordinal)
                  && !publisher.Contains("SetPrivateField(publisher, \"_logPerformanceDiagnostics\", true", StringComparison.Ordinal),
                "138I-14: diagnostics remain opt-in and are not auto-enabled by demo scaffolding");
        }

        private static void VerifyAsyncRaycastStateMachine()
        {
            var lidar = ReadRepoText(VirtualLidarRelativePath);

            Check(lidar.Contains("enum PendingScanState", StringComparison.Ordinal)
                  && lidar.Contains("Scheduled", StringComparison.Ordinal)
                  && lidar.Contains("Consumed", StringComparison.Ordinal)
                  && lidar.Contains("Published", StringComparison.Ordinal),
                "138I-15: async raycast is represented as an explicit pending-scan state machine");
            Check(lidar.Contains("JobHandle", StringComparison.Ordinal)
                  && lidar.Contains("DrainPendingScan", StringComparison.Ordinal)
                  && lidar.Contains("SchedulePendingScan", StringComparison.Ordinal)
                  && lidar.Contains("ConsumePendingScan", StringComparison.Ordinal),
                "138I-16: async raycast schedules one tick and consumes/drains on later lifecycle paths");
            Check(!lidar.Contains("ScheduleBatch(_commands.GetSubArray(0, batchCount), _results.GetSubArray(0, batchCount), 64).Complete()", StringComparison.Ordinal),
                "138I-17: VirtualLidar no longer completes the RaycastCommand batch synchronously in FixedUpdate");
            Check(!lidar.Contains("_pointData.CopyTo(_pointDataManaged)", StringComparison.Ordinal),
                "138I-18: hot consume path does not copy the full NativeArray point buffer into managed memory");
        }

        private static void VerifyDracoDocumentationIsCurrent()
        {
            var editor = ReadRepoText(PointCloudPublisherEditorRelativePath);
            var readme = ReadRepoText(DemoReadmeRelativePath);
            var combined = editor + "\n" + readme;

            Check(!combined.Contains("synchronous on the main thread", StringComparison.OrdinalIgnoreCase)
                  && !combined.Contains("native encode is synchronous", StringComparison.OrdinalIgnoreCase),
                "138I-19: UI/docs no longer claim Draco native encode is synchronous on the main thread");
            Check(combined.Contains("worker thread", StringComparison.OrdinalIgnoreCase)
                  && combined.Contains("Raw/ROS2", StringComparison.Ordinal),
                "138I-20: UI/docs distinguish background Draco visualization from full-stride Raw/ROS2 validation");
        }

        private static void VerifyValidationWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("--phase138i", StringComparison.Ordinal)
                  && registry.Contains("Phase138IValidation.Validate", StringComparison.Ordinal),
                "138I-21: validation registry exposes --phase138i");
            Check(project.Contains("Phase138IValidation.cs", StringComparison.Ordinal),
                "138I-22: test project compiles Phase138I validation");
        }

        private static void VerifyUnityDebugAliases()
        {
            var lidar = ReadRepoText(VirtualLidarRelativePath);
            var publisher = ReadRepoText(PointCloudPublisherRelativePath);

            Check(!lidar.Contains("using System.Diagnostics;", StringComparison.Ordinal)
                  && !publisher.Contains("using System.Diagnostics;", StringComparison.Ordinal)
                  && lidar.Contains("using Stopwatch = System.Diagnostics.Stopwatch;", StringComparison.Ordinal)
                  && publisher.Contains("using Stopwatch = System.Diagnostics.Stopwatch;", StringComparison.Ordinal)
                  && publisher.Contains("new System.Threading.Thread(RunDracoEncodeWorker)", StringComparison.Ordinal)
                  && publisher.Contains("Priority = System.Threading.ThreadPriority.BelowNormal", StringComparison.Ordinal),
                "138I-23: Stopwatch and Draco thread priority references avoid ambiguous UnityEngine names");
        }

        private static void VerifyVirtualLidarMainThreadIsolation()
        {
            var lidar = ReadRepoText(VirtualLidarRelativePath);
            var buildJob = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarBuildPointsJob.cs");

            Check(buildJob.Contains("[ReadOnly] public NativeArray<RaycastHit> Hits", StringComparison.Ordinal)
                  && buildJob.Contains("hit.distance > 0f", StringComparison.Ordinal)
                  && !buildJob.Contains("VirtualLidarHitData", StringComparison.Ordinal)
                  && lidar.Contains("var raycastHandle = RaycastCommand.ScheduleBatch", StringComparison.Ordinal)
                  && lidar.Contains("buildJob.Schedule(batchCount, 64, raycastHandle)", StringComparison.Ordinal)
                  && lidar.Contains("_pendingScanHandle.Complete()", StringComparison.Ordinal)
                  && !lidar.Contains("hit.collider == null", StringComparison.Ordinal)
                  && !lidar.Contains("_rayHits", StringComparison.Ordinal)
                  && !lidar.Contains("job.Schedule(_pendingBatchCount, 64).Complete()", StringComparison.Ordinal),
                "138I-24: VirtualLidar chains raycast-to-point build work off the main-thread consume path");
        }

        private static void VerifyVirtualLidarStableSourceRateCap()
        {
            var lidar = ReadRepoText(VirtualLidarRelativePath);
            var editor = ReadRepoText(DemoEditorRelativePath);
            var bootstrap = ReadRepoText(DemoBootstrapRelativePath);

            Check(lidar.Contains("_maxRaycastCommandsPerFixedUpdate", StringComparison.Ordinal)
                  && lidar.Contains("BudgetColumnsPerTick", StringComparison.Ordinal)
                  && lidar.Contains("Math.Min((int)Math.Floor(_scanColumnProgress), budgetColumns)", StringComparison.Ordinal)
                  && lidar.Contains("StartNewScan(Time.fixedTimeAsDouble)", StringComparison.Ordinal)
                  && !lidar.Contains("ComputeProtectedScanPeriodSeconds", StringComparison.Ordinal)
                  && !lidar.Contains("_protectMainThreadFrameRate", StringComparison.Ordinal)
                  && !lidar.Contains("_protectedScanRateHz", StringComparison.Ordinal)
                  && Regex.IsMatch(editor, @"SetField\(lidar,\s*""_maxRaycastCommandsPerFixedUpdate"",\s*\d+\)")
                  && Regex.IsMatch(bootstrap, @"SetPrivateField\(lidar,\s*""_maxRaycastCommandsPerFixedUpdate"",\s*\d+\)"),
                "138I-25: VirtualLidar caps per-FixedUpdate raycast work (budget) so full-fidelity scans lower their rate instead of stalling the main loop");
        }

        private static void VerifyVirtualLidarDracoBypassesManagedPointAppend()
        {
            var lidar = ReadRepoText(VirtualLidarRelativePath);
            var publisher = ReadRepoText(PointCloudPublisherRelativePath);
            var encoder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudNativeEncoder.cs");

            Check(lidar.Contains("VirtualLidarPointData[] _activeScanPointSnapshot", StringComparison.Ordinal)
                  && lidar.Contains("CopyPendingPointDataSegment", StringComparison.Ordinal)
                  && lidar.Contains("TryPublishActiveNativeDracoScan", StringComparison.Ordinal)
                  && lidar.Contains("TryQueueVirtualLidarDracoFrame", StringComparison.Ordinal)
                  && publisher.Contains("TryQueueVirtualLidarDracoFrame", StringComparison.Ordinal)
                  && publisher.Contains("QueueVirtualLidarDracoEncode", StringComparison.Ordinal)
                  && publisher.Contains("VirtualLidarPointData[]", StringComparison.Ordinal)
                  && publisher.Contains("RecordPointCloudPrepared(pointCount)", StringComparison.Ordinal)
                  && publisher.Contains("private void RecordPointCloudPrepared(int pointCount)", StringComparison.Ordinal)
                  && publisher.Contains("CompressedPointCloudMessageBuilder.SerializeProtobuf", StringComparison.Ordinal)
                  && encoder.Contains("TryEncodeVirtualLidarPoints", StringComparison.Ordinal)
                  && encoder.Contains("VirtualLidarPointData[]", StringComparison.Ordinal),
                "138I-26: VirtualLidar Draco path bypasses managed per-point append and encodes from an off-thread snapshot");
        }

        private static void VerifyVirtualLidarPointSnapshotAsmdefBoundary()
        {
            var publisher = ReadRepoText(PointCloudPublisherRelativePath);
            var encoder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudNativeEncoder.cs");
            var sharedPoint = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/VirtualLidarPointData.cs");
            var assemblyInfo = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/AssemblyInfo.cs");
            var protoAssemblyInfo = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/AssemblyInfo.cs");

            Check(!publisher.Contains("using Unity.FoxgloveSDK.Sensors.Lidar;", StringComparison.Ordinal)
                  && !encoder.Contains("using Unity.FoxgloveSDK.Sensors.Lidar;", StringComparison.Ordinal)
                  && sharedPoint.Contains("namespace Unity.FoxgloveSDK.Schemas.PointCloud", StringComparison.Ordinal)
                  && sharedPoint.Contains("internal struct VirtualLidarPointData", StringComparison.Ordinal)
                  && assemblyInfo.Contains("InternalsVisibleTo(\"Unity.FoxgloveSDK.Proto\")", StringComparison.Ordinal)
                  && assemblyInfo.Contains("InternalsVisibleTo(\"Unity.FoxgloveSDK.Sensors\")", StringComparison.Ordinal)
                  && protoAssemblyInfo.Contains("InternalsVisibleTo(\"Unity.FoxgloveSDK.Sensors\")", StringComparison.Ordinal),
                "138I-27: VirtualLidar Draco snapshot DTO lives below Sensors so Proto asmdef has no reverse dependency");
        }

        private static void VerifyMetadataOnlyNativeDracoFrameCountsValidPoints()
        {
            var metadataOnlyFrame = new PointCloudFrame
            {
                FrameId = "os_lidar",
                ValidCount = 123
            };

            Check(metadataOnlyFrame.GetPointCount() == 123,
                "138I-28: metadata-only native Draco frames count ValidCount without managed points");
        }

        private static void VerifyVirtualLidarNativeDracoPublishRateCap()
        {
            var publisher = ReadRepoText(PointCloudPublisherRelativePath);
            var editor = ReadRepoText(DemoEditorRelativePath);
            var bootstrap = ReadRepoText(DemoBootstrapRelativePath);
            var importedEditor = ReadRepoText(ImportedDemoEditorRelativePath);
            var importedBootstrap = ReadRepoText(ImportedDemoBootstrapRelativePath);
            var smokeScene = ReadRepoText(SmokeSceneRelativePath);

            Check(publisher.Contains("_nativeDracoMaxPublishRateHz", StringComparison.Ordinal)
                  && publisher.Contains("_lastNativeDracoPublishUnixNs", StringComparison.Ordinal)
                  && publisher.Contains("rateHz <= 0f", StringComparison.Ordinal)
                  && publisher.Contains("ShouldQueueVirtualLidarDracoFrame(unixNs)", StringComparison.Ordinal)
                  && publisher.Contains("RecordPointCloudDrop()", StringComparison.Ordinal),
                "138I-29: VirtualLidar native Draco path has an optional source-side publish cap and records dropped frames");
            Check(Regex.IsMatch(editor, @"SetField\(publisher,\s*""_nativeDracoMaxPublishRateHz"",\s*0f\)")
                  && Regex.IsMatch(bootstrap, @"SetPrivateField\(publisher,\s*""_nativeDracoMaxPublishRateHz"",\s*0f\)")
                  && Regex.IsMatch(importedEditor, @"SetField\(publisher,\s*""_nativeDracoMaxPublishRateHz"",\s*0f\)")
                  && Regex.IsMatch(importedBootstrap, @"SetPrivateField\(publisher,\s*""_nativeDracoMaxPublishRateHz"",\s*0f\)")
                  && smokeScene.Contains("_nativeDracoMaxPublishRateHz: 0", StringComparison.Ordinal)
                  && smokeScene.Contains("_suppressTransformFallbackAfterSourceFrames: 1", StringComparison.Ordinal)
                  && !editor.Contains("_nativeDracoPublishRateHz", StringComparison.Ordinal)
                  && !bootstrap.Contains("_nativeDracoPublishRateHz", StringComparison.Ordinal)
                  && !importedEditor.Contains("_nativeDracoPublishRateHz", StringComparison.Ordinal)
                  && !importedBootstrap.Contains("_nativeDracoPublishRateHz", StringComparison.Ordinal)
                  && !smokeScene.Contains("_nativeDracoPublishRateHz", StringComparison.Ordinal),
                "138I-30: maze demo and imported Unity project leave native Draco source cadence uncapped so the LiDAR raycast budget controls the actual rate");
            Check(publisher.Contains("_suppressTransformFallbackAfterSourceFrames", StringComparison.Ordinal)
                  && publisher.Contains("MarkSourceDrivenPointCloud", StringComparison.Ordinal)
                  && publisher.Contains("ShouldSuppressTransformFallback()", StringComparison.Ordinal)
                  && publisher.Contains("CreateFrameFromTransforms(unixNs)", StringComparison.Ordinal)
                  && publisher.Contains("PointCloud transform fallback suppressed", StringComparison.Ordinal),
                "138I-31: source-driven point clouds suppress sparse transform fallback frames that would overwrite LiDAR data");
        }

        private static void VerifyManagedDracoRejectsMetadataOnlyFramesBeforePinning()
        {
            var metadataOnlyFrame = new PointCloudFrame
            {
                FrameId = "os_lidar",
                ValidCount = 123
            };

            Check(!DracoPointCloudNativeEncoder.TryEncode(metadataOnlyFrame, out _, out var error)
                  && error.Contains("empty", StringComparison.OrdinalIgnoreCase),
                "138I-32: managed Draco encoder rejects metadata-only frames before indexing managed points");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new InvalidOperationException($"Phase 138I cannot find expected file: {path}");
            return File.ReadAllText(path);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException($"Phase 138I validation failed: {label}");
            Console.WriteLine($"[PASS] {label}");
        }
    }
}
