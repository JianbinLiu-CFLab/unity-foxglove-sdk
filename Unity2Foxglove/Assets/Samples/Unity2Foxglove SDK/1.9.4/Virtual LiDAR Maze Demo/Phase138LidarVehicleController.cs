// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR Maze Demo

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Unity.FoxgloveSDK.Samples.LidarMaze
{
    /// <summary>
    /// Velocity-driven vehicle controller: sets Rigidbody.linearVelocity each
    /// FixedUpdate so the car stops the instant keys are released (no inertial
    /// drift) while still being blocked by static wall colliders. Supports WASD
    /// input or deterministic auto-wander, on both the new Input System and the
    /// legacy Input Manager.
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

        private void FixedUpdate()
        {
            if (_rb == null)
                return;

            Vector3 worldVelocity;
            float turnDeg;
            if (_useAutoWander)
                ComputeAutoWander(out worldVelocity, out turnDeg);
            else
                ComputeWASD(out worldVelocity, out turnDeg);

            // Drive by setting velocity directly: releasing the keys yields zero
            // velocity (instant stop, no inertial drift) while static wall colliders
            // still block a non-kinematic body. Clear residual spin from turning.
            _rb.angularVelocity = Vector3.zero;
            if (Mathf.Abs(turnDeg) > 0.001f)
                _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, turnDeg * Time.fixedDeltaTime, 0f));

            _rb.linearVelocity = new Vector3(worldVelocity.x, 0f, worldVelocity.z);
        }

        private void ComputeWASD(out Vector3 worldVelocity, out float turnDeg)
        {
            var forward = (Held(_forwardKey) ? 1f : 0f) - (Held(_backKey) ? 1f : 0f);
            var turn = (Held(_turnRightKey) ? 1f : 0f) - (Held(_turnLeftKey) ? 1f : 0f);
            worldVelocity = transform.forward * (forward * _moveSpeed);
            turnDeg = turn * _turnRateDegPerSec;
        }

        private void ComputeAutoWander(out Vector3 worldVelocity, out float turnDeg)
        {
            // Rotate on wall contact to avoid getting stuck.
            if (Time.time >= _nextWanderChange)
            {
                var hitForward = Physics.Raycast(transform.position,
                    _wanderDirection, 1.2f, ~0, QueryTriggerInteraction.Ignore);
                if (hitForward)
                {
                    var angle = (Random.value > 0.5f ? 90f : -90f) * (1f + Random.value * 0.3f);
                    _wanderDirection = Quaternion.Euler(0f, angle, 0f) * _wanderDirection;
                    _nextWanderChange = Time.time + 1.5f;
                }
            }

            if (Time.time >= _nextWanderChange - 1.0f)
            {
                var jitter = Random.Range(-15f, 15f);
                _wanderDirection = Quaternion.Euler(0f, jitter, 0f) * _wanderDirection;
                _nextWanderChange = Time.time + 3f + Random.value * 2f;
            }

            worldVelocity = _wanderDirection.normalized * _moveSpeed;
            turnDeg = 0f;
        }

        /// <summary>True while the given key is held, using whichever input backend is active.</summary>
        private static bool Held(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            var control = kb[ToKey(key)];
            return control != null && control.isPressed;
#else
            return Input.GetKey(key);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static Key ToKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.W: return Key.W;
                case KeyCode.A: return Key.A;
                case KeyCode.S: return Key.S;
                case KeyCode.D: return Key.D;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                default: return Key.None;
            }
        }
