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
    /// maze, a primitive car with a roof LiDAR, the map -> base_link -> vehicle_lidar
    /// TF tree, a FoxgloveManager in RightHand mode, and an overview camera.
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

        [MenuItem("Foxglove/Phase138/Build Maze Demo Scene")]
        public static void BuildScene()
        {
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

            // 1. FoxgloveManager (RightHand) + point cloud publisher.
            var mgrGo = new GameObject("FoxgloveManager");
            Undo.RegisterCreatedObjectUndo(mgrGo, "Build Maze Demo");
            var manager = mgrGo.AddComponent<FoxgloveManager>();
            SetField(manager, "_coordinateMode", CoordinateMode.RightHand);
            var publisher = mgrGo.AddComponent<FoxglovePointCloudPublisher>();
            SetField(publisher, "_frameId", "vehicle_lidar");
            // Default to "no clipping": size the publish cap to the densest sensor in
            // the registry so any model fits (auto-scales as new LiDARs are added).
            // Uniform sampling keeps the cloud even if Max Points is later lowered.
            var publishCap = 4096;
            foreach (var spec in LidarModelRegistry.All)
            {
                var rc = LidarScanPatternFactory.Create(spec, "", 4).RayCount;
                if (rc > publishCap) publishCap = rc;
            }
            SetField(publisher, "_maxPoints", publishCap);
            SetField(publisher, "_samplingMode", Unity.FoxgloveSDK.Util.PointCloudSamplingMode.UniformStride);
            // Draco output: compresses the cloud and runs the encode on a worker thread
            // (lower bandwidth). Publishes foxglove.CompressedPointCloud on /unity/point_cloud_draco.
            SetField(publisher, "_outputMode", PointCloudOutputMode.Draco);
            SetField(publisher, "_topic", "/unity/point_cloud_draco");

            // 2. Maze centred on origin.
            var maze = Phase138MazeBuilder.Build(CellsX, CellsZ, CellSize, 1.5f, 0.2f, 42);
            Undo.RegisterCreatedObjectUndo(maze, "Build Maze Demo");

            // 3. Vehicle (base_link) at the start cell with a roof LiDAR mount.
            var start = Phase138MazeBuilder.CellCenter(0, 0, CellsX, CellsZ, CellSize);
            var vehicleGo = Phase138LidarVehicleController.BuildVehicle(start, out var lidarMount);
            Undo.RegisterCreatedObjectUndo(vehicleGo, "Build Maze Demo");

            var rb = vehicleGo.AddComponent<Rigidbody>();
            rb.useGravity = false;
            var controller = vehicleGo.AddComponent<Phase138LidarVehicleController>();
            SetField(controller, "_useAutoWander", false); // WASD control

            var basePub = vehicleGo.AddComponent<FoxgloveTransformPublisher>();
            SetField(basePub, "_manager", manager);
            SetField(basePub, "_topic", "/tf");
            SetField(basePub, "_parentFrameId", "map");
            SetField(basePub, "_childFrameId", "base_link");

            // 4. LiDAR on the roof mount (vehicle_lidar), static link under base_link.
            var lidar = lidarMount.gameObject.AddComponent<VirtualLidar>();
            SetField(lidar, "_frameId", "vehicle_lidar");
            SetField(lidar, "_pointCloudPublisher", publisher);
            SetField(lidar, "_columnStep", 4);

            var lidarPub = lidarMount.gameObject.AddComponent<FoxgloveTransformPublisher>();
            SetField(lidarPub, "_manager", manager);
            SetField(lidarPub, "_topic", "/tf");
            SetField(lidarPub, "_parentFrameId", "base_link");
            SetField(lidarPub, "_childFrameId", "vehicle_lidar");
            SetField(lidarPub, "_useLocalTransform", true);

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

            foreach (var dirty in new Object[] { manager, publisher, controller, basePub, lidar, lidarPub, camPub })
                EditorUtility.SetDirty(dirty);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("[LidarMaze] Maze demo scene built. In Foxglove, set the 3D panel " +
                      "Display frame to 'map'. Press Play and drive with WASD.");
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
    }
}
