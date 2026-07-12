// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase175
// Purpose: Unity-side explicit-Protobuf inbound and bidirectional FoxRun smoke.

using Unity.FoxgloveSDK.Components;
using UnityEngine;

/// <summary>
/// Manual acceptance probe for generated FoxRun Protobuf input contracts.
/// </summary>
/// <remarks>
/// Add this component beside a running <see cref="FoxgloveManager"/> with
/// FoxRun inbound enabled. Publish Protobuf field <c>1</c> to either declared
/// topic and confirm the corresponding Inspector value and status update.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase175 FoxRun Protobuf Inbound Smoke")]
public partial class Phase175ProtobufManualAcceptance : MonoBehaviour
{
    [Header("Inbound Protobuf")]
    [FoxRun("/phase175/protobuf/target-value", Mode = FoxRunMode.SubscribeOnly, Encoding = FoxRunWireEncoding.Protobuf, ProtobufFieldNumber = 1)]
    [SerializeField] private float requestedTargetValue;
    [SerializeField] private float appliedTargetValue;

    [Header("Bidirectional Protobuf")]
#pragma warning disable FOXRUN026 // This probe treats Foxglove input as the remote-authoritative shared observation; generated PublishAndSubscribe suppresses its immediate echo.
    [FoxRun("/phase175/protobuf/shared-state", Mode = FoxRunMode.PublishAndSubscribe, Encoding = FoxRunWireEncoding.Protobuf, ProtobufFieldNumber = 1, RateHz = 2f)]
    [SerializeField] private float sharedState;
#pragma warning restore FOXRUN026

    [Header("Observed State")]
    [SerializeField] private int appliedInboundCount;
    [SerializeField] private string lastStatus = "Waiting for Protobuf input.";

    private float _observedRequestedTargetValue;
    private float _observedSharedState;
    private bool _hasObservedValues;

    private void Update()
    {
        if (!_hasObservedValues)
        {
            _observedRequestedTargetValue = requestedTargetValue;
            _observedSharedState = sharedState;
            _hasObservedValues = true;
            return;
        }

        var targetChanged = !Mathf.Approximately(requestedTargetValue, _observedRequestedTargetValue);
        var sharedChanged = !Mathf.Approximately(sharedState, _observedSharedState);
        if (!targetChanged && !sharedChanged)
            return;

        if (targetChanged)
        {
            _observedRequestedTargetValue = requestedTargetValue;
            appliedTargetValue = requestedTargetValue;
        }

        if (sharedChanged)
            _observedSharedState = sharedState;

        appliedInboundCount++;
        lastStatus = targetChanged
            ? "Applied Protobuf target value " + requestedTargetValue + "."
            : "Applied Protobuf shared state " + sharedState + ".";
    }

    [ContextMenu("Reset Protobuf Acceptance Values")]
    private void ResetProbeValues()
    {
        requestedTargetValue = 0f;
        appliedTargetValue = 0f;
        sharedState = 0f;
        appliedInboundCount = 0;
        _hasObservedValues = false;
        lastStatus = "Waiting for Protobuf input.";
    }
}