#endif

        /// <summary>
        /// Build a small car from primitives (body + cabin + 4 wheels) centred at
        /// <paramref name="position"/>, with wheels resting on y=0. Returns the root
        /// and outputs a roof-mounted transform for the LiDAR sensor.
        /// Safe to call at runtime or from an editor tool.
        /// </summary>
        public static GameObject BuildVehicle(Vector3 position, out Transform lidarMount)
        {
            var root = new GameObject("Vehicle");
            root.transform.position = position;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.3f, 1f);
            StripCollider(body);
            SetColor(body, new Color(0.85f, 0.16f, 0.12f)); // red body

            var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Cabin";
            cabin.transform.SetParent(root.transform, false);
            cabin.transform.localPosition = new Vector3(0f, 0.62f, -0.1f);
            cabin.transform.localScale = new Vector3(0.5f, 0.25f, 0.45f);
            StripCollider(cabin);
            SetColor(cabin, new Color(0.45f, 0.75f, 0.95f)); // light-blue cabin

            // Front marker so the heading (+Z) is obvious while driving.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.4f, 0.52f);
            nose.transform.localScale = new Vector3(0.5f, 0.18f, 0.12f);
            StripCollider(nose);
            SetColor(nose, new Color(0.98f, 0.85f, 0.1f)); // yellow nose

            var wheelPositions = new[]
            {
                new Vector3(0.33f, 0.25f, 0.32f),
                new Vector3(-0.33f, 0.25f, 0.32f),
                new Vector3(0.33f, 0.25f, -0.32f),
                new Vector3(-0.33f, 0.25f, -0.32f),
            };
            foreach (var wp in wheelPositions)
            {
                var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = "Wheel";
                wheel.transform.SetParent(root.transform, false);
                wheel.transform.localPosition = wp;
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheel.transform.localScale = new Vector3(0.5f, 0.05f, 0.5f);
                StripCollider(wheel);
                SetColor(wheel, new Color(0.1f, 0.1f, 0.1f));
            }

            // Roof heading arrow (+Z) so front/back is always obvious.
            BuildHeadingArrow(root.transform, 0.95f, new Color(0.2f, 1f, 0.3f));

            // Single body collider on the root for wall physics.
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.45f, 0f);
            col.size = new Vector3(0.7f, 0.6f, 1.1f);

            var mount = new GameObject("LidarMount");
            mount.transform.SetParent(root.transform, false);
            mount.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            lidarMount = mount.transform;

            return root;
        }

        /// <summary>
        /// Build a flat forward-pointing arrow (shaft + chevron head along +Z) as a
        /// child of <paramref name="parent"/>, floating at local height <paramref name="y"/>.
        /// </summary>
        private static void BuildHeadingArrow(Transform parent, float y, Color color)
        {
            var arrow = new GameObject("HeadingArrow");
            arrow.transform.SetParent(parent, false);
            arrow.transform.localPosition = new Vector3(0f, y, 0f);

            void Bar(Vector3 pos, float yawDeg, Vector3 scale)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "ArrowBar";
                b.transform.SetParent(arrow.transform, false);
                b.transform.localPosition = pos;
                b.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
                b.transform.localScale = scale;
                StripCollider(b);
                SetColor(b, color);
            }

            // Shaft along +Z, then two chevron wings meeting at the tip (z = 0.35).
            Bar(new Vector3(0f, 0f, 0.05f), 0f, new Vector3(0.07f, 0.05f, 0.4f));
            Bar(new Vector3(0.09f, 0f, 0.26f), 135f, new Vector3(0.07f, 0.05f, 0.26f));
            Bar(new Vector3(-0.09f, 0f, 0.26f), -135f, new Vector3(0.07f, 0.05f, 0.26f));
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c == null) return;
            if (Application.isPlaying)
                Destroy(c);
            else
                DestroyImmediate(c);
        }

        private static void SetColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            // Clone the primitive's default material so we inherit a shader that
            // matches the active render pipeline (URP/HDRP/Built-in). Picking a
            // hard-coded "Standard" shader renders magenta under URP. Tint via the
            // built-in (_Color) and URP/HDRP (_BaseColor) main-color properties.
            var mat = new Material(renderer.sharedMaterial) { color = color };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            renderer.sharedMaterial = mat;
        }
    }
}
