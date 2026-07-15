// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase179
// Purpose: Unity-side owned-copy and latest-wins acceptance probe for FoxRun ROS2 input.

using System;
using System.Globalization;
using System.Text;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

/// <summary>
/// Proves that generated FoxRun ROS2 input applies an owned message copy on the
/// main thread. This component never creates a ROS2 node or subscription and
/// never retains the framework-owned callback object.
/// </summary>
/// <remarks>
/// Publish values such as <c>run-001|seq=0|total=64</c> through a matching local
/// ROS2 runtime on <c>/foxrun/phase179/string</c>. The packaged lifetime probe
/// proves that the original callback object is invalid after callback return;
/// this component independently proves that the generated owned copy remains
/// readable on the following Unity frame.
/// </remarks>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase179 ROS2 Ownership Probe")]
public sealed partial class Phase179Ros2OwnershipProbe : MonoBehaviour
{
    public const string Topic = "/foxrun/phase179/string";
    private const int MaximumMarkersPerEnable = 32;
    private const int MaximumInspectorValueLength = 256;
    private const int MaximumMarkerTokenLength = 96;
    private const int DisableObservationFrameCount = 8;

    [Header("Manager Under Test")]
    [Tooltip("Assign the FoxgloveManager, especially when testing its disabled state. If empty, the active Manager is found on enable.")]
    [SerializeField] private FoxgloveManager manager;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    // The generated host owns this current message. It is intentionally not
    // serialized or shown in Inspector; only the bounded managed copies below
    // are acceptance evidence.
    [FoxRun(
        Topic,
        Mode = FoxRunMode.SubscribeOnly,
        SubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native,
        Ros2Qos = FoxRunRos2QosPreset.Reliable)]
    private std_msgs.msg.String inputString;
#else
    [Header("Native Input Availability")]
    [SerializeField] private string nativeAvailability =
        "ROS2 native subscription support is unavailable. Import exactly one R2FU runtime and enable UNITY2FOXGLOVE_ROS2_FOR_UNITY.";
#endif

    [Header("Copied Inspector Evidence")]
    [Tooltip("Bounded managed copy of the most recently applied Data value.")]
    [SerializeField] private string lastAppliedValue;
    [Tooltip("Bounded session token parsed from <session>|seq=<n>|total=<n>.")]
    [SerializeField] private string lastAppliedToken;
    [SerializeField] private int lastAppliedSequence = -1;
    [SerializeField] private int expectedFinalSequence = -1;
    [SerializeField] private string lastStatus = "Waiting for native ROS2 String input.";
    [TextArea(2, 4)]
    [SerializeField] private string borrowedLifetimeEvidence =
        "Callback invalidity is verified by the packaged R2FU lifetime probe. This component observes only the generated owned copy and stores bounded managed strings/integers.";
    public string BorrowedLifetimeEvidence => borrowedLifetimeEvidence;

    [Header("Bounded Acceptance Counters")]
    [SerializeField] private int appliedValueCount;
    [SerializeField] private int nextFrameReadableCount;
    [SerializeField] private long bindingReceivedCount;
    [SerializeField] private long bindingReplacedCount;
    [SerializeField] private long bindingAppliedCount;
    [SerializeField] private int bindingPendingCount;
    [SerializeField] private int burstLatestPassCount;
    [SerializeField] private int disableNoApplyPassCount;
    [SerializeField] private int ownershipFailureCount;
    [SerializeField] private int emittedMarkerCount;

