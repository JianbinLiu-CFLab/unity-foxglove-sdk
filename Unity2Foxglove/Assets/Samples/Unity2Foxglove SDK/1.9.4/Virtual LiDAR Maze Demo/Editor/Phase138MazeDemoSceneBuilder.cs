// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR Maze Demo (Editor)

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
    /// <summary>
    /// Summary text for this member.
    /// </summary>

/// <summary>Summary text for this member.</summary>
    public static class Phase138MazeDemoSceneBuilder
    {
        private const int CellsX = 8;
        private const int CellsZ = 8;
        private const float CellSize = 2f;
        private const string FullFidelityModel = "OS-2-128";
        private const string FullFidelityMode = "2048x10";
        private const int FullFidelityPointCount = 128 * 2048;

        /// <summary>
        /// Summary text for this member.
        /// </summary>

        [MenuItem("Foxglove/Phase138/Build Maze Demo Scene")]
/// <summary>Summary text for this member.</summary>
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
            SetField(sensorUnit, "_model", FullFidelityModel);
            SetField(sensorUnit, "_mode", FullFidelityMode);

            var publisher = lidarImuUnit.gameObject.AddComponent<FoxglovePointCloudPublisher>();
            SetField(publisher, "_manager", manager);
            SetField(publisher, "_frameId", "os_lidar");
            SetField(publisher, "_maxPoints", FullFidelityPointCount);
            SetField(publisher, "_maxPackedBytes", 0);
            SetField(publisher, "_publishRateHz", 10f);
            SetField(publisher, "_nativeDracoPublishRateHz", 2f);
            SetField(publisher, "_samplingMode", Unity.FoxgloveSDK.Util.PointCloudSamplingMode.UniformStride);
            // Draco output: compresses the cloud and runs the encode on a worker thread
            // (lower bandwidth). Publishes foxglove.CompressedPointCloud on /unity/point_cloud_draco.
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

            // Optional: R2FU PointCloud2 mirror (Phase 138C smoke). Resolved by name
            // across loaded assemblies (the smoke lives in Assembly-CSharp, not this
            // editor assembly, so Type.GetType alone returns null). Added to the Vehicle
            // only when the R2FU sample is imported; the smoke auto-finds the VirtualLidar.
            var smokeType = FindTypeByName("Phase138VirtualLidarPointCloud2Smoke");
            if (smokeType != null)
            {
                var smoke = vehicleGo.AddComponent(smokeType);
                SetField(smoke, "_virtualLidar", lidar);
                EditorUtility.SetDirty(smoke);
            }
            else
            {
                Debug.Log("[LidarMaze] Phase138VirtualLidarPointCloud2Smoke not found " +
                          "(import the 'Virtual LiDAR PointCloud2 Digital Twin' R2FU sample to enable ROS2 output).");
            }

            // 5. Static overview camera framing the whole maze.
            var camGo = new GameObject("DemoCamera");
            Undo.RegisterCreatedObjectUndo(camGo, "Build Maze Demo");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.transform.position = new Vector3(0f, 20f, -18f);
            camGo.transform.LookAt(Vector3.zero);

            // Stream the overview camera to Foxglove as a JPEG image topic.
            var camPub = camGo.AddComponent<FoxgloveCameraPublisher>();
            SetField(camPub, "_manager", manager);
            SetField(camPub, "_topic", "/unity/camera");
            SetField(camPub, "_frameId", "unity_camera");

            foreach (var dirty in new Object[] { manager, publisher, controller, basePub, lidar, unitPub, imu, imuPub, lidarPub, camPub })
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

            if (field != null)
                field.SetValue(target, value);
            else
                Debug.LogWarning($"[LidarMaze] Failed to set private field '{fieldName}' on {target.GetType().Name}");
        }

        /// <summary>Find a type by simple name across all loaded assemblies (cross-assembly).</summary>
        private static System.Type FindTypeByName(string name)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name);
                if (t != null) return t;
            }
            return null;
        }
    }
}
