// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Provider-neutral runtime scheduler for generated FoxRun sources.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Util;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public sealed partial class FoxgloveLogHub : MonoBehaviour
    {
        private void RemoveSourceNow(
            IFoxgloveLogSource source)
        {
            if (source == null
                || !_sources.TryGetValue(
                    source,
                    out var state))
            {
                return;
            }

            _sources.Remove(source);
            for (var index = 0;
                 index < state.Contracts.Length;
                 index++)
            {
                if (!state.Accepted[index]
                    || state.Contracts[index] == null)
                {
                    continue;
                }

                var contract =
                    state.Contracts[index];
                _topicBus.Unregister(
                    contract.Topic,
                    state.Origin);
                if (contract.Visibility
                    == FoxTopicVisibility.Exported)
                {
                    _sinkRouter.Unregister(
                        contract.Topic);
                }
            }
        }

        private int Scan()
        {
            var added = 0;
            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            foreach (var behaviour in behaviours)
            {
                if (behaviour
                        is IFoxgloveLogSource source
                    && AddSourceNow(source))
                {
                    added++;
                }
            }

            return added;
        }

        private void DrainPending()
        {
            IFoxgloveLogSource[] copy;
            lock (PendingGate)
            {
                copy = Pending.ToArray();
                Pending.Clear();
                PendingSet.Clear();
            }

            foreach (var source in copy)
                QueueAdd(source);
        }

        private static FoxTopicContract FallbackContract(
            FoxgloveLogTopicInfo info)
            => new FoxTopicContract(
                info.Topic,
                string.Empty,
                info.HasExplicitEncoding
                    ? FoxRunEncodingResolver
                        .ToProtocolEncoding(
                            info.DeclaredEncoding)
                    : "json",
                string.Empty,
                string.Empty,
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

        private static string SourceOrigin(
            IFoxgloveLogSource source)
        {
            var type = source.GetType();
            var instance = source is UnityEngine.Object value
                ? value.GetInstanceID()
                : source.GetHashCode();
            return (type.FullName ?? type.Name)
                   + ":"
                   + instance;
        }

        private void WarnOnce(
            IFoxgloveLogSource source,
            int topicIndex,
            Exception exception)
        {
            var sourceType = source?.GetType().FullName ?? "unknown";
            var exceptionType = exception?.GetType().FullName ?? typeof(Exception).FullName;
            var key = sourceType + "|" + topicIndex + "|" + exceptionType;
            var message = key + "|" + (exception?.Message ?? string.Empty);
            WarnOnce(key, message);
        }

        private void WarnOnce(string message)
            => WarnOnce(message, message);

        private void WarnOnce(string key, string message)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock (_reportedFailures)
            {
                if (!_reportedFailures.Add(key))
                    return;

                _reportedFailureOrder.Enqueue(key);
                if (_reportedFailureOrder.Count > MaximumReportedFailures)
                    _reportedFailures.Remove(_reportedFailureOrder.Dequeue());
            }

            Debug.LogWarning("[FoxRun] " + message);
        }

        private static void OnSinkFaulted(
            FoxTopicSinkFault fault)
        {
            if (fault?.Exception != null)
                Debug.LogException(fault.Exception);
        }

        private void OnDestroy()
        {
            _sinkRouter.SinkFaulted -= OnSinkFaulted;
            foreach (var source in
                     new List<IFoxgloveLogSource>(
                         _sources.Keys))
            {
                RemoveSourceNow(source);
            }

            _sinkRouter.Dispose();
            if (_instance == this)
                _instance = null;
        }

        private sealed class SourceState
        {
            internal SourceState(
                FoxgloveLogTopicInfo[] topics,
                FixedRatePublishState[] timers,
                bool[] accepted,
                FoxTopicContract[] contracts,
                string origin)
            {
                Topics = topics;
                Timers = timers;
                Accepted = accepted;
                Contracts = contracts;
                Origin = origin;
            }

            internal FoxgloveLogTopicInfo[] Topics { get; }
            internal FixedRatePublishState[] Timers { get; }
            internal bool[] Accepted { get; }
            internal FoxTopicContract[] Contracts { get; }
            internal string Origin { get; }
        }

        private readonly struct DeferredTrigger
        {
            internal DeferredTrigger(
                IFoxgloveLogSource source,
                int topicIndex)
            {
                Source = source;
                TopicIndex = topicIndex;
            }

            internal IFoxgloveLogSource Source { get; }
            internal int TopicIndex { get; }
        }
    }
}
