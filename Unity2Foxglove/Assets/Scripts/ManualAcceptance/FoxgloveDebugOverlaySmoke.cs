// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: FoxRun debug overlay manual smoke component.

using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

/// <summary>
/// Manual Unity/Foxglove smoke test for the FoxRun debug overlay helper.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Add this component to any enabled GameObject in a scene that also has a
///    running <see cref="FoxgloveManager"/>.
/// 2. Enter Play Mode and connect Foxglove to ws://127.0.0.1:8765.
/// 3. Confirm that the default <c>/debug/overlay_smoke</c> topic
///    appears as schemaless JSON with version/kind/source/label/values fields.
/// 4. Enable <c>Invalid Topic Should Not Publish</c> in the Inspector during
///    Play Mode. While any negative probe is enabled, the valid overlay stream
///    is paused. The probe should keep reporting <c>false</c>, and no
///    <c>/robot/overlay_smoke</c> topic should appear.
/// 5. Enable <c>Binary Value Should Not Publish</c>. The probe should keep
///    reporting <c>false</c>, and the valid overlay frame counter should stop
///    increasing while the probe is enabled.
/// 6. Recheck <c>Assets/Generated/FoxRun/foxrun.manifest.hash</c>. This smoke
///    component uses a non-contract debug overlay path and should not change
///    the FoxRun manifest hash.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Debug Overlay Smoke")]
public sealed class FoxgloveDebugOverlaySmoke : MonoBehaviour
{
    private const string InvalidTopicProbe = "/robot/overlay_smoke";

    [Header("Manager")]
    [Tooltip("Optional explicit manager. When empty, the component finds the first FoxgloveManager in the active scene.")]
    [SerializeField] private FoxgloveManager _manager;
    [Tooltip("Automatically find a FoxgloveManager when the explicit Manager field is empty.")]
    [SerializeField] private bool _autoFindManager = true;

    [Header("Valid Overlay Publish")]
    [Tooltip("The valid debug overlay topic. Keep the /debug/ prefix for the positive smoke path.")]
    [SerializeField] private string _topic = "/debug/overlay_smoke";
    [Tooltip("Human-readable source name written into the overlay envelope.")]
    [SerializeField] private string _source = nameof(FoxgloveDebugOverlaySmoke);
    [Tooltip("Optional short label written into the overlay envelope.")]
    [SerializeField] private string _label = "manual unity smoke";
    [Tooltip("How often the valid overlay sample is published while Play Mode is running.")]
    [SerializeField, Min(0.05f)] private float _publishIntervalSeconds = 0.5f;
    [Tooltip("Disable normal valid-topic publishing while keeping the rejection probes available.")]
    [SerializeField] private bool _disableNormalPublish;

    [Header("Negative Probes")]
    [Tooltip("When enabled during Play Mode, pauses the valid stream and repeatedly attempts to publish to a non-/debug/ topic. Expected result: false and no /robot/overlay_smoke topic.")]
    [SerializeField] private bool _invalidTopicShouldNotPublish;
    [Tooltip("When enabled during Play Mode, pauses the valid stream and repeatedly attempts to publish a byte[] value. Expected result: false and no binary/blob overlay message.")]
    [SerializeField] private bool _binaryValueShouldNotPublish;

    [Header("Observed State")]
    [SerializeField] private int _publishedCount;
    [SerializeField] private bool _validOverlayStreamBlocked;
    [SerializeField] private bool _lastPublishAccepted;
    [SerializeField] private bool _lastInvalidTopicAccepted;
    [SerializeField] private bool _lastBinaryProbeAccepted;
    [SerializeField] private string _lastStatus = "Not started.";

    private float _nextPublishTime;
    private bool _loggedFirstPublish;
    private bool _loggedInvalidTopicProbe;
    private bool _loggedBinaryRejectionProbe;

    /// <summary>
    /// True when the component is intentionally verifying that overlay data is blocked.
    /// </summary>
    private bool NegativePublishBlockEnabled
    {
        get { return _invalidTopicShouldNotPublish || _binaryValueShouldNotPublish; }
    }

    /// <summary>
    /// Initializes the component state each time Play Mode enables the smoke.
    /// </summary>
    private void OnEnable()
    {
        Application.runInBackground = true;
        _nextPublishTime = 0f;
        _publishedCount = 0;
        _validOverlayStreamBlocked = false;
        _lastPublishAccepted = false;
        _lastInvalidTopicAccepted = false;
        _lastBinaryProbeAccepted = false;
        _loggedFirstPublish = false;
        _loggedInvalidTopicProbe = false;
        _loggedBinaryRejectionProbe = false;
        _lastStatus = "Waiting for Play Mode publish.";
    }

    /// <summary>
    /// Drives the positive overlay stream and optional one-shot rejection probes.
    /// </summary>
    private void Update()
    {
        if (!TryResolveManager())
            return;

        if (_invalidTopicShouldNotPublish)
        {
            if (!_loggedInvalidTopicProbe)
                RunInvalidTopicProbe();
        }
        else
        {
            _loggedInvalidTopicProbe = false;
        }

        if (_binaryValueShouldNotPublish)
        {
            if (!_loggedBinaryRejectionProbe)
                RunBinaryRejectionProbe();
        }
        else
        {
            _loggedBinaryRejectionProbe = false;
        }

        _validOverlayStreamBlocked = NegativePublishBlockEnabled || _disableNormalPublish;

        if (NegativePublishBlockEnabled)
        {
            _lastPublishAccepted = false;
            _nextPublishTime = Time.unscaledTime + _publishIntervalSeconds;
            return;
        }

        if (!_disableNormalPublish)
            PublishValidOverlayIfDue();
    }

