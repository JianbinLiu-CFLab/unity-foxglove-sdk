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
    [FoxRun("/debug/position")]
    private Vector3 _pos;

    [FoxRun("/debug/health", RateHz = 5)]
    private float _health = 100f;

    /// <summary>
    /// Each frame, updates <c>_pos</c> from the Transform so the
    /// Foxglove publisher sees the latest position.
    /// </summary>
    void Update() { _pos = transform.position; }
}
