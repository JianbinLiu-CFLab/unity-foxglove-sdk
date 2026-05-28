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
    /// Bootstrap entry point for the Virtual LiDAR Maze Demo.
    /// Creates FoxgloveManager, maze, vehicle with VirtualLidar, and camera at runtime.
    /// </summary>
    public class Phase138MazeDemoBootstrap : MonoBehaviour
    {
        private static bool s_warnedManagerNull;

        private void Start()
        {
            Application.runInBackground = true;

            // 1. Create FoxgloveManager + FoxglovePointCloudPublisher
            var mgrGo = new GameObject("FoxgloveManager");
            var manager = mgrGo.AddComponent<FoxgloveManager>();
            var publisher = mgrGo.AddComponent<FoxglovePointCloudPublisher>();
            publisher.enabled = false; // enable after verifying Runtime is ready

            // 2. Create maze
            var mazeGo = new GameObject("MazeBuilder");
            var mazeBuilder = mazeGo.AddComponent<Phase138MazeBuilder>();

            // 3. Create vehicle: Cube + Rigidbody + VirtualLidar + controller
            // Place at maze origin (default 8x8 grid, cellSize=2) = cell (0,0) world position
            var vehicleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicleGo.name = "Vehicle";
            vehicleGo.transform.position = new Vector3(-7f, 0.5f, -7f);
            vehicleGo.transform.localScale = Vector3.one;

            // Replace existing collider with one that's not a trigger
            var existingCollider = vehicleGo.GetComponent<BoxCollider>();
            if (existingCollider != null)
            {
                existingCollider.isTrigger = false;
            }

            var vehicleRb = vehicleGo.AddComponent<Rigidbody>();

            var lidar = vehicleGo.AddComponent<VirtualLidar>();
            SetPrivateField(lidar, "_frameId", "vehicle_lidar");
            SetPrivateField(lidar, "_pointCloudPublisher", publisher);
            SetPrivateField(lidar, "_columnStep", 4);

            var controller = vehicleGo.AddComponent<Phase138LidarVehicleController>();

            // 4. Create camera
            var cameraGo = new GameObject("DemoCamera");
            var cam = cameraGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            var camFollow = cameraGo.AddComponent<Phase138MazeCameraFollow>();

            // Use reflection to set the private _target field on the camera follow
            typeof(Phase138MazeCameraFollow)
                .GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(camFollow, vehicleGo.transform);

            // 5. Verify FoxgloveManager.Runtime
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
            var field = target.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                field.SetValue(target, value);
            else
                Debug.LogWarning($"[LidarMaze] Failed to set private field '{fieldName}' on {target.GetType().Name}");
        }
    }
}