    [Header("Explicit Burst Attempt")]
    [Tooltip("Set the session token before publishing, then use the Arm Burst Attempt context menu. Publish <token>|seq=0..N|total=N+1 only after the armed marker.")]
    [SerializeField] private string armedBurstToken = "burst-001";
    [SerializeField] private bool burstArmed;
    [SerializeField] private long burstAttemptEpoch;
    [SerializeField] private long burstAttemptReceived;
    [SerializeField] private long burstAttemptReplaced;
    [SerializeField] private long burstAttemptApplied;
    [SerializeField] private int burstAttemptPending;
    [SerializeField] private int burstAttemptCallbacksInFlight;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private int _lastObservedLength = -1;
    private ulong _lastObservedFingerprint;
    private int _pendingNextFrameLength = -1;
    private ulong _pendingNextFrameFingerprint;
    private int _pendingNextFrameStartedAt = -1;
    private bool _managerWasEnabled;
    private bool _disableWindowActive;
    private bool _disableWindowFailed;
    private int _disableWindowFrames;
    private int _appliedCountAtDisable;
    private bool _disablePendingArmed;
    private bool _disableArmMarkerEmitted;
    private long _armedReceivedCount;
    private long _armedReplacedCount;
    private long _armedAppliedCount;
    private int _armedPendingCount;
    private bool _evidenceComplete;
    private bool _burstCompletionPending;
    private int _burstCompletionSequence = -1;
    private Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionAcceptanceSnapshot
        _latestBindingSnapshot;
    private Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2AcceptanceAttemptSnapshot
        _latestAttemptSnapshot;
#else
    private bool _warnedUnavailable;
#endif

    private void OnEnable()
    {
        emittedMarkerCount = 0;
        if (manager == null)
            manager = FindFirstObjectByType<FoxgloveManager>();

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        EndActiveBurstAttempt();
        _managerWasEnabled = manager != null && manager.isActiveAndEnabled;
        _disableWindowActive = false;
        _disableWindowFailed = false;
        _disablePendingArmed = false;
        _disableArmMarkerEmitted = false;
        burstArmed = false;
        burstAttemptEpoch = 0;
        _evidenceComplete = false;
        _burstCompletionPending = false;
        _burstCompletionSequence = -1;
        _pendingNextFrameStartedAt = -1;
        lastStatus = manager == null
            ? "Assign a FoxgloveManager before running the disable-clean check."
            : "Waiting for native ROS2 String input.";
#else
        WarnUnavailableOnce();
#endif
    }

    private void OnDisable()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        EndActiveBurstAttempt();
#endif
    }

    private void Update()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        ObserveManagerDisableWindow();
        ObserveGeneratedOwnedCopy();
#else
        WarnUnavailableOnce();
