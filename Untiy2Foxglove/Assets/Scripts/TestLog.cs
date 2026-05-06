// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Demo
// Purpose: Minimal [FoxRun] demo — auto-publishes /debug/position and
// /debug/health to Foxglove via FoxgloveLogHub and ISG-generated code.

using UnityEngine;
using Unity.FoxgloveSDK.Components;

public partial class TestLog : MonoBehaviour
{
    [FoxRun("/debug/position")]
    private Vector3 _pos;

    [FoxRun("/debug/health", RateHz = 5)]
    private float _health = 100f;

    void Update() { _pos = transform.position; }
}
