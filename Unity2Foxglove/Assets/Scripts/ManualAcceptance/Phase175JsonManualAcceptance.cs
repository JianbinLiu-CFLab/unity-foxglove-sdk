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
    private const string LegacyJsonTopic = "/phase175/json/legacy-state";

    [Header("Explicit JSON Legacy")]
    [FoxRun(LegacyJsonTopic, Mode = FoxRunFlow.Subscribe, Encoding = FoxRunWireEncoding.Json)]
    [SerializeField] private float requestedLegacyJsonState;

    [Header("Observed State")]
    [SerializeField] private int receivedJsonMessageCount;
    [SerializeField] private float observedJsonRateHz;
    [SerializeField] private int appliedInboundCount;
    [SerializeField] private string lastStatus = "Waiting for JSON legacy input.";

    private FoxgloveManager _manager;
    private float _observedRequestedLegacyJsonState;
    private bool _hasObservedValue;
    private int _messagesInRateWindow;
    private double _rateWindowStartedAt = -1d;
    private double _lastMessageAt = -1d;

    private void OnEnable()
    {
        AttachManager();
    }

    private void OnDisable()
    {
        DetachManager();
    }

    private void Update()
    {
        AttachManager();
        if (_lastMessageAt >= 0d
            && Time.realtimeSinceStartupAsDouble - _lastMessageAt >= 1d)
        {
            observedJsonRateHz = 0f;
            _messagesInRateWindow = 0;
            _rateWindowStartedAt = -1d;
        }

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

    private void AttachManager()
    {
        if (_manager != null)
            return;

        _manager = FindFirstObjectByType<FoxgloveManager>();
        if (_manager != null)
            _manager.OnClientMessageWithEncoding += OnClientMessageWithEncoding;
    }

    private void DetachManager()
    {
        if (_manager != null)
            _manager.OnClientMessageWithEncoding -= OnClientMessageWithEncoding;
        _manager = null;
    }

    private void OnClientMessageWithEncoding(uint clientId, uint channelId, string topic, string encoding, byte[] payload)
    {
        if (!string.Equals(topic, LegacyJsonTopic, System.StringComparison.Ordinal)
            || !string.Equals(encoding, "json", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = Time.realtimeSinceStartupAsDouble;
        receivedJsonMessageCount++;
        _lastMessageAt = now;
        if (_rateWindowStartedAt < 0d)
            _rateWindowStartedAt = now;
        _messagesInRateWindow++;

        var elapsed = now - _rateWindowStartedAt;
        if (elapsed < 1d)
            return;

        observedJsonRateHz = (float)(_messagesInRateWindow / elapsed);
        _messagesInRateWindow = 0;
        _rateWindowStartedAt = now;
    }

    [ContextMenu("Reset JSON Legacy Acceptance Values")]
    private void ResetProbeValues()
    {
        requestedLegacyJsonState = 0f;
        receivedJsonMessageCount = 0;
        observedJsonRateHz = 0f;
        appliedInboundCount = 0;
        _hasObservedValue = false;
        _messagesInRateWindow = 0;
        _rateWindowStartedAt = -1d;
        _lastMessageAt = -1d;
        lastStatus = "Waiting for JSON legacy input.";
    }
}
