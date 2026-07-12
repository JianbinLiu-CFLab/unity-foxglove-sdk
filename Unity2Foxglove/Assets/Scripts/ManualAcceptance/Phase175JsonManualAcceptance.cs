// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase175
// Purpose: Unity-side explicit-JSON legacy FoxRun smoke.

using Unity.FoxgloveSDK.Components;
using UnityEngine;

/// <summary>
/// Manual acceptance probe for an explicit JSON FoxRun input contract.
/// </summary>
/// <remarks>
/// Add this component beside a running <see cref="FoxgloveManager"/> with
/// FoxRun inbound enabled. Leave the Manager default at Protobuf, then publish
/// JSON with <c>requestedLegacyJsonState</c> to verify source-owned JSON precedence.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase175 FoxRun JSON Legacy Smoke")]
public sealed partial class Phase175JsonManualAcceptance : MonoBehaviour
{
    [Header("Explicit JSON Legacy")]
    [FoxRun("/phase175/json/legacy-state", Mode = FoxRunMode.SubscribeOnly, Encoding = FoxRunWireEncoding.Json)]
    [SerializeField] private float requestedLegacyJsonState;

    [Header("Observed State")]
    [SerializeField] private int appliedInboundCount;
    [SerializeField] private string lastStatus = "Waiting for JSON legacy input.";

    private float _observedRequestedLegacyJsonState;
    private bool _hasObservedValue;

    private void Update()
    {
        if (!_hasObservedValue)
        {
            _observedRequestedLegacyJsonState = requestedLegacyJsonState;
            _hasObservedValue = true;
            return;
        }

        if (Mathf.Approximately(requestedLegacyJsonState, _observedRequestedLegacyJsonState))
            return;

        _observedRequestedLegacyJsonState = requestedLegacyJsonState;
        appliedInboundCount++;
        lastStatus = "Applied JSON legacy state " + requestedLegacyJsonState.ToString("R") + ".";
    }

    [ContextMenu("Reset JSON Legacy Acceptance Values")]
    private void ResetProbeValues()
    {
        requestedLegacyJsonState = 0f;
        appliedInboundCount = 0;
        _hasObservedValue = false;
        lastStatus = "Waiting for JSON legacy input.";
    }
}
