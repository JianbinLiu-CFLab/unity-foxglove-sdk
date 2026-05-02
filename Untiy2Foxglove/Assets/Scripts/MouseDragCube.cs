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
    [SerializeField] private float _minScale = 0.2f;
    [SerializeField] private float _maxScale = 5f;
    [SerializeField] private FoxgloveDemoSetup _demo;

    private Vector2 _lastMouse;

    private void Awake()
    {
        if (_demo == null)
            _demo = FindFirstObjectByType<FoxgloveDemoSetup>();
    }

    private void Update()
    {
        var cam = Camera.main;
        if (cam == null) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        var pos = mouse.position.ReadValue();
        var delta = pos - _lastMouse;
        _lastMouse = pos;

        if (mouse.leftButton.isPressed)
        {
            transform.Rotate(cam.transform.up, -delta.x * _rotateSpeed * 0.1f, Space.World);
            transform.Rotate(cam.transform.right, delta.y * _rotateSpeed * 0.1f, Space.World);
        }

        if (mouse.rightButton.isPressed)
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
