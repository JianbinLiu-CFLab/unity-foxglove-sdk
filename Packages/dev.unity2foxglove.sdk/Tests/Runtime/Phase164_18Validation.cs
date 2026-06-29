using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_18Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-18 Tests ---");
            _passed = 0;

            VerifyRigidWorldToLocalHelperIsUsed();
            VerifySpinningScanPatternPrecomputesTrigTables();
            VerifySchedulerAvoidsPerTickCrossingList();
            VerifyBuildJobUsesDirectPerRayMetadataReads();
            VerifyVirtualImuCachesNativeFrameHandlerPerFrame();
            VerifyRegistry();

            Console.WriteLine("Phase 164-18: " + _passed + " checks passed.\n");
        }

        private static void VerifyRigidWorldToLocalHelperIsUsed()
        {
            var converter = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/CoordinateConverterFloat3.cs");
            var scheduler = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");

            Check(converter.Contains("public static float4x4 RigidWorldToLocal(Vector3 position, Quaternion rotation)", StringComparison.Ordinal)
                  && converter.Contains("return math.inverse(transform);", StringComparison.Ordinal),
                "164-18A-1: coordinate converter exposes a unit-scale rigid world-to-local helper");
            Check(scheduler.Contains("CoordinateConverterFloat3.RigidWorldToLocal(worldPos, worldRot)", StringComparison.Ordinal)
                  && !scheduler.Contains("Matrix4x4\r\n                    .TRS(worldPos, worldRot, Vector3.one)", StringComparison.Ordinal)
                  && !scheduler.Contains("Matrix4x4\n                    .TRS(worldPos, worldRot, Vector3.one)", StringComparison.Ordinal),
                "164-18A-2: LiDAR pending scan scheduling avoids managed Matrix4x4 inverse construction");
            Check(lidar.Contains("CoordinateConverterFloat3.RigidWorldToLocal(transform.position, transform.rotation)", StringComparison.Ordinal)
                  && !lidar.Contains("Matrix4x4\r\n                .TRS(transform.position, transform.rotation, Vector3.one)", StringComparison.Ordinal)
                  && !lidar.Contains("Matrix4x4\n                .TRS(transform.position, transform.rotation, Vector3.one)", StringComparison.Ordinal),
                "164-18A-3: LiDAR scan start uses the same rigid inverse helper");
        }

        private static void VerifySpinningScanPatternPrecomputesTrigTables()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/SpinningScanPattern.cs");
            var ctor = PhaseValidationSourceHelpers.SourceMethod(source, "public SpinningScanPattern");
            var tryGetRay = PhaseValidationSourceHelpers.SourceMethod(source, "public bool TryGetRay");

            Check(source.Contains("private readonly double[] _sinAlt;", StringComparison.Ordinal)
                  && source.Contains("private readonly double[] _cosAlt;", StringComparison.Ordinal)
                  && source.Contains("private readonly double[] _sinColumnAzm;", StringComparison.Ordinal)
                  && source.Contains("private readonly double[] _cosColumnAzm;", StringComparison.Ordinal),
                "164-18B-1: spinning scan pattern owns precomputed trigonometry tables");
            Check(ctor.Contains("_sinAlt[i] = Math.Sin(_altRad[i]);", StringComparison.Ordinal)
                  && ctor.Contains("_cosAlt[i] = Math.Cos(_altRad[i]);", StringComparison.Ordinal)
                  && ctor.Contains("_sinColumnAzm[i] = Math.Sin(columnAzm);", StringComparison.Ordinal),
                "164-18B-2: spinning scan pattern fills trig tables once at construction");
            Check(!tryGetRay.Contains("Math.Sin(", StringComparison.Ordinal)
                  && !tryGetRay.Contains("Math.Cos(", StringComparison.Ordinal)
                  && tryGetRay.Contains("sinTotalAzm", StringComparison.Ordinal)
                  && tryGetRay.Contains("_cosAlt[ring]", StringComparison.Ordinal),
                "164-18B-3: spinning scan ray lookup avoids per-ray transcendental calls");
        }

        private static void VerifySchedulerAvoidsPerTickCrossingList()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");
            Check(!source.Contains("using System.Collections.Generic;", StringComparison.Ordinal)
                  && source.Contains("private readonly int[] _scanCrossings = new int[4];", StringComparison.Ordinal)
                  && source.Contains("private int _scanCrossingCount;", StringComparison.Ordinal),
                "164-18C-1: LiDAR scheduler uses fixed crossing scratch storage instead of List<int>");
            Check(!source.Contains("_scanCrossings.Clear();", StringComparison.Ordinal)
                  && source.Contains("_scanCrossingCount = 0;", StringComparison.Ordinal)
                  && source.Contains("_scanCrossings[_scanCrossingCount++] = batchCount;", StringComparison.Ordinal),
                "164-18C-2: LiDAR scheduler resets crossing count without clearing a List");
        }

        private static void VerifyBuildJobUsesDirectPerRayMetadataReads()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarBuildPointsJob.cs");
            Check(source.Contains("output.TimeOffsetSeconds = RayTimeOffsets[index];", StringComparison.Ordinal)
                  && source.Contains("output.Ring = RayRings[index];", StringComparison.Ordinal)
                  && !source.Contains("index < RayTimeOffsets.Length ? RayTimeOffsets[index] : 0f", StringComparison.Ordinal)
                  && !source.Contains("index < RayRings.Length ? RayRings[index] : (ushort)0", StringComparison.Ordinal),
                "164-18D-1: LiDAR build job avoids redundant metadata bounds branches");
        }

        private static void VerifyVirtualImuCachesNativeFrameHandlerPerFrame()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var update = PhaseValidationSourceHelpers.SourceMethod(source, "private void Update");
            var whileIndex = update.IndexOf("while (_queue.Count > 0)", StringComparison.Ordinal);
            var handlerIndex = update.IndexOf("var nativeFrameHandler = ImuNativeFrameReady;", StringComparison.Ordinal);

            Check(handlerIndex >= 0 && whileIndex >= 0 && handlerIndex < whileIndex,
                "164-18E-1: Virtual IMU snapshots native frame subscribers once per render frame");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-18\"", StringComparison.Ordinal), "164-18F-1: validation registry exposes Phase164-18");
            Check(project.Contains("Phase164_18Validation.cs", StringComparison.Ordinal), "164-18F-2: runtime validation project compiles Phase164-18");
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
