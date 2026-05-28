// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR Maze Demo

using UnityEngine;

namespace Unity.FoxgloveSDK.Samples.LidarMaze
{
    /// <summary>
    /// Simple kinematic vehicle controller using Rigidbody.MovePosition/MoveRotation.
    /// Supports WASD input or deterministic auto-wander mode.
    /// </summary>
    public class Phase138LidarVehicleController : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 1.5f;
        [SerializeField] private float _turnRateDegPerSec = 90f;
        [SerializeField] private KeyCode _forwardKey = KeyCode.W;
        [SerializeField] private KeyCode _backKey = KeyCode.S;
        [SerializeField] private KeyCode _turnLeftKey = KeyCode.A;
        [SerializeField] private KeyCode _turnRightKey = KeyCode.D;
        [SerializeField] private bool _useAutoWander;

        private Rigidbody _rb;
        private Vector3 _wanderDirection = Vector3.forward;
        private float _nextWanderChange;
        private static bool s_legacyInputWarningEmitted;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.constraints =
                    RigidbodyConstraints.FreezePositionY |
                    RigidbodyConstraints.FreezeRotationX |
                    RigidbodyConstraints.FreezeRotationZ;
            }
        }

        private void Update()
        {
            if (_useAutoWander)
                UpdateAutoWander();
            else
                UpdateWASD();
        }

        private void UpdateWASD()
        {
            if (!InputManagerAvailable())
            {
                if (!s_legacyInputWarningEmitted)
                {
                    Debug.LogWarning("[LidarMaze] Legacy Input Manager is disabled; vehicle stays parked.");
                    s_legacyInputWarningEmitted = true;
                }
                return;
            }

            var move = Vector3.zero;
            var turn = 0f;

            if (Input.GetKey(_forwardKey))
                move += Vector3.forward * _moveSpeed;
            if (Input.GetKey(_backKey))
                move -= Vector3.forward * _moveSpeed;
            if (Input.GetKey(_turnLeftKey))
                turn -= _turnRateDegPerSec;
            if (Input.GetKey(_turnRightKey))
                turn += _turnRateDegPerSec;

            ApplyMovement(move, turn);
        }

        private void UpdateAutoWander()
        {
            // Rotate on wall contact to avoid getting stuck
            if (Time.time >= _nextWanderChange)
            {
                // Cast forward to detect wall
                var hitForward = Physics.Raycast(transform.position,
                    _wanderDirection, 1.2f, ~0, QueryTriggerInteraction.Ignore);

                if (hitForward)
                {
                    // Rotate 90 degrees randomly left or right
                    var angle = (Random.value > 0.5f ? 90f : -90f) *
                        (1f + Random.value * 0.3f);
                    _wanderDirection = Quaternion.Euler(0f, angle, 0f) * _wanderDirection;
                    _nextWanderChange = Time.time + 1.5f;
                }
            }

            // Move forward
            ApplyMovement(_wanderDirection * _moveSpeed, 0f);

            // Periodically randomize direction
            if (Time.time >= _nextWanderChange - 1.0f)
            {
                var jitter = Random.Range(-15f, 15f);
                _wanderDirection = Quaternion.Euler(0f, jitter, 0f) * _wanderDirection;
                _nextWanderChange = Time.time + 3f + Random.value * 2f;
            }
        }

        private void ApplyMovement(Vector3 move, float turnDeltaDeg)
        {
            if (_rb == null)
                return;

            // Turn
            if (Mathf.Abs(turnDeltaDeg) > 0.001f)
            {
                var turnDelta = Quaternion.Euler(0f, turnDeltaDeg * Time.deltaTime, 0f);
                _rb.MoveRotation(_rb.rotation * turnDelta);
            }

            // Move in local space
            var worldMove = transform.TransformDirection(move) * Time.deltaTime;
            _rb.MovePosition(_rb.position + worldMove);
        }

        private static bool InputManagerAvailable()
        {
            // Legacy Input Manager is always available in standalone Unity builds
            // unless the project is Input System-only with "Input System Package (New)" active.
            // A simple check: try reading any axis; if no axis exists in legacy, it returns 0,
            // but the real issue is when UnityEngine.Input class throws.
            try
            {
                var _ = Input.anyKey; // force touch the Input class
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
