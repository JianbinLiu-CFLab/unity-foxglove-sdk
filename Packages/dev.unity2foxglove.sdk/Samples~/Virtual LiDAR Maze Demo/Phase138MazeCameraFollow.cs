// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR Maze Demo

using UnityEngine;

namespace Unity.FoxgloveSDK.Samples.LidarMaze
{
    /// <summary>
    /// Simple top-down or chase camera that follows a target transform.
    /// </summary>
    public class Phase138MazeCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private Vector3 _offset = new Vector3(0, 15, 0);
        [SerializeField] private bool _chaseMode;

        private void LateUpdate()
        {
            if (_target == null)
                return;

            if (_chaseMode)
            {
                transform.position = _target.position
                    - _target.forward * 5f
                    + Vector3.up * 3f;
                transform.LookAt(_target);
            }
            else
            {
                transform.position = _target.position + _offset;
                transform.LookAt(_target);
            }
        }
    }
}
