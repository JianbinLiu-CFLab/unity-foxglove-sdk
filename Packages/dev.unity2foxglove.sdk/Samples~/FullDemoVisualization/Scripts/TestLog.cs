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
    private Transform _trackedCube;

    // Minimal form uses only a topic path.
    // Publishes at the default fixed rate and uses the field value as payload.
    [FoxRun("/debug/position")]
    private Vector3 _pos;

    // RateHz is an option. It lowers this topic's scheduled publish rate to 5 Hz.
    [FoxRun("/debug/health", RateHz = 5)]
    private float _health = 100f;

    // Change-driven options:
    // - PublishMode = OnChangeOrInterval publishes changed values.
    // - ChangeEpsilon suppresses tiny Vector jitter.
    // - ForceIntervalSeconds still sends a heartbeat every second.
    [FoxRun("/debug/position2", RateHz = 10, PublishMode = FoxRunPublishMode.OnChangeOrInterval, ChangeEpsilon = 0.01f, ForceIntervalSeconds = 1f)]
    public Vector3 position;

    void Awake()
    {
        var cube = GameObject.Find("Cube");
        _trackedCube = cube != null ? cube.transform : transform;
    }

    /// <summary>
    /// Each frame, updates <c>_pos</c> from the Transform so the
    /// Foxglove publisher sees the latest position.
    /// </summary>
    void Update()
    {
        var trackedPosition = _trackedCube != null ? _trackedCube.position : transform.position;
        _pos = trackedPosition;
        position = trackedPosition;
        _health = 95f + Mathf.Sin(Time.time * 0.75f) * 5f;
    }
}
