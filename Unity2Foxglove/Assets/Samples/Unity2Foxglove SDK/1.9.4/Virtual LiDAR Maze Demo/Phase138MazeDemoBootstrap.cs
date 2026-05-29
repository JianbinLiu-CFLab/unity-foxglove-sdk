// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR Maze Demo

using System.Reflection;
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
    /// TF tree: map -> base_link (vehicle pose) -> vehicle_lidar (roof mount).
    /// In Foxglove set the 3D panel Display frame to "map" to watch the car drive
    /// through the static maze with the point cloud accumulating (Decay time > 0).
    /// </summary>
    public class Phase138MazeDemoBootstrap : MonoBehaviour
    {
        private static bool s_warnedManagerNull;

        private void Start()
        {
            Application.runInBackground = true;

            // 1. FoxgloveManager (RightHand so TF and point cloud share handedness)
            //    + point cloud publisher.
            var mgrGo = new GameObject("FoxgloveManager");
            var manager = mgrGo.AddComponent<FoxgloveManager>();
            SetPrivateField(manager, "_coordinateMode", CoordinateMode.RightHand);

            var publisher = mgrGo.AddComponent<FoxglovePointCloudPublisher>();
            publisher.enabled = false; // enable after verifying Runtime is ready

            // 2. Maze (centred on origin)
            Phase138MazeBuilder.Build(8, 8, 2f, 1.5f, 0.2f, 42);

            // 3. Vehicle (base_link) at maze start cell, with roof LiDAR mount.
            var start = Phase138MazeBuilder.CellCenter(0, 0, 8, 8, 2f);
            var vehicleGo = Phase138LidarVehicleController.BuildVehicle(start, out var lidarMount);

            var vehicleRb = vehicleGo.AddComponent<Rigidbody>();
            vehicleRb.useGravity = false;

            var controller = vehicleGo.AddComponent<Phase138LidarVehicleController>();
            SetPrivateField(controller, "_useAutoWander", false); // WASD control

            var basePublisher = vehicleGo.AddComponent<FoxgloveTransformPublisher>();
            SetPrivateField(basePublisher, "_manager", manager);
            SetPrivateField(basePublisher, "_topic", "/tf");
            SetPrivateField(basePublisher, "_parentFrameId", "map");
            SetPrivateField(basePublisher, "_childFrameId", "base_link");

            // 4. LiDAR on the roof mount (vehicle_lidar), static link under base_link.
            var lidar = lidarMount.gameObject.AddComponent<VirtualLidar>();
            SetPrivateField(lidar, "_frameId", "vehicle_lidar");
            SetPrivateField(lidar, "_pointCloudPublisher", publisher);
            SetPrivateField(lidar, "_columnStep", 4);
            SetPrivateField(lidar, "_publishEmptyFrames", false);
            SetPrivateField(lidar, "_drawDebugRays", false);

            var lidarPublisher = lidarMount.gameObject.AddComponent<FoxgloveTransformPublisher>();
            SetPrivateField(lidarPublisher, "_manager", manager);
            SetPrivateField(lidarPublisher, "_topic", "/tf");
            SetPrivateField(lidarPublisher, "_parentFrameId", "base_link");
            SetPrivateField(lidarPublisher, "_childFrameId", "vehicle_lidar");
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
                Debug.LogWarning("[LidarMaze] FoxgloveManager.Runtime is null — point cloud publisher stays disabled.");
                s_warnedManagerNull = true;
            }
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
