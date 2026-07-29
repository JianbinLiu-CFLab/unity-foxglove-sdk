// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 151 validation for profiler infrastructure boundaries.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase151Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 151 Tests ---");
            _passCount = 0;

            VerifyUnityNeutralProfilerCore();
            VerifyUnityProfilerAdapterShape();
            VerifyManagerProfilerLifecycleShape();
            VerifyPhase151BMarkerInstrumentation();
            VerifyPhase151CManualAcceptanceShape();
            VerifyProfilerUnitCoverage();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 151: " + _passCount + " checks passed.\n");
        }

        private static void VerifyUnityNeutralProfilerCore()
        {
            var abstraction = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Abstractions/IFoxgloveProfiler.cs");
            var nullProfiler = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Profiling/NullProfiler.cs");
            var global = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Profiling/FoxgloveProfiler.cs");

            Check(abstraction.Contains("public interface IFoxgloveProfiler", StringComparison.Ordinal)
                  && abstraction.Contains("IDisposable Sample(string name)", StringComparison.Ordinal)
                  && abstraction.Contains("void BeginSample(string name)", StringComparison.Ordinal)
                  && abstraction.Contains("void EndSample()", StringComparison.Ordinal)
                  && !abstraction.Contains("UnityEngine", StringComparison.Ordinal)
                  && !abstraction.Contains("Unity.Profiling", StringComparison.Ordinal),
                "Profiler abstraction is Unity-neutral");

            Check(nullProfiler.Contains("public sealed class NullProfiler : IFoxgloveProfiler", StringComparison.Ordinal)
                  && nullProfiler.Contains("public static readonly NullProfiler Instance", StringComparison.Ordinal)
                  && nullProfiler.Contains("public static readonly IDisposable Scope", StringComparison.Ordinal)
                  && nullProfiler.Contains("public IDisposable Sample(string name) => Scope", StringComparison.Ordinal)
                  && !nullProfiler.Contains("public IDisposable Sample(string name) => new", StringComparison.Ordinal),
                "NullProfiler returns a reusable no-op scope");

            Check(global.Contains("public static class FoxgloveProfiler", StringComparison.Ordinal)
                  && global.Contains("private static volatile IFoxgloveProfiler _global = NullProfiler.Instance", StringComparison.Ordinal)
                  && global.Contains("throw new ArgumentNullException", StringComparison.Ordinal)
                  && global.Contains("SetGlobal(object owner, IFoxgloveProfiler profiler)", StringComparison.Ordinal)
                  && global.Contains("ResetGlobal(object owner)", StringComparison.Ordinal)
                  && global.Contains("ResetGlobal()", StringComparison.Ordinal),
                "Global profiler defaults to NullProfiler and supports owner-scoped resets");
        }

        private static void VerifyUnityProfilerAdapterShape()
        {
            var adapter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Profiling/UnityProfilerAdapter.cs");

            Check(adapter.Contains("public sealed class UnityProfilerAdapter : IFoxgloveProfiler", StringComparison.Ordinal)
                  && adapter.Contains("Unity.Profiling", StringComparison.Ordinal)
                  && adapter.Contains("ProfilerMarker", StringComparison.Ordinal)
                  && adapter.Contains("ConcurrentDictionary<string, ProfilerMarker>", StringComparison.Ordinal)
                  && adapter.Contains("ConcurrentBag<ProfilerScope>", StringComparison.Ordinal)
                  && adapter.Contains("public IDisposable Sample(string name)", StringComparison.Ordinal)
                  && adapter.Contains("public void BeginSample(string name)", StringComparison.Ordinal)
                  && adapter.Contains("public void EndSample()", StringComparison.Ordinal),
                "Unity profiler adapter maps IFoxgloveProfiler to pooled ProfilerMarker scopes");
        }

        private static void VerifyManagerProfilerLifecycleShape()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var diagnosticsEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Diagnostics.cs");

            Check(manager.Contains("_profilingEnabled", StringComparison.Ordinal)
                  && manager.Contains("public bool ProfilingEnabled => _profilingEnabled", StringComparison.Ordinal)
                  && manager.Contains("ConfigureProfiler()", StringComparison.Ordinal)
                  && manager.Contains("FoxgloveProfiler.SetGlobal(this, UnityProfilerAdapter.Instance)", StringComparison.Ordinal)
                  && manager.Contains("FoxgloveProfiler.ResetGlobal(this)", StringComparison.Ordinal)
                  && manager.Contains("OnDisable()", StringComparison.Ordinal)
                  && manager.Contains("OnDestroy()", StringComparison.Ordinal)
                  && diagnosticsEditor.Contains("DrawProfilerDiagnostics()", StringComparison.Ordinal)
                  && diagnosticsEditor.Contains("DrawProperty(\"_profilingEnabled\", \"Unity Profiler Markers\")", StringComparison.Ordinal),
                "FoxgloveManager exposes profiling toggle, custom Inspector UI, and owner-scoped lifecycle hook");
        }

        private static void VerifyProfilerUnitCoverage()
        {
            var unitTest = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/Profiling/ProfilerCoreTests.cs");
            var markerTest = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/Profiling/ProfilerMarkerInstrumentationTests.cs");

            Check(unitTest.Contains("GlobalProfilerDefaultsToNullProfiler", StringComparison.Ordinal)
                  && unitTest.Contains("NullProfilerHotLoopDoesNotAllocate", StringComparison.Ordinal)
                  && unitTest.Contains("OwnerScopedResetOnlyClearsMatchingOwner", StringComparison.Ordinal)
                  && unitTest.Contains("UnityProfilerAdapterSampleScopesArePooledAfterDispose", StringComparison.Ordinal)
                  && markerTest.Contains("Phase151BMarkersUseBoundedLiteralNames", StringComparison.Ordinal)
                  && markerTest.Contains("ProfilerMarkersDoNotUseDynamicNames", StringComparison.Ordinal),
                "Unit tests cover profiler defaults, owner resets, adapter scope pooling, and bounded marker names");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase151"),
                "Validation registry exposes the profiler infrastructure flag");
        }

        private static void VerifyPhase151BMarkerInstrumentation()
        {
            var checks = new (string marker, string relativePath)[]
            {
                ("FoxglovePublisher.Tick", "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs"),
                ("FoxgloveManager.PublishJson", "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs"),
                ("FoxgloveManager.PublishProto", "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs"),
                ("FoxgloveManager.PublishRos2", "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs"),
                ("Ros2CdrWriter.ToArray", "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Cdr/Ros2CdrWriter.cs"),
                ("CdrBuild.FrameTransform", "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrFrameTransformBuilder.cs"),
                ("CdrBuild.SceneUpdate", "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrSceneUpdateBuilder.cs"),
                ("CdrBuild.PointCloud", "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrPointCloudBuilder.cs"),
                ("CdrBuild.PointCloud2", "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrSensorPointCloud2Builder.cs"),
                ("CdrBuild.LaserScan", "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrLaserScanBuilder.cs"),
                ("VirtualLidar.Update", "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs"),
                ("VirtualLidar.ScheduleScan", "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs"),
                // BuildPoints.Schedule marks the main-thread job scheduling boundary; the Burst job body itself stays marker-free.
                ("VirtualLidar.BuildPoints.Schedule", "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs"),
                ("VirtualLidar.Publish", "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanFramePublisher.cs"),
                ("VirtualImu.Publish", "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs"),
                ("PointCloudWorker.EncodeDraco", "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs"),
                ("PointCloudWorker.EncodePointCloud2Native", "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs"),
                ("WsSendQueue.Enqueue", "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsSendQueue.cs"),
                ("WsSendQueue.Flush", "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsSendQueue.cs"),
                ("WsFrameCodec.Encode", "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsFrameCodec.cs"),
            };

            foreach (var group in checks.GroupBy(item => item.relativePath))
            {
                var text = ReadRepoText(group.Key);
                foreach (var (marker, _) in group)
                {
                    Check(text.Contains("\"" + marker + "\"", StringComparison.Ordinal),
                        "Bounded profiler marker exists: " + marker);
                }
            }

            var pointCloud2 = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrSensorPointCloud2Builder.cs");
            var serialize = ExtractMethod(pointCloud2, "public static byte[] Serialize(");
            Check(pointCloud2.Contains("static Ros2CdrSensorPointCloud2Builder()", StringComparison.Ordinal)
                  && pointCloud2.Contains("EnsureLittleEndianRuntime();", StringComparison.Ordinal)
                  && !serialize.Contains("EnsureLittleEndianRuntime();", StringComparison.Ordinal),
                "PointCloud2 CDR endian guard runs once from the static constructor");
        }

        private static void VerifyPhase151CManualAcceptanceShape()
        {
            var script = ReadRepoText("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase151ManualAcceptance.cs");

            Check(script.Contains("[AddComponentMenu(\"Foxglove/Manual Acceptance/Phase151 Profiler\")]", StringComparison.Ordinal)
                  && script.Contains("[Phase151]", StringComparison.Ordinal)
                  && script.Contains("\"Phase151.Acceptance.PublishSamples\"", StringComparison.Ordinal)
                  && script.Contains("manager.ProfilingEnabled", StringComparison.Ordinal)
                  && script.Contains("runContinuously", StringComparison.Ordinal)
                  && script.Contains("initialSamplesPublished", StringComparison.Ordinal)
                  && script.Contains("profilerToggleObserved", StringComparison.Ordinal)
                  && script.Contains("BuildStatus", StringComparison.Ordinal)
                  && script.Contains("[CustomEditor(typeof(Phase151ManualAcceptance))]", StringComparison.Ordinal)
                  && script.Contains("DrawDefaultInspector()", StringComparison.Ordinal)
                  && !script.Contains("new ProfilerMarker($", StringComparison.Ordinal)
                  && !script.Contains("BeginSample($", StringComparison.Ordinal)
                  && !script.Contains("Sample($", StringComparison.Ordinal),
                "Phase151 manual acceptance script exposes stable Inspector state and no dynamic marker names");
        }

        private static string ExtractMethod(string source, string startToken)
        {
            var start = source.IndexOf(startToken, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var nextMember = source.IndexOf("\n        private static", start + startToken.Length, StringComparison.Ordinal);
            return nextMember < 0 ? source.Substring(start) : source.Substring(start, nextMember - start);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
