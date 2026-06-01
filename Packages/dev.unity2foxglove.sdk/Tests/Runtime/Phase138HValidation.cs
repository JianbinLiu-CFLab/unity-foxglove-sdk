// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138H validation for shared timeline and streaming LiDAR scan state.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Sensors.Lidar;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Regression checks for 138H LiDAR-IMU timeline alignment and streaming scan
    /// behavior.
    /// </summary>
    public static class Phase138HValidation
    {
        private const string VirtualLidarRelativePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs";
        private const string SensorUnitProfileRelativePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/SensorUnitProfile.cs";
        private const string SensorUnitProfileEditorRelativePath =
            "Packages/dev.unity2foxglove.sdk/Editor/Sensors/SensorUnitProfileEditor.cs";
        private const string VirtualLidarEditorRelativePath =
            "Packages/dev.unity2foxglove.sdk/Editor/Sensors/VirtualLidarEditor.cs";
        private const string FoxgloveManagerRelativePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs";
        private const string LidarModelSpecRelativePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/LidarModelSpec.cs";
        private const string DemoEditorRelativePath =
            "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138MazeDemoSceneBuilder.cs";
        private const string DemoBootstrapRelativePath =
            "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs";
        private const string DemoVehicleRelativePath =
            "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138LidarVehicleController.cs";
        private const string ImportedDemoBootstrapRelativePath =
            "Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs";
        private const string DemoEditorFileName = "Phase138MazeDemoSceneBuilder.cs";

        /// <summary>Run all Phase 138H checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138H: LiDAR-IMU Time Sync + Streaming Scan ===");

            VerifySharedSensorClock();
            VerifyStreamingLiDARState();
            VerifyPhase138hDemoHooks();
            VerifyAbsoluteTimeField();
            VerifyTIlBaseline();
            VerifyTIlOverrideContracts();
            VerifyTIlExtrinsicMath();

            Console.WriteLine("Phase 138H: all checks passed.");
            Console.WriteLine();
        }

        private static void VerifySharedSensorClock()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var managerSource = ReadText(repoRoot, FoxgloveManagerRelativePath);

            Check(managerSource.Contains("GetSharedSensorClockUnixTime"),
                "138H-1: FoxgloveManager exposes shared sensor clock API");
            Check(Regex.IsMatch(managerSource,
                        @"_sensorClockInitialized|_sensorClockEpochUnixNs|_sensorClockEpochPhysSeconds"),
                    "138H-2: FoxgloveManager stores shared clock epoch state");
        }

        private static void VerifyStreamingLiDARState()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var source = ReadText(repoRoot, VirtualLidarRelativePath);

            Check(source.Contains("_scanSubSteps"), "138H-3: VirtualLidar exposes configurable _scanSubSteps");
            Check(source.Contains("_activeScanStartPhysSeconds"),
                "138H-4: VirtualLidar tracks each scan's physical-start epoch for shared timing");
            Check(source.Contains("_scanColumnProgress") && source.Contains("columnsToEmit"),
                "138H-5: VirtualLidar advances the streaming scan by columns per tick");
            Check(source.Contains("StartNewScan(Time.fixedTimeAsDouble)"),
                "138H-6: VirtualLidar restarts each completed scan from the actual physics time (steady, non-superseding scan timestamps)");
            Check(source.Contains("_scanCrossings") && source.Contains("GetSubArray"),
                "138H-7: per-tick batch casts one ScheduleBatch slice and publishes at revolution crossings");
            Check(source.Contains("_columnRays"),
                "138H-8: VirtualLidar buckets rays by column (removes the per-column O(N) scan)");
            Check(source.Contains("RaycastCommand.ScheduleBatch") &&
                  source.Contains("_pointCloudPublisher.SetFrame(_activeScanFrame)"),
                "138H-9: streaming worker writes managed frame buffer and publishes via publisher SetFrame");
        }

        private static void VerifyPhase138hDemoHooks()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var specSource = ReadText(repoRoot, LidarModelSpecRelativePath);
            var demoEditorSource = ResolveDemoBuilderSource(repoRoot);

            Check(Regex.IsMatch(specSource, @"LidarToSensorTranslationMeters") &&
                  Regex.IsMatch(specSource, @"ImuToSensorTranslationMeters"),
                "138H-10: LidarModelSpec includes Ouster-style child-to-sensor translation fields");
            Check(Regex.IsMatch(specSource, @"LidarToSensorRotation") &&
                  Regex.IsMatch(specSource, @"ImuToSensorRotation"),
                "138H-11: LidarModelSpec includes Ouster-style child-to-sensor rotation fields");
            Check(demoEditorSource.Contains("ApplySensorChildTransform") &&
                  demoEditorSource.Contains("InvertExtrinsic"),
                "138H-12: Demo builder inverts child-to-sensor extrinsics into Unity local transforms");
        }

        // Behavioral check: registry ships a non-zero T_IL extrinsic baseline (not zero/identity).
        private static void VerifyTIlBaseline()
        {
            Check(LidarModelRegistry.TryGet(LidarVendor.Ouster, "OS-1-32", out var spec),
                "138H-16: OS-1-32 resolves from the model registry");
            Check(spec.ImuToSensorTranslationMeters != System.Numerics.Vector3.Zero &&
                  spec.LidarToSensorTranslationMeters != System.Numerics.Vector3.Zero,
                "138H-17: OS-1-32 ships non-zero Ouster child-to-sensor extrinsic baselines");
            Check(NearlyEqual(spec.LidarToSensorTranslationMeters, new System.Numerics.Vector3(0f, 0f, 0.038195f)) &&
                  NearlyEqual(spec.ImuToSensorTranslationMeters, new System.Numerics.Vector3(-0.002441f, -0.009725f, 0.007533f)),
                "138H-18: OS-1-32 registry baselines match documented Ouster metadata");
        }

        private static void VerifyTIlOverrideContracts()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var lidarSource = ReadText(repoRoot, VirtualLidarRelativePath);
            var sensorUnitSource = ReadText(repoRoot, SensorUnitProfileRelativePath);
            var sensorUnitEditorSource = ReadText(repoRoot, SensorUnitProfileEditorRelativePath);
            var lidarEditorSource = ReadText(repoRoot, VirtualLidarEditorRelativePath);
            var demoEditorSource = ResolveDemoBuilderSource(repoRoot);
            var demoBootstrapSource = ReadText(repoRoot, DemoBootstrapRelativePath);
            var demoVehicleSource = ReadText(repoRoot, DemoVehicleRelativePath);

            Check(sensorUnitSource.Contains("class SensorUnitProfile") &&
                  sensorUnitSource.Contains("_profileSource") &&
                  sensorUnitSource.Contains("_pointCloudPublisher") &&
                  sensorUnitSource.Contains("_useLidarToSensorExtrinsic") &&
                  sensorUnitSource.Contains("_useImuToSensorExtrinsic") &&
                  sensorUnitSource.Contains("_useLidarToImuExtrinsic") &&
                  sensorUnitSource.Contains("NormalizeExtrinsicSelection") &&
                  !sensorUnitSource.Contains("ExtrinsicAuthoringMode") &&
                  sensorUnitSource.Contains("ModelLidarToSensor") &&
                  sensorUnitSource.Contains("EffectiveLidarToSensor") &&
                  sensorUnitSource.Contains("EffectiveImuToSensor") &&
                  sensorUnitSource.Contains("EffectiveLidarToImu"),
                "138H-19: SensorUnitProfile owns profile selection, publisher reference, and unit extrinsics");
            Check(lidarSource.Contains("GetComponentInParent<SensorUnitProfile>()") &&
                  lidarSource.Contains("CreateScanPattern(_columnStep)") &&
                  !lidarEditorSource.Contains("Profile Source") &&
                  !lidarEditorSource.Contains("IMU -> Sensor Extrinsic"),
                "138H-20: VirtualLidar consumes SensorUnitProfile and no longer exposes shared profile/extrinsic UI");
            Check(!lidarSource.Contains("ApplyEffectiveTIlToImuMount") &&
                  !lidarSource.Contains("FindGeneratedImuMount"),
                "138H-21: VirtualLidar does not mutate scene hierarchy to apply T_IL");
            Check(sensorUnitEditorSource.Contains("Override Model LiDAR->Sensor") &&
                  sensorUnitEditorSource.Contains("Override Model IMU->Sensor") &&
                  sensorUnitEditorSource.Contains("Override Model LiDAR->IMU") &&
                  sensorUnitEditorSource.Contains("Use LiDAR -> Sensor") &&
                  sensorUnitEditorSource.Contains("Use IMU -> Sensor") &&
                  sensorUnitEditorSource.Contains("Use LiDAR -> IMU") &&
                  sensorUnitEditorSource.Contains("Model Defaults") &&
                  sensorUnitEditorSource.Contains("DrawModelDefaults") &&
                  sensorUnitEditorSource.Contains("DrawExtrinsicUsageToggle") &&
                  sensorUnitEditorSource.Contains("DrawExtrinsicsHelp") &&
                  sensorUnitEditorSource.Contains("DrawDerivedExtrinsicPreview") &&
                  sensorUnitEditorSource.Contains("LiDAR->IMU") &&
                  sensorUnitEditorSource.Contains("Rotation Input") &&
                  sensorUnitEditorSource.Contains("3x3 Rotation Matrix") &&
                  sensorUnitEditorSource.Contains("Derived") &&
                  !sensorUnitEditorSource.Contains("Extrinsic Authoring Mode") &&
                  !sensorUnitEditorSource.Contains("Ouster Metadata") &&
                  !sensorUnitEditorSource.Contains("SLAM Friendly") &&
                  !sensorUnitEditorSource.Contains("IMU Anchored") &&
                  !sensorUnitEditorSource.Contains("Model {labelPrefix}") &&
                  !sensorUnitEditorSource.Contains("Apply T_IL To IMUMount"),
                "138H-22: SensorUnitProfileEditor exposes checkbox-based two-of-three extrinsic editing with separate model defaults");
            Check(!sensorUnitSource.Contains("[Header(\"Profile\")]") &&
                  !sensorUnitSource.Contains("[Header(\"Frames\")]") &&
                  !sensorUnitSource.Contains("[Header(\"LiDAR -> Sensor Extrinsic\")]") &&
                  !sensorUnitSource.Contains("[Header(\"IMU -> Sensor Extrinsic\")]") &&
                  !lidarSource.Contains("[Header(\"Scan\")]") &&
                  !lidarSource.Contains("[Header(\"Synthetic Values\")]"),
                "138H-23: custom sensor Inspectors do not double-render section headers");
            Check(demoVehicleSource.Contains("new GameObject(\"Lidar-IMU-Unit\")") &&
                  demoVehicleSource.Contains("BuildVehicle(Vector3 position, out Transform lidarImuUnit, out Transform lidarMount)") &&
                  demoEditorSource.Contains("lidarImuUnit.gameObject.AddComponent<SensorUnitProfile>()") &&
                  demoEditorSource.Contains("lidarImuUnit.gameObject.AddComponent<FoxglovePointCloudPublisher>()") &&
                  demoEditorSource.Contains("SetField(publisher, \"_manager\", manager)") &&
                  !demoEditorSource.Contains("mgrGo.AddComponent<FoxglovePointCloudPublisher>()") &&
                  demoEditorSource.Contains("sensorUnit.EffectiveLidarToSensor") &&
                  demoEditorSource.Contains("sensorUnit.EffectiveImuToSensor") &&
                  demoEditorSource.Contains("SetField(unitPub, \"_childFrameId\", \"os_sensor\")") &&
                  demoEditorSource.Contains("SetField(lidarPub, \"_parentFrameId\", \"os_sensor\")") &&
                  demoEditorSource.Contains("SetField(lidarPub, \"_childFrameId\", \"os_lidar\")") &&
                  demoEditorSource.Contains("SetField(imuPub, \"_parentFrameId\", \"os_sensor\")") &&
                  demoEditorSource.Contains("SetField(imuPub, \"_childFrameId\", \"os_imu\")") &&
                  !demoEditorSource.Contains("imuMount.transform.SetParent(lidarMount"),
                "138H-24: editor maze builder emits base_link -> os_sensor -> os_lidar/os_imu and keeps point-cloud publisher on the sensor unit");
            Check(demoBootstrapSource.Contains("lidarImuUnit.gameObject.AddComponent<SensorUnitProfile>()") &&
                  demoBootstrapSource.Contains("lidarImuUnit.gameObject.AddComponent<FoxglovePointCloudPublisher>()") &&
                  demoBootstrapSource.Contains("SetPrivateField(publisher, \"_manager\", manager)") &&
                  !demoBootstrapSource.Contains("mgrGo.AddComponent<FoxglovePointCloudPublisher>()") &&
                  demoBootstrapSource.Contains("sensorUnit.EffectiveLidarToSensor") &&
                  demoBootstrapSource.Contains("sensorUnit.EffectiveImuToSensor") &&
                  demoBootstrapSource.Contains("SetPrivateField(sensorPublisher, \"_childFrameId\", \"os_sensor\")") &&
                  demoBootstrapSource.Contains("SetPrivateField(lidarPublisher, \"_parentFrameId\", \"os_sensor\")") &&
                  demoBootstrapSource.Contains("SetPrivateField(lidarPublisher, \"_childFrameId\", \"os_lidar\")") &&
                  demoBootstrapSource.Contains("SetPrivateField(imuPublisher, \"_parentFrameId\", \"os_sensor\")") &&
                  demoBootstrapSource.Contains("SetPrivateField(imuPublisher, \"_childFrameId\", \"os_imu\")") &&
                  !demoBootstrapSource.Contains("imuMount.SetParent(lidarMount"),
                "138H-25: runtime maze bootstrap emits base_link -> os_sensor -> os_lidar/os_imu and keeps point-cloud publisher on the sensor unit");
        }

        private static void VerifyTIlExtrinsicMath()
        {
            var expected = System.Numerics.Quaternion.CreateFromYawPitchRoll(0.2f, -0.1f, 0.3f);
            var matrix = System.Numerics.Matrix4x4.CreateFromQuaternion(expected);
            var translation = new System.Numerics.Vector3(0.006253f, -0.011775f, 0.007645f);

            var extrinsic = LidarTIlExtrinsic.FromRotationMatrix3x3(
                translation,
                matrix.M11, matrix.M12, matrix.M13,
                matrix.M21, matrix.M22, matrix.M23,
                matrix.M31, matrix.M32, matrix.M33);

            Check(extrinsic.TranslationMeters == translation,
                "138H-26: T_IL matrix conversion preserves standalone translation");
            Check(Math.Abs(System.Numerics.Quaternion.Dot(extrinsic.Rotation, expected)) > 0.9999f,
                "138H-27: T_IL 3x3 rotation matrix converts back to the same normalized quaternion");

            var zeroRotation = new LidarTIlExtrinsic(System.Numerics.Vector3.Zero, default);
            Check(zeroRotation.Rotation == System.Numerics.Quaternion.Identity,
                "138H-28: T_IL zero/invalid quaternion normalizes to identity");

            var repoRoot = Phase16Validation.FindRepoRoot();
            var importedBootstrapSource = TryReadText(repoRoot, ImportedDemoBootstrapRelativePath);
            Check(importedBootstrapSource == null ||
                  (importedBootstrapSource.Contains("using Unity.FoxgloveSDK.Sensors.Lidar;") &&
                   importedBootstrapSource.Contains("SetPrivateField(sensorPublisher, \"_childFrameId\", \"os_sensor\")")),
                "138H-29: imported maze bootstrap resolves LidarTIlExtrinsic and emits os_sensor TF");
        }

        // Behavioral check (shared layer, harness-runnable) for the D6 absolute-ns t field.
        private static void VerifyAbsoluteTimeField()
        {
            // Off by default: existing point-cloud payloads are unchanged (no `t` field).
            var plain = new PointCloudFrame();
            plain.Points.Add(new PointCloudPoint(1f, 2f, 3f) { TimeOffsetSeconds = 0.01f });
            var plainPacked = PointCloudPackedDataBuilder.Build(plain);
            Check(plainPacked.Fields.All(f => f.Name != "t"),
                "138H-13: absolute-ns t field is absent unless opted in");

            // Opt-in: adds an Ouster-style `t` = round(TimeOffsetSeconds * 1e9) ns.
            var frame = new PointCloudFrame { EmitAbsoluteTimeNs = true };
            frame.Points.Add(new PointCloudPoint(1f, 2f, 3f) { TimeOffsetSeconds = 0.0025f });
            var packed = PointCloudPackedDataBuilder.Build(frame);
            var tField = packed.Fields.FirstOrDefault(f => f.Name == "t");
            Check(tField != null, "138H-14: opt-in adds Ouster-style t field for SLAM front-ends");
            if (tField == null)
                return;

            var t = BitConverter.ToUInt32(packed.Data, (int)tField.Offset);
            Check(t == 2_500_000u, "138H-15: t equals round(TimeOffsetSeconds * 1e9) ns");
        }

        private static string ReadText(string repoRoot, string relativePath)
        {
            var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new InvalidOperationException($"Phase 138H cannot find expected file: {path}");
            return File.ReadAllText(path);
        }

        private static string TryReadText(string repoRoot, string relativePath)
        {
            var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static bool NearlyEqual(System.Numerics.Vector3 actual, System.Numerics.Vector3 expected, float epsilon = 0.000001f)
            => Math.Abs(actual.X - expected.X) <= epsilon &&
               Math.Abs(actual.Y - expected.Y) <= epsilon &&
               Math.Abs(actual.Z - expected.Z) <= epsilon;

        private static string ResolveDemoBuilderSource(string repoRoot)
        {
            try
            {
                var literal = Path.Combine(repoRoot, DemoEditorRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(literal))
                    return File.ReadAllText(literal);
            }
            catch (Exception)
            {
                // Ignore and fall back to filename search for environments where relative
                // paths with `~` or spaces are normalized differently.
            }

            var matches = Directory.GetFiles(repoRoot, DemoEditorFileName, SearchOption.AllDirectories);
            var packageMatches = Array.FindAll(matches, path =>
                path.Replace('\\', '/').Contains("/Packages/dev.unity2foxglove.sdk/Samples~/", StringComparison.OrdinalIgnoreCase));
            if (packageMatches.Length == 1)
                return File.ReadAllText(packageMatches[0]);
            if (packageMatches.Length > 1)
                throw new InvalidOperationException(
                    $"Phase 138H cannot disambiguate demo source file: {DemoEditorRelativePath} ({packageMatches.Length} package matches)");

            if (matches.Length == 1)
                return File.ReadAllText(matches[0]);

            throw new InvalidOperationException(
                $"Phase 138H cannot find expected file: {DemoEditorRelativePath} (found {matches.Length} matches)");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException($"Phase 138H validation failed: {label}");
            Console.WriteLine($"[PASS] {label}");
        }
    }
}
