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

        private bool _hasLastTargetPose;
        private Vector3 _lastTargetPosition;
        private Quaternion _lastTargetRotation;
        private bool _lastChaseMode;
        private Vector3 _lastOffset;

        private void Start()
        {
            if (_target == null)
                Debug.LogWarning("[LidarMaze] Camera follow target is not assigned.", this);
        }

        private void LateUpdate()
        {
            if (_target == null)
                return;

            if (_hasLastTargetPose
                && _target.position == _lastTargetPosition
                && _target.rotation == _lastTargetRotation
                && _chaseMode == _lastChaseMode
                && _offset == _lastOffset)
                return;

            _lastTargetPosition = _target.position;
            _lastTargetRotation = _target.rotation;
            _lastChaseMode = _chaseMode;
            _lastOffset = _offset;
            _hasLastTargetPose = true;

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
