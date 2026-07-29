// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/FullDemoVisualization
// Purpose: Controlled typed MessagePack full-duplex acceptance probe.

using UnityEngine;
using Unity.FoxgloveSDK.Components;

public partial class TestLog
{
    public const string MessagePackProbeTopic =
        "/phase185/messagepack/full-duplex";
    public const string MessagePackApplyEvidenceTopic =
        "/phase185/messagepack/apply-evidence";
    public const int MessagePackRemoteSequenceA = 185001;
    public const int MessagePackRemoteValueA = 41;
    public const int MessagePackLocalSequenceB = 185002;
    public const int MessagePackLocalValueB = 82;
    public const int MessagePackRecoverySequence = 185003;
    public const int MessagePackRecoveryValue = 123;
    public const float MessagePackNoOutputWindowSeconds = 1.0f;
    public const float MessagePackLocalMutationDelaySeconds = 1.5f;

    [FoxRun(
        MessagePackProbeTopic,
        Mode = FoxRunFlow.PublishAndSubscribe,
        Encoding = FoxRunEncoding.MessagePack,
        Policy = FoxRunPolicy.Change,
        Hz = 20f)]
    private int _messagePackSequence;

    [FoxRun(
        MessagePackProbeTopic,
        Mode = FoxRunFlow.PublishAndSubscribe,
        Encoding = FoxRunEncoding.MessagePack,
        Policy = FoxRunPolicy.Change,
        Hz = 20f)]
    private int _messagePackValue;

    [FoxRun(
        MessagePackApplyEvidenceTopic,
        Encoding = FoxRunEncoding.JSON,
        Policy = FoxRunPolicy.Change,
        Hz = 20f)]
    private int _messagePackAppliedSequence;

    [FoxRun(
        MessagePackApplyEvidenceTopic,
        Encoding = FoxRunEncoding.JSON,
        Policy = FoxRunPolicy.Change,
        Hz = 20f)]
    private int _messagePackAppliedValue;

    private int _messagePackLastObservedSequence;
    private int _messagePackLastObservedValue;
    private float _messagePackLocalMutationAt;
    private bool _messagePackLocalMutationArmed;
    private bool _messagePackLocalMutationCompleted;

    partial void UpdateMessagePackProbe()
    {
        ObserveMessagePackProbeState();
        if (!_messagePackLocalMutationArmed
            || _messagePackLocalMutationCompleted
            || Time.unscaledTime < _messagePackLocalMutationAt)
        {
            return;
        }

        _messagePackLocalMutationArmed = false;
        _messagePackLocalMutationCompleted = true;
        _messagePackSequence = MessagePackLocalSequenceB;
        _messagePackValue = MessagePackLocalValueB;
        Debug.Log(
            "PHASE185_MESSAGEPACK_LOCAL_MUTATION "
            + "sequence=" + _messagePackSequence
            + " value=" + _messagePackValue,
            this);
    }

    private void ObserveMessagePackProbeState()
    {
        if (_messagePackSequence == _messagePackLastObservedSequence
            && _messagePackValue == _messagePackLastObservedValue)
        {
            return;
        }

        _messagePackLastObservedSequence = _messagePackSequence;
        _messagePackLastObservedValue = _messagePackValue;

        if (_messagePackSequence == MessagePackRemoteSequenceA
            && _messagePackValue == MessagePackRemoteValueA
            && !_messagePackLocalMutationCompleted)
        {
            _messagePackAppliedSequence = _messagePackSequence;
            _messagePackAppliedValue = _messagePackValue;
            _messagePackLocalMutationAt =
                Time.unscaledTime + MessagePackLocalMutationDelaySeconds;
            _messagePackLocalMutationArmed = true;
            Debug.Log(
                "PHASE185_MESSAGEPACK_REMOTE_APPLIED "
                + "sequence=" + _messagePackSequence
                + " value=" + _messagePackValue,
                this);
            return;
        }

        if (_messagePackSequence == MessagePackRecoverySequence
            && _messagePackValue == MessagePackRecoveryValue)
        {
            _messagePackAppliedSequence = _messagePackSequence;
            _messagePackAppliedValue = _messagePackValue;
            Debug.Log(
                "PHASE185_MESSAGEPACK_RECOVERY_APPLIED "
                + "sequence=" + _messagePackSequence
                + " value=" + _messagePackValue,
                this);
        }
    }
}
