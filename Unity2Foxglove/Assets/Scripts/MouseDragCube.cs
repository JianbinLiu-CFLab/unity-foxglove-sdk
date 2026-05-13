// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/FullDemoVisualization
// Purpose: Mouse-driven cube control demo — drag to rotate/pan, scroll to scale, synced to Foxglove parameters.

using UnityEngine;
using UnityEngine.InputSystem;

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

        var mouse = Mouse.current;
        if (mouse == null) return;

        var pos = mouse.position.ReadValue();
        var leftPressed = mouse.leftButton.isPressed;
        var rightPressed = mouse.rightButton.isPressed;
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

        var scroll = mouse.scroll.ReadValue().y;
        if (scroll != 0)
        {
            var s = Mathf.Clamp(transform.localScale.x + scroll * _scaleSpeed, _minScale, _maxScale);
            transform.localScale = new Vector3(s, s, s);

            // Sync to Foxglove parameter
            if (_demo != null)
                _demo.SyncScaleToParameter(s);
        }
    }
}
