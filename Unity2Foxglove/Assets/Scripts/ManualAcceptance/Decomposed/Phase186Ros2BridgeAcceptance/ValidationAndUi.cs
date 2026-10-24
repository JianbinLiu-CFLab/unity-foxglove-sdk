// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase186
// Purpose: Controlled main-thread evidence surface for the ROS-free Bridge.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Unity2Foxglove.Ros2Bridge;

namespace Unity2Foxglove.ManualAcceptance
{
    public sealed partial class Phase186Ros2BridgeAcceptance
    {
        private bool TryValidateContext(out string reason)
        {
            if (_manager == null || _provider == null)
            {
                reason = "scene Manager or Bridge Provider is absent";
                return false;
            }
            if (!_generatedIdentity.Present)
            {
                reason = "token-specific generated partial is absent";
                return false;
            }
            if (!string.Equals(_runId, _generatedIdentity.RunId, StringComparison.Ordinal)
                || !string.Equals(_caseId, _generatedIdentity.CaseId, StringComparison.Ordinal)
                || !string.Equals(_tokenHash, _generatedIdentity.TokenHash, StringComparison.Ordinal)
                || !string.Equals(_featureHead, _generatedIdentity.Head, StringComparison.Ordinal)
                || !string.Equals(
                    InterfaceDigest,
                    _generatedIdentity.InterfaceDigest,
                    StringComparison.Ordinal))
            {
                reason = "serialized run identity differs from generated binding";
                return false;
            }
            if (!SameTopics(_topics, _generatedIdentity.Topics))
            {
                reason = "serialized topics differ from generated binding";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private string FormatIdentityMarker(string prefix, string verdict)
            => prefix
               + " run=" + _runId
               + " case=" + _caseId
               + " tokenHash=" + _tokenHash
               + " head=" + _featureHead
               + " verdict=" + verdict;

        private void OnGUI()
        {
            const float left = 12f;
            const float top = 44f;
            const float width = 820f;
            GUI.Box(
                new Rect(left, top, width, _manual ? 280f : 250f),
                "Phase186 ROS-free Bridge acceptance");
            GUI.Label(new Rect(left + 12f, top + 26f, width - 24f, 22f),
                "Run: " + Phase186Bound(_runId) + " / " + Phase186Bound(_caseId));
            GUI.Label(new Rect(left + 12f, top + 48f, width - 24f, 22f),
                "Status: " + Phase186Bound(_status));
            GUI.Label(new Rect(left + 12f, top + 70f, width - 24f, 22f),
                "Provider: connected=" + _connected
                + " publish=" + _publishState
                + " subscribe=" + _subscribeState
                + " generation=" + _sessionGeneration);
            GUI.Label(new Rect(left + 12f, top + 92f, width - 24f, 22f),
                "Inbound estimate: received=" + _generatedEvidence.Received
                + " applied=" + _generatedEvidence.Applied
                + " replaced=" + _generatedEvidence.Replaced);
            GUI.Label(new Rect(left + 12f, top + 114f, width - 24f, 22f),
                "Outbound: accepted=" + _acceptedFrames
                + " sent=" + _sentFrames
                + " failed=" + _failedFrames
                + " queued=" + _queuedFrames);
            GUI.Label(new Rect(left + 12f, top + 136f, width - 24f, 22f),
                "Latest standard: "
                + Phase186Bound(_generatedEvidence.LastStandardMessage));
            GUI.Label(new Rect(left + 12f, top + 158f, width - 24f, 22f),
                "Latest Phase181: "
                + Phase186Bound(_generatedEvidence.LastCustomMessage));

            var interaction = EvaluateManualInteraction();
            if (_manual)
            {
                GUI.Label(new Rect(left + 12f, top + 180f, width - 24f, 22f),
                    "Manual step: " + ManualStepText(interaction.Step));
            }
            var buttonTop = _manual ? 212f : 190f;

            GUI.enabled = _manual
                          ? interaction.CanRequestLocalB
                          : _contextValid && !_terminal;
            if (GUI.Button(
                    new Rect(left + 12f, top + buttonTop, 280f, 32f),
                    "Publish distinct local mutation B"))
            {
                PublishLocalMutation();
            }
            GUI.enabled = interaction.CanRequestComplete;
            if (GUI.Button(
                    new Rect(left + 310f, top + buttonTop, 280f, 32f),
                    "Complete current manual run"))
            {
                CompleteManualAcceptance();
            }
            GUI.enabled = true;
        }

        private static string ManualStepText(Phase186ManualStep step)
        {
            switch (step)
            {
                case Phase186ManualStep.ReadyToPublishLocalB:
                    return "Ready to publish local B.";
                case Phase186ManualStep.WaitingForAutomatedBAndPeerEvidence:
                    return "Waiting for automated B and peer evidence.";
                case Phase186ManualStep.ReadyToComplete:
                    return "Ready to complete.";
                case Phase186ManualStep.CompletedKeepPlayRunning:
                    return "Complete accepted; exiting Play Mode.";
                default:
                    return "Waiting for Provider Publish/Subscribe readiness and external A.";
            }
        }

        internal static void Phase186RecordSequence(
            long sequence,
            ref long previous,
            ref GeneratedEvidence evidence)
        {
            if (sequence < 0 || sequence == previous)
                return;
            if (previous >= 0 && sequence > previous)
            {
                var delta = sequence - previous;
                evidence.Received = SaturatingAdd(evidence.Received, delta);
                evidence.Replaced = SaturatingAdd(
                    evidence.Replaced,
                    Math.Max(0L, delta - 1L));
            }
            else
            {
                evidence.Received = SaturatingIncrement(evidence.Received);
            }
            evidence.Applied = SaturatingIncrement(evidence.Applied);
            previous = sequence;
        }

        internal static bool Phase186TryReadSequence(
            string message,
            out long sequence)
        {
            sequence = 0;
            if (string.IsNullOrEmpty(message))
                return false;
            var first = message.IndexOf(':');
            var second = first < 0 ? -1 : message.IndexOf(':', first + 1);
            var third = second < 0 ? -1 : message.IndexOf(':', second + 1);
            return first > 0
                   && second > first + 1
                   && third > second + 1
                   && long.TryParse(
                       message.Substring(second + 1, third - second - 1),
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out sequence);
        }

        internal static string Phase186Bound(string value)
        {
            value ??= string.Empty;
            return value.Length <= MaximumTextLength
                ? value
                : value.Substring(0, MaximumTextLength);
        }

        private static void ValidateTopics(string[] topics)
        {
            if (topics == null || topics.Length == 0 || topics.Length > 3)
                throw new ArgumentException("Topic set is absent or unbounded.", nameof(topics));
            for (var i = 0; i < topics.Length; i++)
            {
                var topic = topics[i];
                if (string.IsNullOrWhiteSpace(topic)
                    || !topic.StartsWith("/foxrun/phase186/p186h_", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Topic is outside Phase186 run authority.", nameof(topics));
                }
                for (var previous = 0; previous < i; previous++)
                {
                    if (string.Equals(topic, topics[previous], StringComparison.Ordinal))
                        throw new ArgumentException("Topic set contains a duplicate.", nameof(topics));
                }
            }
        }

        private static bool SameTopics(string[] left, string[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static bool IsRunId(string value)
            => !string.IsNullOrEmpty(value)
               && value.Length >= 12
               && value.Length <= 80
               && value.StartsWith("phase186h-", StringComparison.Ordinal);

        private static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length)
                return false;
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private static long SaturatingIncrement(long value)
            => value == long.MaxValue ? long.MaxValue : value + 1L;

        private static long SaturatingAdd(long value, long addition)
        {
            if (addition <= 0)
                return value;
            return value > long.MaxValue - addition
                ? long.MaxValue
                : value + addition;
        }
    }
}
