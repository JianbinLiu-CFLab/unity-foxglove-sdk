// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/FullDemoVisualization
// Purpose: Mouse-driven cube control demo — drag to rotate/pan, scroll to scale, synced to Foxglove parameters.

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Mouse-driven cube control for demo.
/// Left-drag: rotate. Right-drag: pan. Scroll: scale.
/// If a FoxgloveDemoSetup is provided, syncs scale to Foxglove parameter.
/// </summary>
public class MouseDragCube : MonoBehaviour
{
    [SerializeField] private float _rotateSpeed = 3f;
    [SerializeField] private float _panSpeed = 0.01f;
    [SerializeField] private float _scaleSpeed = 0.5f;
    [SerializeField] private float _minScale = FoxgloveDemoSetup.ScaleMinimum;
    [SerializeField] private float _maxScale = FoxgloveDemoSetup.ScaleMaximum;
    [SerializeField] private FoxgloveDemoSetup _demo;

    private Vector2 _lastMouse;
    private bool _isDragging;

    /// <summary>
    /// Finds the <c>FoxgloveDemoSetup</c> if not assigned.
    /// </summary>
    private void Awake()
    {
        if (_demo == null)
            _demo = FindFirstObjectByType<FoxgloveDemoSetup>();
    }

    /// <summary>
    /// Each frame, reads mouse input: left-drag rotates, right-drag
    /// pans, scroll scales. Scale changes are synced back to Foxglove
    /// via <c>FoxgloveDemoSetup</c>.
    /// </summary>
    private void Update()
    {
        var cam = Camera.main;
        if (cam == null) return;

        if (!TryReadMouse(out var pos, out var leftPressed, out var rightPressed, out var scroll))
            return;

        var dragging = leftPressed || rightPressed;

        if (!dragging)
        {
            _isDragging = false;
            _lastMouse = pos;
        }

        var delta = Vector2.zero;
        if (dragging)
        {
            if (!_isDragging)
            {
                _isDragging = true;
                _lastMouse = pos;
            }
            else
            {
                delta = pos - _lastMouse;
                _lastMouse = pos;
            }
        }

        if (leftPressed)
        {
            transform.Rotate(cam.transform.up, -delta.x * _rotateSpeed * 0.1f, Space.World);
            transform.Rotate(cam.transform.right, delta.y * _rotateSpeed * 0.1f, Space.World);
        }

        if (rightPressed)
        {
            var right = cam.transform.right * delta.x * _panSpeed;
            var up = cam.transform.up * delta.y * _panSpeed;
            transform.position += right + up;
        }

        if (scroll != 0)
        {
            var s = Mathf.Clamp(transform.localScale.x + scroll * _scaleSpeed, _minScale, _maxScale);
            transform.localScale = new Vector3(s, s, s);

            // Sync to Foxglove parameter
            if (_demo != null)
                _demo.SyncScaleToParameter(s);
        }
    }

    private static bool TryReadMouse(out Vector2 position, out bool leftPressed, out bool rightPressed, out float scroll)
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        if (mouse == null)
        {
            position = default;
            leftPressed = false;
            rightPressed = false;
            scroll = 0f;
            return false;
        }

        position = mouse.position.ReadValue();
        leftPressed = mouse.leftButton.isPressed;
        rightPressed = mouse.rightButton.isPressed;
        scroll = mouse.scroll.ReadValue().y;
        return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        position = Input.mousePosition;
        leftPressed = Input.GetMouseButton(0);
        rightPressed = Input.GetMouseButton(1);
        scroll = Input.mouseScrollDelta.y;
        return true;
#else
        position = default;
        leftPressed = false;
        rightPressed = false;
        scroll = 0f;
        return false;
#endif
    }
}
