// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR Maze Demo
// Purpose: Bootstraps the maze demo scene at runtime when no prebuilt scene exists.

using System.Reflection;
using Unity.FoxgloveSDK.Sensors.Lidar;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity.FoxgloveSDK.Samples.LidarMaze
{
    /// <summary>
    /// Runtime bootstrap for the Virtual LiDAR Maze Demo. Builds the same scene
    /// the editor tool (Phase138MazeDemoSceneBuilder) bakes, so hitting Play in an
    /// empty scene yields a working demo. Prefer the editor tool for an
    /// inspectable, pre-generated scene.
    ///
    /// TF tree: map -> base_link -> os_sensor -> os_lidar/os_imu.
    /// In Foxglove set the 3D panel Display frame to "map" to watch the car drive
    /// through the static maze with the point cloud accumulating (Decay time > 0).
    /// </summary>
    public class Phase138MazeDemoBootstrap : MonoBehaviour
    {
        private const string FullFidelityModel = "OS-2-128";
        private const string FullFidelityMode = "2048x10";
        private const int FullFidelityPointCount = 128 * 2048;

        private static bool s_warnedManagerNull;

        private void Start()
        {
            Application.runInBackground = true;

            // 1. FoxgloveManager (RightHand so TF and point cloud share handedness).
            var mgrGo = new GameObject("FoxgloveManager");
            var manager = mgrGo.AddComponent<FoxgloveManager>();
            SetPrivateField(manager, "_coordinateMode", CoordinateMode.RightHand);

            // 2. Maze (centred on origin)
            Phase138MazeBuilder.Build(8, 8, 2f, 1.5f, 0.2f, 42);

            // 3. Vehicle (base_link) at maze start cell, with roof LiDAR mount.
            var start = Phase138MazeBuilder.CellCenter(0, 0, 8, 8, 2f);
            var vehicleGo = Phase138LidarVehicleController.BuildVehicle(start, out var lidarImuUnit, out var lidarMount);

            var sensorUnit = lidarImuUnit.gameObject.AddComponent<SensorUnitProfile>();
            SetPrivateField(sensorUnit, "_manager", manager);
            SetPrivateField(sensorUnit, "_model", FullFidelityModel);
            SetPrivateField(sensorUnit, "_mode", FullFidelityMode);

            var publisher = lidarImuUnit.gameObject.AddComponent<FoxglovePointCloudPublisher>();
            SetPrivateField(publisher, "_manager", manager);
            SetPrivateField(publisher, "_maxPoints", FullFidelityPointCount);
            SetPrivateField(publisher, "_maxPackedBytes", 0);
            SetPrivateField(publisher, "_publishRateHz", 10f);
            SetPrivateField(publisher, "_samplingMode", Unity.FoxgloveSDK.Util.PointCloudSamplingMode.UniformStride);
            // Draco output: compresses the cloud and runs the encode on a worker thread
            // (lower bandwidth). Publishes foxglove.CompressedPointCloud on /unity/point_cloud_draco.
            SetPrivateField(publisher, "_outputMode", PointCloudOutputMode.Draco);
            SetPrivateField(publisher, "_topic", "/unity/point_cloud_draco");
            SetPrivateField(publisher, "_frameId", "os_lidar");
            SetPrivateField(sensorUnit, "_pointCloudPublisher", publisher);
            publisher.enabled = false; // enable after verifying Runtime is ready

            var vehicleRb = vehicleGo.AddComponent<Rigidbody>();
            vehicleRb.useGravity = false;

            var controller = vehicleGo.AddComponent<Phase138LidarVehicleController>();
            SetPrivateField(controller, "_useAutoWander", false); // WASD control

            var basePublisher = vehicleGo.AddComponent<FoxgloveTransformPublisher>();
            SetPrivateField(basePublisher, "_manager", manager);
            SetPrivateField(basePublisher, "_topic", "/tf");
            SetPrivateField(basePublisher, "_parentFrameId", "map");
            SetPrivateField(basePublisher, "_childFrameId", "base_link");

            // 4. LiDAR on the Ouster-style os_lidar frame under os_sensor.
            var lidar = lidarMount.gameObject.AddComponent<VirtualLidar>();
            SetPrivateField(lidar, "_manager", manager);
            SetPrivateField(lidar, "_sensorUnitProfile", sensorUnit);
            SetPrivateField(lidar, "_frameId", "os_lidar");
            SetPrivateField(lidar, "_pointCloudPublisher", publisher);
            SetPrivateField(lidar, "_columnStep", 1);
            SetPrivateField(lidar, "_maxRaysPerScan", 0);
            SetPrivateField(lidar, "_protectMainThreadFrameRate", true);
            SetPrivateField(lidar, "_protectedScanRateHz", 2f);
            SetPrivateField(lidar, "_publishEmptyFrames", false);
            SetPrivateField(lidar, "_drawDebugRays", false);
            ApplySensorChildTransform(lidarMount, sensorUnit.EffectiveLidarToSensor);

            // 4. IMU mount under the shared Ouster-style sensor unit frame.
            var imuMount = new GameObject("IMUMount").transform;
            imuMount.SetParent(lidarImuUnit, false);
            ApplySensorChildTransform(imuMount, sensorUnit.EffectiveImuToSensor);

            var imu = imuMount.gameObject.AddComponent<VirtualImu>();
            SetPrivateField(imu, "_manager", manager);
            SetPrivateField(imu, "_rigidbody", vehicleRb);
            SetPrivateField(imu, "_frameId", "os_imu");
            SetPrivateField(imu, "_topic", "/imu/data");
            SetPrivateField(imu, "_publishOnStart", true);
            SetPrivateField(imu, "_includeOrientation", true);
            SetPrivateField(imu, "_globalPhysicsRateHzOverride", 0);
            SetPrivateField(imu, "_enableNoise", false);
            SetPrivateField(imu, "_accelNoiseStdDev", 0f);
            SetPrivateField(imu, "_gyroNoiseStdDev", 0f);

            var sensorPublisher = lidarImuUnit.gameObject.AddComponent<FoxgloveTransformPublisher>();
            SetPrivateField(sensorPublisher, "_manager", manager);
            SetPrivateField(sensorPublisher, "_topic", "/tf_sensor");
            SetPrivateField(sensorPublisher, "_parentFrameId", "base_link");
            SetPrivateField(sensorPublisher, "_childFrameId", "os_sensor");
            SetPrivateField(sensorPublisher, "_useLocalTransform", true);

            var imuPublisher = imuMount.gameObject.AddComponent<FoxgloveTransformPublisher>();
            SetPrivateField(imuPublisher, "_manager", manager);
            // Distinct topic from base_link's publisher (same shared-/tf guard as the LiDAR).
            SetPrivateField(imuPublisher, "_topic", "/tf_imu");
            SetPrivateField(imuPublisher, "_parentFrameId", "os_sensor");
            SetPrivateField(imuPublisher, "_childFrameId", "os_imu");
            SetPrivateField(imuPublisher, "_useLocalTransform", true);

            var lidarPublisher = lidarMount.gameObject.AddComponent<FoxgloveTransformPublisher>();
            SetPrivateField(lidarPublisher, "_manager", manager);
            // Distinct topic from base_link's publisher: sharing one /tf channel
            // trips a server subscription-routing bug. Foxglove still aggregates this
            // FrameTransform into the TF tree.
            SetPrivateField(lidarPublisher, "_topic", "/tf_lidar");
            SetPrivateField(lidarPublisher, "_parentFrameId", "os_sensor");
            SetPrivateField(lidarPublisher, "_childFrameId", "os_lidar");
            SetPrivateField(lidarPublisher, "_useLocalTransform", true);

            // 5. Static overview camera framing the whole maze.
            var cameraGo = new GameObject("DemoCamera");
            var cam = cameraGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cameraGo.transform.position = new Vector3(0f, 20f, -18f);
            cameraGo.transform.LookAt(Vector3.zero);

            // Stream the overview camera to Foxglove as a JPEG image topic.
            var cameraPublisher = cameraGo.AddComponent<FoxgloveCameraPublisher>();
            SetPrivateField(cameraPublisher, "_manager", manager);
            SetPrivateField(cameraPublisher, "_topic", "/unity/camera");
            SetPrivateField(cameraPublisher, "_frameId", "unity_camera");

            // 6. Verify FoxgloveManager.Runtime then enable publishing.
            var runtime = manager.Runtime;
            if (runtime != null)
            {
                publisher.enabled = true;
            }
            else if (!s_warnedManagerNull)
            {
                Debug.LogWarning("[LidarMaze] FoxgloveManager.Runtime is null point cloud publisher stays disabled.");
                s_warnedManagerNull = true;
            }
        }

        private static void ApplySensorChildTransform(
            Transform child,
            LidarTIlExtrinsic childToSensor)
        {
            var sensorToChild = InvertExtrinsic(childToSensor);
            var localTranslation = VirtualLidar.ToUnityVector3(sensorToChild.TranslationMeters);
            var localRotation = VirtualLidar.ToUnityQuaternion(sensorToChild.Rotation);
            child.localPosition = CoordinateConverter.FoxgloveToUnityPosition(localTranslation);
            child.localRotation = CoordinateConverter.FoxgloveToUnityRotation(localRotation);
        }

        private static LidarTIlExtrinsic InvertExtrinsic(LidarTIlExtrinsic childToParent)
        {
            var inverseRotation = System.Numerics.Quaternion.Inverse(childToParent.Rotation);
            var inverseTranslation = System.Numerics.Vector3.Transform(
                -childToParent.TranslationMeters,
                inverseRotation);
            return new LidarTIlExtrinsic(inverseTranslation, inverseRotation);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            // Walk the base hierarchy: GetField does not return non-public fields
            // declared on base classes (e.g. _manager/_topic in FoxglovePublisherBase).
            var type = target.GetType();
            FieldInfo field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                type = type.BaseType;
            }

            if (field != null)
                field.SetValue(target, value);
            else
                Debug.LogWarning($"[LidarMaze] Failed to set private field '{fieldName}' on {target.GetType().Name}");
        }
    }
}