#endif
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private void ObserveGeneratedOwnedCopy()
    {
        var currentValue = inputString != null ? inputString.Data : null;
        var currentLength = currentValue?.Length ?? -1;
        var currentFingerprint = currentValue == null ? 0UL : Fingerprint(currentValue);
        if (_pendingNextFrameStartedAt >= 0 && Time.frameCount > _pendingNextFrameStartedAt)
        {
            if (currentLength == _pendingNextFrameLength
                && currentFingerprint == _pendingNextFrameFingerprint)
            {
                nextFrameReadableCount++;
                if (CanEmitMarker)
                    EmitMarker(
                        "PHASE179_ROS2_OWNERSHIP_NEXT_FRAME_READABLE",
                        lastAppliedToken,
                        "readable=" + nextFrameReadableCount.ToString(CultureInfo.InvariantCulture));
                UpdateEvidenceComplete();
            }

            _pendingNextFrameLength = -1;
            _pendingNextFrameFingerprint = 0;
            _pendingNextFrameStartedAt = -1;
        }

        if (_burstCompletionPending)
            TryCompleteArmedBurstAttempt();

        if (currentValue == null)
            return;
        if (currentLength == _lastObservedLength
            && currentFingerprint == _lastObservedFingerprint)
            return;

        _lastObservedLength = currentLength;
        _lastObservedFingerprint = currentFingerprint;
        lastAppliedValue = CopyBounded(currentValue, MaximumInspectorValueLength);
        appliedValueCount++;

        if (TryParseBurstValue(
                currentValue,
                out var sessionLength,
                out var sequence,
                out var total))
        {
            lastAppliedToken = CopyBoundedPrefix(
                currentValue,
                sessionLength,
                MaximumMarkerTokenLength);
            lastAppliedSequence = sequence;
            expectedFinalSequence = total - 1;
        }
        else
        {
            lastAppliedToken = CopyBounded(currentValue, MaximumMarkerTokenLength);
            lastAppliedSequence = -1;
            expectedFinalSequence = -1;
        }

        lastStatus = "Applied generated owned copy on frame "
                     + Time.frameCount.ToString(CultureInfo.InvariantCulture) + ".";
        if (CanEmitMarker)
            EmitMarker(
                "PHASE179_ROS2_OWNERSHIP_APPLIED",
                lastAppliedToken,
                "applied=" + appliedValueCount.ToString(CultureInfo.InvariantCulture)
                + BindingCounterText());
        _pendingNextFrameLength = currentLength;
        _pendingNextFrameFingerprint = currentFingerprint;
        _pendingNextFrameStartedAt = Time.frameCount;
        TryEmitBurstLatestMarker(currentValue, sessionLength, sequence, total);
    }

    private void TryEmitBurstLatestMarker(
        string currentValue,
        int sessionLength,
        int sequence,
        int total)
    {
        if (sessionLength <= 0 || sequence != total - 1)
            return;

        if (!burstArmed
            || burstAttemptEpoch <= 0
            || !SessionMatchesArmedToken(currentValue, sessionLength))
        {
            lastStatus = "Final burst sequence observed without a matching armed attempt; no burst PASS was emitted.";
            return;
        }
        _burstCompletionPending = true;
        _burstCompletionSequence = sequence;
        TryCompleteArmedBurstAttempt();
    }

    private void TryCompleteArmedBurstAttempt()
    {
        if (!_burstCompletionPending || !burstArmed || burstAttemptEpoch <= 0)
            return;
        if (!Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionAcceptanceDiagnostics.TryCompleteAcceptanceAttempt(
                this,
                Topic,
                burstAttemptEpoch,
                out _latestAttemptSnapshot))
        {
            lastStatus = "Burst completion closed callback admission and is waiting for in-flight callbacks.";
            return;
        }

        _burstCompletionPending = false;
        var completedEpoch = burstAttemptEpoch;
        try
        {
            CopyAttemptSnapshot(_latestAttemptSnapshot);
            if (_latestAttemptSnapshot.Epoch != completedEpoch
                || !_latestAttemptSnapshot.IsSingleApplyLatestWinsComplete)
            {
                burstArmed = false;
                ownershipFailureCount++;
                lastStatus = "Final burst sequence failed the atomically completed single-apply latest-wins accounting gate.";
                if (CanEmitMarker)
                    EmitMarker(
                        "PHASE179_ROS2_OWNERSHIP_FAIL",
                        armedBurstToken,
                        "reason=burst-accounting" + AttemptCounterText());
                return;
            }

            burstLatestPassCount++;
            burstArmed = false;
            if (CanEmitMarker)
                EmitMarker(
                    "PHASE179_ROS2_OWNERSHIP_BURST_LATEST",
                    armedBurstToken,
                    "sequence=" + _burstCompletionSequence.ToString(CultureInfo.InvariantCulture)
                    + AttemptCounterText());
            UpdateEvidenceComplete();
        }
        finally
        {
            Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionAcceptanceDiagnostics.EndAttempt(
                this,
                Topic,
                completedEpoch);
        }
    }

    private bool RefreshBindingCounters()
    {
        if (!Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionAcceptanceDiagnostics.TryGet(
                this,
                Topic,
                out var snapshot))
        {
            bindingPendingCount = 0;
            return false;
        }

        _latestBindingSnapshot = snapshot;
        bindingReceivedCount = snapshot.Received;
        bindingReplacedCount = snapshot.Replaced;
        bindingAppliedCount = snapshot.Applied;
        bindingPendingCount = snapshot.Pending;
        return true;
    }

    private string BindingCounterText()
        => " received=" + bindingReceivedCount.ToString(CultureInfo.InvariantCulture)
           + " replaced=" + bindingReplacedCount.ToString(CultureInfo.InvariantCulture)
           + " bindingApplied=" + bindingAppliedCount.ToString(CultureInfo.InvariantCulture)
           + " pending=" + bindingPendingCount.ToString(CultureInfo.InvariantCulture);

    private void CopyAttemptSnapshot(
        Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2AcceptanceAttemptSnapshot snapshot)
    {
        burstAttemptEpoch = snapshot.Epoch;
        burstAttemptReceived = snapshot.Received;
        burstAttemptReplaced = snapshot.Replaced;
        burstAttemptApplied = snapshot.Applied;
        burstAttemptPending = snapshot.Pending;
        burstAttemptCallbacksInFlight = snapshot.CallbacksInFlight;
    }

    private string AttemptCounterText()
        => " attemptReceived=" + burstAttemptReceived.ToString(CultureInfo.InvariantCulture)
           + " attemptReplaced=" + burstAttemptReplaced.ToString(CultureInfo.InvariantCulture)
           + " attemptApplied=" + burstAttemptApplied.ToString(CultureInfo.InvariantCulture)
           + " attemptPending=" + burstAttemptPending.ToString(CultureInfo.InvariantCulture)
           + " attemptCallbacks="
           + burstAttemptCallbacksInFlight.ToString(CultureInfo.InvariantCulture);

    private void ObserveManagerDisableWindow()
    {
        if (manager == null)
            manager = FindFirstObjectByType<FoxgloveManager>();
        var managerEnabled = manager != null && manager.isActiveAndEnabled;

        if (managerEnabled)
        {
            var hasLiveCounters = RefreshBindingCounters();
            var hasPendingEvidence = hasLiveCounters
                                     && bindingPendingCount > 0
                                     && bindingReceivedCount
                                     > bindingReplacedCount + bindingAppliedCount;
            _disablePendingArmed = hasPendingEvidence;
            if (hasPendingEvidence)
            {
                _armedReceivedCount = bindingReceivedCount;
                _armedReplacedCount = bindingReplacedCount;
                _armedAppliedCount = bindingAppliedCount;
                _armedPendingCount = bindingPendingCount;
                lastStatus = "Pending owned copy observed. Disable the Manager before it drains.";
                if (!_disableArmMarkerEmitted && CanEmitMarker)
                {
                    _disableArmMarkerEmitted = true;
                    EmitMarker(
                        "PHASE179_ROS2_OWNERSHIP_DISABLE_ARMED",
                        lastAppliedToken,
                        BindingCounterText());
                }
            }
        }

        if (_managerWasEnabled && !managerEnabled)
        {
            _disableWindowFailed = false;
            if (!_disablePendingArmed)
            {
                FailDisableWindow("manager-disabled-without-live-pending-evidence");
            }
            else
            {
                _disableWindowActive = true;
                _disableWindowFrames = 0;
                _appliedCountAtDisable = appliedValueCount;
                bindingPendingCount = 0;
                lastStatus = "Manager disabled after pending evidence; observing cleanup for "
                             + DisableObservationFrameCount.ToString(CultureInfo.InvariantCulture)
                             + " frames.";
            }
            _disablePendingArmed = false;
        }

        if (_disableWindowActive)
        {
            if (managerEnabled)
            {
                FailDisableWindow("manager-reenabled-before-clean-window");
            }
            else if (inputString != null)
            {
                FailDisableWindow("owned-message-remained-after-manager-disable");
            }
            else if (appliedValueCount != _appliedCountAtDisable)
            {
                FailDisableWindow("apply-count-changed-after-manager-disable");
            }
            else
            {
                _disableWindowFrames++;
                if (_disableWindowFrames >= DisableObservationFrameCount)
                {
                    _disableWindowActive = false;
                    disableNoApplyPassCount++;
                    lastStatus = "Manager disable-clean window passed with no owned message or late apply.";
                    if (CanEmitMarker)
                        EmitMarker(
                            "PHASE179_ROS2_OWNERSHIP_DISABLE_CLEAN",
                            lastAppliedToken,
                            "passes=" + disableNoApplyPassCount.ToString(CultureInfo.InvariantCulture)
                            + " armedReceived=" + _armedReceivedCount.ToString(CultureInfo.InvariantCulture)
                            + " armedReplaced=" + _armedReplacedCount.ToString(CultureInfo.InvariantCulture)
                            + " armedApplied=" + _armedAppliedCount.ToString(CultureInfo.InvariantCulture)
                            + " armedPending=" + _armedPendingCount.ToString(CultureInfo.InvariantCulture));
                    UpdateEvidenceComplete();
                }
            }
        }

        _managerWasEnabled = managerEnabled;
    }

    private void FailDisableWindow(string reason)
    {
        if (_disableWindowFailed)
            return;
        _disableWindowFailed = true;
        _disableWindowActive = false;
        ownershipFailureCount++;
        lastStatus = "Disable-clean check failed: " + reason + ".";
        if (CanEmitMarker)
            EmitMarker(
                "PHASE179_ROS2_OWNERSHIP_FAIL",
                lastAppliedToken,
                "reason=" + reason);
    }

    private static bool TryParseBurstValue(
        string value,
        out int sessionLength,
        out int sequence,
        out int total)
    {
        sessionLength = -1;
        sequence = -1;
        total = -1;
        if (string.IsNullOrEmpty(value))
            return false;

        const string sequenceMarker = "|seq=";
        const string totalMarker = "|total=";
        var sequenceStart = value.LastIndexOf(sequenceMarker, StringComparison.Ordinal);
        var totalStart = value.LastIndexOf(totalMarker, StringComparison.Ordinal);
        if (sequenceStart <= 0 || totalStart <= sequenceStart + sequenceMarker.Length)
            return false;

        var sequenceTextStart = sequenceStart + sequenceMarker.Length;
        if (!TryParseNonNegativeInt(value, sequenceTextStart, totalStart, out sequence)
            || !TryParseNonNegativeInt(
                value,
                totalStart + totalMarker.Length,
                value.Length,
                out total)
            || sequence < 0
            || total <= 0
            || sequence >= total)
        {
            sequence = -1;
            total = -1;
            return false;
        }

        sessionLength = sequenceStart;
        return true;
    }

    private static bool TryParseNonNegativeInt(
        string value,
        int start,
        int end,
        out int result)
    {
        result = 0;
        if (start < 0 || end <= start || end > value.Length)
            return false;
        for (var i = start; i < end; i++)
        {
            var digit = value[i] - '0';
            if (digit < 0 || digit > 9 || result > (int.MaxValue - digit) / 10)
                return false;
            result = result * 10 + digit;
        }
        return true;
    }

    private bool SessionMatchesArmedToken(string value, int sessionLength)
    {
        if (string.IsNullOrEmpty(armedBurstToken)
            || sessionLength != armedBurstToken.Length)
            return false;
        for (var i = 0; i < sessionLength; i++)
        {
            if (value[i] != armedBurstToken[i])
                return false;
        }
        return true;
    }

    private static ulong Fingerprint(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        for (var i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= prime;
        }
        return hash;
    }

    private bool CanEmitMarker
        => !_evidenceComplete && emittedMarkerCount < MaximumMarkersPerEnable;

    private void UpdateEvidenceComplete()
        => _evidenceComplete = burstLatestPassCount > 0
                               && nextFrameReadableCount > 0
                               && disableNoApplyPassCount > 0;

    private void EndActiveBurstAttempt()
    {
        if (burstAttemptEpoch > 0)
            Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionAcceptanceDiagnostics.EndAttempt(
                this,
                Topic,
                burstAttemptEpoch);
        burstArmed = false;
        _burstCompletionPending = false;
        _burstCompletionSequence = -1;
        burstAttemptEpoch = 0;
    }
