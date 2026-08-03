// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/FullDemoVisualization
// Purpose: Demo MonoBehaviour with [FoxRun] source-generated attributes for automatic position/health publishing.

using UnityEngine;
using Unity.FoxgloveSDK.Components;

/// <summary>
/// Demo MonoBehaviour that publishes position and health fields
/// automatically via <c>[FoxRun]</c> source-generated attributes.
/// </summary>
public partial class TestLog : MonoBehaviour
{
    [SerializeField] private Transform _trackedCube;
    private bool _warnedMissingTrackedCube;

    // Minimum form: one topic path, default Publish flow, FixedRate policy,
    // configured/frozen Publish Profile cadence (10 Hz by default), and
    // profile-selected Targets.
    [FoxRun("/debug/position")]
    private Vector3 _pos;

    // Hz overrides this topic's scheduled publish rate to 5 Hz.
    [FoxRun("/debug/health", Hz = 5)]
    private float _health = 100f;

    // Change-driven options:
    // - Policy = Change publishes semantic changes.
    // - Tolerance suppresses tiny Vector jitter.
    // - Hz = 1 adds a one-second heartbeat.
    [FoxRun("/debug/position2", Policy = FoxRunPolicy.Change, Hz = 1, Tolerance = 0.01f)]
    private Vector3 _position2;

    // Conditional publish gates.
    // Toggle this in the Inspector: false stops /debug/conditional_position,
    // true allows it to publish again.
    public bool telemetryEnabled = true;

    // Toggle this in the Inspector: true suppresses /debug/conditional_health,
    // false allows it to publish again.
    public bool isPaused = false;
    private bool healthPublishingEnabled => !isPaused;

    partial void UpdateMessagePackProbe();

    [FoxRun("/debug/conditional_position", Hz = 15, OnlyIf = nameof(telemetryEnabled))]
    public Vector3 conditionalPosition;

    [FoxRun("/debug/conditional_health", Hz = 15, OnlyIf = nameof(healthPublishingEnabled))]
    public int conditionalHealth = 100;

    void Awake()
    {
        if (_trackedCube != null)
            return;

        var cube = GameObject.Find("Cube");
        _trackedCube = cube != null ? cube.transform : transform;
        if (cube == null && !_warnedMissingTrackedCube)
        {
            Debug.LogWarning("TestLog tracked cube is not assigned and no active GameObject named 'Cube' was found; publishing this transform instead.");
            _warnedMissingTrackedCube = true;
        }
    }

    /// <summary>
    /// Each frame, updates <c>_pos</c> from the Transform so the
    /// Foxglove publisher sees the latest position.
    /// </summary>
    void Update()
    {
        var trackedPosition = _trackedCube != null ? _trackedCube.position : transform.position;
        _pos = trackedPosition;
        _position2 = trackedPosition;
        _health = 95f + Mathf.Sin(Time.time * 0.75f) * 5f;
        conditionalPosition = trackedPosition;
        conditionalHealth = Mathf.RoundToInt(_health);
        UpdateMessagePackProbe();
    }
}
