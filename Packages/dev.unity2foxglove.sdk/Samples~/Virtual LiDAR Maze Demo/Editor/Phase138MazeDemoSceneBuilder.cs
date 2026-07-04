// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR Maze Demo (Editor)
// Purpose: Builds a preconfigured maze scene with Virtual LiDAR + IMU demo components.

using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Sensors.Lidar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FoxgloveSDK.Samples.LidarMaze.EditorTools
{
    /// <summary>
    /// Editor tool that bakes the Virtual LiDAR Maze Demo into the active scene as
    /// inspectable, pre-generated objects (no runtime auto-generation). Builds the
    /// maze, a primitive car with a roof LiDAR/IMU unit, the
    /// map -> base_link -> os_sensor -> os_lidar/os_imu TF tree, a
    /// FoxgloveManager in RightHand mode, and an overview camera.
    ///
    /// In Foxglove set the 3D panel Display frame to "map" to watch the car drive
    /// through the static maze. Use WASD to drive; raise Decay time to accumulate
    /// the point cloud.
    /// </summary>
    public static class Phase138MazeDemoSceneBuilder
    {
        private const int CellsX = 8;
        private const int CellsZ = 8;
        private const float CellSize = 2f;
        private const string DefaultLidarModel = "OS-1-32";
        private const string DefaultLidarMode = "1024x10";
        private const int DefaultLidarPointCount = 32 * 1024;

        /// <summary>
        /// Rebuilds the preconfigured maze/vehicle demo scene from Unity's Foxglove menu.
        /// </summary>
        [MenuItem("Foxglove/Phase138/Build Maze Demo Scene")]
        public static void BuildScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[LidarMaze] Build Maze Demo Scene is disabled during Play Mode.");
                return;
            }

            // Clear previously generated demo roots, including any live
            // Phase138MazeDemoBootstrap so the baked scene is not rebuilt at Play.
            foreach (var rootName in new[]
                { "FoxgloveManager", "Maze", "Vehicle", "DemoCamera", "MazeBuilder", "DemoBootstrap" })
            {
                var existing = GameObject.Find(rootName);
                if (existing != null)
                    Object.DestroyImmediate(existing);
            }
            foreach (var stale in Object.FindObjectsByType<Phase138MazeDemoBootstrap>(
                         FindObjectsSortMode.None))
            {
                if (stale != null)
                    Object.DestroyImmediate(stale.gameObject);
            }

            // 1. FoxgloveManager (RightHand). Sensor publishers live on their units.
            var mgrGo = new GameObject("FoxgloveManager");
            Undo.RegisterCreatedObjectUndo(mgrGo, "Build Maze Demo");
            var manager = mgrGo.AddComponent<FoxgloveManager>();
            SetField(manager, "_coordinateMode", CoordinateMode.RightHand);

            // 2. Maze centred on origin.
            var maze = Phase138MazeBuilder.Build(CellsX, CellsZ, CellSize, 1.5f, 0.2f, 42);
            Undo.RegisterCreatedObjectUndo(maze, "Build Maze Demo");

            // 3. Vehicle (base_link) at the start cell with a roof LiDAR mount.
            var start = Phase138MazeBuilder.CellCenter(0, 0, CellsX, CellsZ, CellSize);
            var vehicleGo = Phase138LidarVehicleController.BuildVehicle(start, out var lidarImuUnit, out var lidarMount);
            Undo.RegisterCreatedObjectUndo(vehicleGo, "Build Maze Demo");

            var sensorUnit = lidarImuUnit.gameObject.AddComponent<SensorUnitProfile>();
            SetField(sensorUnit, "_manager", manager);
            SetField(sensorUnit, "_model", DefaultLidarModel);
            SetField(sensorUnit, "_mode", DefaultLidarMode);

            var publisher = lidarImuUnit.gameObject.AddComponent<FoxglovePointCloudPublisher>();
            SetField(publisher, "_manager", manager);
            SetField(publisher, "_frameId", "os_lidar");
            SetField(publisher, "_maxPoints", DefaultLidarPointCount);
            SetField(publisher, "_maxPackedBytes", 0);
            SetField(publisher, "_publishRateHz", 10f);
            SetField(publisher, "_nativeDracoMaxPublishRateHz", 6f);
            SetField(publisher, "_samplingMode", Unity.FoxgloveSDK.Util.PointCloudSamplingMode.UniformStride);
            // Default demo path stays WebSocket/Protobuf-friendly. Switch this
            // publisher to PointCloud2 Native manually when validating ROS2/SLAM.
            SetField(publisher, "_outputMode", PointCloudOutputMode.Draco);
            SetField(publisher, "_topic", "/unity/point_cloud_draco");
            SetField(sensorUnit, "_pointCloudPublisher", publisher);

            var rb = vehicleGo.AddComponent<Rigidbody>();
            rb.useGravity = false;
            var controller = vehicleGo.AddComponent<Phase138LidarVehicleController>();
            SetField(controller, "_useAutoWander", false); // WASD control

            var basePub = vehicleGo.AddComponent<FoxgloveTransformPublisher>();
            SetField(basePub, "_manager", manager);
            SetField(basePub, "_topic", "/tf");
            SetField(basePub, "_parentFrameId", "map");
            SetField(basePub, "_childFrameId", "base_link");

            // 4. LiDAR on the Ouster-style os_lidar frame under os_sensor.
            var lidar = lidarMount.gameObject.AddComponent<VirtualLidar>();
            SetField(lidar, "_manager", manager);
            SetField(lidar, "_sensorUnitProfile", sensorUnit);
            SetField(lidar, "_frameId", "os_lidar");
            SetField(lidar, "_pointCloudPublisher", publisher);
            SetField(lidar, "_columnStep", 1);
            SetField(lidar, "_maxRaysPerScan", 0);
            SetField(lidar, "_layerMask", (LayerMask)Physics.DefaultRaycastLayers);
            // Per-tick raycast budget keeps LiDAR work off the main loop. The scan rate
            // falls out of it automatically from rings-per-column, trading cloud Hz for
            // a steady main thread and a continuous, non-flickering point cloud.
            SetField(lidar, "_maxRaycastCommandsPerFixedUpdate", 6144);
            SetField(lidar, "_publishEmptyFrames", false);
            SetField(lidar, "_drawDebugRays", false);
            ApplySensorChildTransform(lidarMount, sensorUnit.EffectiveLidarToSensor);

            // 4. IMU on the shared Ouster-style sensor unit frame.
            var imuMount = new GameObject("IMUMount");
            Undo.RegisterCreatedObjectUndo(imuMount, "Build Maze Demo");
            imuMount.transform.SetParent(lidarImuUnit, false);
            ApplySensorChildTransform(imuMount.transform, sensorUnit.EffectiveImuToSensor);

            var imu = imuMount.AddComponent<VirtualImu>();
            SetField(imu, "_manager", manager);
            SetField(imu, "_rigidbody", rb);
            SetField(imu, "_frameId", "os_imu");
            SetField(imu, "_topic", "/imu/data");
            SetField(imu, "_publishOnStart", true);
            SetField(imu, "_includeOrientation", true);
            SetField(imu, "_globalPhysicsRateHzOverride", 0);
            SetField(imu, "_enableNoise", false);
            SetField(imu, "_accelNoiseStdDev", 0f);
            SetField(imu, "_gyroNoiseStdDev", 0f);

            var unitPub = lidarImuUnit.gameObject.AddComponent<FoxgloveTransformPublisher>();
            SetField(unitPub, "_manager", manager);
            SetField(unitPub, "_topic", "/tf_sensor");
            SetField(unitPub, "_parentFrameId", "base_link");
            SetField(unitPub, "_childFrameId", "os_sensor");
            SetField(unitPub, "_useLocalTransform", true);

            var imuPub = imuMount.AddComponent<FoxgloveTransformPublisher>();
            SetField(imuPub, "_manager", manager);
            // Separate topic from base_link's publisher (same shared-/tf guard as the LiDAR).
            SetField(imuPub, "_topic", "/tf_imu");
            SetField(imuPub, "_parentFrameId", "os_sensor");
            SetField(imuPub, "_childFrameId", "os_imu");
            SetField(imuPub, "_useLocalTransform", true);

            var lidarPub = lidarMount.gameObject.AddComponent<FoxgloveTransformPublisher>();
            SetField(lidarPub, "_manager", manager);
            // Separate topic from base_link's publisher: two publishers sharing one
            // /tf channel triggers a server subscription-routing bug ("unknown
            // subscription id"). Foxglove aggregates FrameTransform from ALL topics
            // into the TF tree, so a distinct topic still yields os_sensor->os_lidar.
            SetField(lidarPub, "_topic", "/tf_lidar");
            SetField(lidarPub, "_parentFrameId", "os_sensor");
            SetField(lidarPub, "_childFrameId", "os_lidar");
            SetField(lidarPub, "_useLocalTransform", true);

            // 5. Cart-mounted SLAM camera on the same sensor unit/profile clock.
            var cartCameraMount = new GameObject("CartCameraMount");
            Undo.RegisterCreatedObjectUndo(cartCameraMount, "Build Maze Demo");
            cartCameraMount.transform.SetParent(lidarImuUnit, false);
            ApplySensorChildTransform(cartCameraMount.transform, sensorUnit.EffectiveCameraToSensor);

            var sensorCam = cartCameraMount.AddComponent<Camera>();
            sensorCam.clearFlags = CameraClearFlags.Skybox;
            sensorCam.fieldOfView = 70f;
            sensorCam.nearClipPlane = 0.05f;
            sensorCam.farClipPlane = 80f;

            var sensorCamPub = cartCameraMount.AddComponent<FoxgloveCameraPublisher>();
            SetField(sensorCamPub, "_manager", manager);
            SetField(sensorCamPub, "_sensorUnitProfile", sensorUnit);
            SetField(sensorCamPub, "_useSharedSensorClock", true);
            SetField(sensorCamPub, "_publishStandardRos2CompressedImage", false);
            SetField(sensorCamPub, "_publishStandardRos2RawImage", false);
            SetField(sensorCamPub, "_topic", "/unity/sensor/camera/image/compressed");
            SetField(sensorCamPub, "_frameId", "os_camera");
            SetField(sensorCamPub, "_width", 640);
            SetField(sensorCamPub, "_height", 480);

            var sensorCamInfoPub = cartCameraMount.AddComponent<FoxgloveCameraInfoPublisher>();
            SetField(sensorCamInfoPub, "_manager", manager);
            SetField(sensorCamInfoPub, "_sourceCamera", sensorCam);
            SetField(sensorCamInfoPub, "_imagePublisher", sensorCamPub);
            SetField(sensorCamInfoPub, "_sensorUnitProfile", sensorUnit);
            SetField(sensorCamInfoPub, "_useSharedSensorClock", true);
            SetField(sensorCamInfoPub, "_publishCameraTfAnchor", true);
            SetField(sensorCamInfoPub, "_topic", "/unity/sensor/camera/camera_info");
            SetField(sensorCamInfoPub, "_frameId", "os_camera");
            sensorCamInfoPub.enabled = false;
            cartCameraMount.SetActive(false);

            var replayAdapter = ConfigureReplayAdapter(
                mgrGo,
                manager,
                vehicleGo.transform,
                lidarImuUnit,
                lidarMount,
                imuMount.transform,
                cartCameraMount.transform);

            // 6. Static overview camera framing the whole maze for the Unity Game view.
            var camGo = new GameObject("DemoCamera");
            Undo.RegisterCreatedObjectUndo(camGo, "Build Maze Demo");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.transform.position = new Vector3(0f, 20f, -18f);
            camGo.transform.LookAt(Vector3.zero);

            var demoCameraPublisher = camGo.AddComponent<FoxgloveCameraPublisher>();
            SetField(demoCameraPublisher, "_manager", manager);
            SetField(demoCameraPublisher, "_topic", "/unity/camera");
            SetField(demoCameraPublisher, "_frameId", "unity_camera");
            SetField(demoCameraPublisher, "_width", 640);
            SetField(demoCameraPublisher, "_height", 480);
            SetField(demoCameraPublisher, "_publishStandardRos2CompressedImage", false);
            SetField(demoCameraPublisher, "_publishStandardRos2RawImage", false);

            foreach (var dirty in new Object[] { manager, publisher, controller, basePub, lidar, unitPub, imu, imuPub, lidarPub, sensorCamPub, sensorCamInfoPub, replayAdapter, demoCameraPublisher })
                EditorUtility.SetDirty(dirty);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("[LidarMaze] Maze demo scene built. In Foxglove, set the 3D panel " +
                      "Display frame to 'map'. Press Play and drive with WASD.");
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

        private static FoxgloveReplayObjectAdapter ConfigureReplayAdapter(
            GameObject host,
            FoxgloveManager manager,
            Transform vehicle,
            Transform sensorUnit,
            Transform lidarMount,
            Transform imuMount,
            Transform cameraMount)
        {
            var adapter = host.AddComponent<FoxgloveReplayObjectAdapter>();
            SetField(adapter, "_manager", manager);
            SetField(adapter, "_frameOverrides", new[]
            {
                new FoxgloveReplayObjectAdapter.FrameMapping { ChildFrameId = "base_link", Target = vehicle },
                new FoxgloveReplayObjectAdapter.FrameMapping { ChildFrameId = "os_sensor", Target = sensorUnit },
                new FoxgloveReplayObjectAdapter.FrameMapping { ChildFrameId = "os_lidar", Target = lidarMount },
                new FoxgloveReplayObjectAdapter.FrameMapping { ChildFrameId = "os_imu", Target = imuMount },
                new FoxgloveReplayObjectAdapter.FrameMapping { ChildFrameId = "os_camera", Target = cameraMount }
            });
            return adapter;
        }

        private static void SetField(object target, string fieldName, object value)
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

            if (field == null)
                throw new System.MissingFieldException(target.GetType().FullName, fieldName);

            field.SetValue(target, value);
        }

    }
}