    /// <summary>
    /// Keeps Inspector-edited numeric settings inside a safe range.
    /// </summary>
    private void OnValidate()
    {
        _publishIntervalSeconds = Mathf.Max(0.05f, _publishIntervalSeconds);
    }

    /// <summary>
    /// Runs the valid overlay publish once from the Inspector context menu.
    /// </summary>
    [ContextMenu("Debug Overlay/Publish Valid Overlay Once")]
    public void PublishValidOverlayOnce()
    {
        if (!TryResolveManager())
            return;

        if (NegativePublishBlockEnabled)
        {
            _lastPublishAccepted = false;
            _lastStatus = "Valid overlay publish is blocked while a negative probe is enabled.";
            Debug.Log("[FoxgloveDebugOverlaySmoke] " + _lastStatus);
            return;
        }

        PublishValidOverlay();
    }

    /// <summary>
    /// Attempts to publish to a non-/debug/ topic. The expected result is false.
    /// </summary>
    [ContextMenu("Debug Overlay/Run Invalid Topic Probe")]
    public void RunInvalidTopicProbe()
    {
        if (!TryResolveManager())
            return;

        _lastInvalidTopicAccepted = FoxgloveDebugOverlay.PublishValue(
            _manager,
            InvalidTopicProbe,
            _source,
            "frame",
            _publishedCount,
            "invalid topic probe");

        _lastStatus = "Invalid topic probe accepted=" + _lastInvalidTopicAccepted
                      + " (expected false). Valid overlay stream is paused.";
        if (!_loggedInvalidTopicProbe || _lastInvalidTopicAccepted)
        {
            _loggedInvalidTopicProbe = true;
            Debug.Log("[FoxgloveDebugOverlaySmoke] " + _lastStatus);
        }
    }

    /// <summary>
    /// Attempts to publish a byte array. The expected result is false.
    /// </summary>
    [ContextMenu("Debug Overlay/Run Binary Rejection Probe")]
    public void RunBinaryRejectionProbe()
    {
        if (!TryResolveManager())
            return;

        _lastBinaryProbeAccepted = FoxgloveDebugOverlay.PublishValue(
            _manager,
            _topic,
            _source,
            "payload",
            new byte[] { 1, 2, 3 },
            "binary rejection probe");

        _lastStatus = "Binary rejection probe accepted=" + _lastBinaryProbeAccepted
                      + " (expected false). Valid overlay stream is paused.";
        if (!_loggedBinaryRejectionProbe || _lastBinaryProbeAccepted)
        {
            _loggedBinaryRejectionProbe = true;
            Debug.Log("[FoxgloveDebugOverlaySmoke] " + _lastStatus);
        }
    }

    /// <summary>
    /// Resolves the manager reference and records a clear status when it is unavailable.
    /// </summary>
    private bool TryResolveManager()
    {
        if (_manager == null && _autoFindManager)
            _manager = Object.FindFirstObjectByType<FoxgloveManager>();

        if (_manager != null)
            return true;

        _lastStatus = "No FoxgloveManager found in the active scene.";
        return false;
    }

    /// <summary>
    /// Publishes the valid overlay stream when the configured interval elapses.
    /// </summary>
    private void PublishValidOverlayIfDue()
    {
        var now = Time.unscaledTime;
        if (now < _nextPublishTime)
            return;

        _nextPublishTime = now + _publishIntervalSeconds;
        PublishValidOverlay();
    }

    /// <summary>
    /// Publishes one valid debug overlay sample and updates Inspector-visible state.
    /// </summary>
    private void PublishValidOverlay()
    {
        var nextCount = _publishedCount + 1;
        _lastPublishAccepted = FoxgloveDebugOverlay.Publish(
            _manager,
            _topic,
            _source,
            BuildValues(nextCount),
            _label);

        if (!_lastPublishAccepted)
        {
            _lastStatus = "Valid overlay publish returned false. Check topic prefix, manager state, and Play Mode.";
            return;
        }

        _publishedCount = nextCount;
        _lastStatus = "Published " + _publishedCount + " valid debug overlay sample(s).";

        if (_loggedFirstPublish)
            return;

        _loggedFirstPublish = true;
        Debug.Log("[FoxgloveDebugOverlaySmoke] First valid overlay publish on " + _topic);
    }

    /// <summary>
    /// Builds JSON-friendly values for the overlay envelope.
    /// </summary>
    private Dictionary<string, object> BuildValues(int frame)
    {
        var position = transform.position;
        return new Dictionary<string, object>
        {
            ["frame"] = frame,
            ["timeSec"] = Time.unscaledTime,
            ["position"] = new Dictionary<string, object>
            {
                ["x"] = position.x,
                ["y"] = position.y,
                ["z"] = position.z
            }
        };
    }
}