#else
    private void WarnUnavailableOnce()
    {
        if (_warnedUnavailable)
            return;
        _warnedUnavailable = true;
        lastStatus = nativeAvailability;
        Debug.LogWarning("[Phase179] " + nativeAvailability, this);
    }
#endif

    private void EmitMarker(string marker, string token, string counters)
    {
        if (emittedMarkerCount >= MaximumMarkersPerEnable)
            return;
        emittedMarkerCount++;
        Debug.Log(
            marker
            + " topic=" + Topic
            + " token=" + SanitizeMarkerToken(token)
            + " " + counters,
            this);
    }

    private static string CopyBounded(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
    }

    private static string CopyBoundedPrefix(string value, int prefixLength, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || prefixLength <= 0)
            return string.Empty;
        var length = Math.Min(Math.Min(prefixLength, value.Length), maximumLength);
        return value.Substring(0, length);
    }

    private static string SanitizeMarkerToken(string value)
    {
        value = CopyBounded(value, MaximumMarkerTokenLength);
        if (value.Length == 0)
            return "none";
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            result.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == ':'
                ? c
                : '_');
        }
        return result.ToString();
    }

    [ContextMenu("Arm Phase179 Burst Attempt")]
    public void ArmBurstAttempt()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        armedBurstToken = CopyBounded(armedBurstToken?.Trim(), MaximumMarkerTokenLength);
        if (string.IsNullOrEmpty(armedBurstToken)
            || armedBurstToken.Contains("|seq=", StringComparison.Ordinal))
        {
            burstArmed = false;
            ownershipFailureCount++;
            lastStatus = "Burst attempt was not armed: set a non-empty plain token.";
            if (CanEmitMarker)
                EmitMarker(
                    "PHASE179_ROS2_OWNERSHIP_FAIL",
                    armedBurstToken,
                    "reason=burst-token-invalid");
            return;
        }

        var status = Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionAcceptanceDiagnostics.ArmAttempt(
            this,
            Topic,
            out _latestAttemptSnapshot);
        burstArmed = status
                     == Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2AcceptanceArmStatus.Armed;
        CopyAttemptSnapshot(_latestAttemptSnapshot);
        _burstCompletionPending = false;
        _burstCompletionSequence = -1;
        if (!burstArmed)
        {
            ownershipFailureCount++;
            lastStatus = "Burst attempt arm rejected: " + status + ".";
            if (CanEmitMarker)
                EmitMarker(
                    "PHASE179_ROS2_OWNERSHIP_FAIL",
                    armedBurstToken,
                    "reason=burst-arm-" + status);
            return;
        }

        lastStatus = "Burst attempt armed at an idle binding. Publish the matching sequence now.";
        if (CanEmitMarker)
            EmitMarker(
                "PHASE179_ROS2_OWNERSHIP_BURST_ARMED",
                armedBurstToken,
                "epoch=" + burstAttemptEpoch.ToString(CultureInfo.InvariantCulture));
#else
        WarnUnavailableOnce();
#endif
    }

    [ContextMenu("Reset Phase179 Ownership Evidence")]
    private void ResetEvidence()
    {
        lastAppliedValue = string.Empty;
        lastAppliedToken = string.Empty;
        lastAppliedSequence = -1;
        expectedFinalSequence = -1;
        lastStatus = "Waiting for native ROS2 String input.";
        appliedValueCount = 0;
        nextFrameReadableCount = 0;
        bindingReceivedCount = 0;
        bindingReplacedCount = 0;
        bindingAppliedCount = 0;
        bindingPendingCount = 0;
        burstLatestPassCount = 0;
        disableNoApplyPassCount = 0;
        ownershipFailureCount = 0;
        emittedMarkerCount = 0;
        burstArmed = false;
        burstAttemptReceived = 0;
        burstAttemptReplaced = 0;
        burstAttemptApplied = 0;
        burstAttemptPending = 0;
        burstAttemptCallbacksInFlight = 0;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        EndActiveBurstAttempt();
        _lastObservedLength = -1;
        _lastObservedFingerprint = 0;
        _pendingNextFrameLength = -1;
        _pendingNextFrameFingerprint = 0;
        _pendingNextFrameStartedAt = -1;
        _disableWindowActive = false;
        _disableWindowFailed = false;
        _disablePendingArmed = false;
        _disableArmMarkerEmitted = false;
        _armedReceivedCount = 0;
        _armedReplacedCount = 0;
        _armedAppliedCount = 0;
        _armedPendingCount = 0;
        _evidenceComplete = false;
        _latestBindingSnapshot = default;
        _latestAttemptSnapshot = default;
#endif
    }
}
