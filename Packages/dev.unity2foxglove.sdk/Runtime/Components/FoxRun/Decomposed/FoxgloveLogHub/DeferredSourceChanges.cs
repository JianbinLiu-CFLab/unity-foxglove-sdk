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
        private void QueueAdd(
            IFoxgloveLogSource source)
        {
            if (_iterating)
            {
                if (!_deferredAdds.Contains(source))
                    _deferredAdds.Add(source);
                _deferredRemoves.Remove(source);
                return;
            }

            AddSourceNow(source);
        }

        private void QueueRemove(
            IFoxgloveLogSource source)
        {
            if (_iterating)
            {
                if (!_deferredRemoves.Contains(source))
                    _deferredRemoves.Add(source);
                _deferredAdds.Remove(source);
                return;
            }

            RemoveSourceNow(source);
        }

        private void ApplyDeferred()
        {
            if (_iterating)
                return;

            if (_deferredRemoves.Count > 0)
            {
                var removes = _deferredRemoves.ToArray();
                _deferredRemoves.Clear();
                foreach (var source in removes)
                    RemoveSourceNow(source);
            }

            if (_deferredAdds.Count > 0)
            {
                var adds = _deferredAdds.ToArray();
                _deferredAdds.Clear();
                foreach (var source in adds)
                    AddSourceNow(source);
            }

            if (_deferredTriggers.Count > 0)
            {
                var triggers = _deferredTriggers.ToArray();
                _deferredTriggers.Clear();
                foreach (var trigger in triggers)
                {
                    TryPublish(
                        trigger.Source,
                        trigger.TopicIndex,
                        explicitTrigger: true);
                }
            }
        }

        private bool AddSourceNow(
            IFoxgloveLogSource source)
        {
            if (source == null
                || _sources.ContainsKey(source))
            {
                return false;
            }

            var count = Math.Max(
                0,
                source.FoxgloveLog_TopicCount);
            var topics =
                new FoxgloveLogTopicInfo[count];
            var accepted = new bool[count];
            var timers =
                new FixedRatePublishState[count];
            var contracts =
                new FoxTopicContract[count];
            var contractSource =
                source as IFoxgloveTopicContractSource;
            var origin =
                contractSource?.FoxgloveLog_Origin
                ?? SourceOrigin(source);
            for (var index = 0;
                 index < count;
                 index++)
            {
                try
                {
                    var info =
                        source.FoxgloveLog_GetTopic(index);
                    topics[index] = info;
                    if (string.IsNullOrWhiteSpace(
                            info.Topic))
                    {
                        continue;
                    }

                    var contract =
                        contractSource
                            ?.FoxgloveLog_GetContract(index)
                        ?? FallbackContract(info);
                    contracts[index] = contract;
                    var registration =
                        _topicBus.Register(
                            contract,
                            origin);
                    if (!registration.Accepted)
                    {
                        WarnOnce(
                            registration.Diagnostic);
                        continue;
                    }

                    if (contract.Visibility
                            == FoxTopicVisibility.Exported
                        && !_sinkRouter.Register(
                            contract,
                            ResolveAdditiveSinkEncoding(
                                info)))
                    {
                        _topicBus.Unregister(
                            contract.Topic,
                            origin);
                        WarnOnce(
                            "Topic sink contract conflict for '"
                            + contract.Topic
                            + "'.");
                        continue;
                    }

                    accepted[index] = true;
                }
                catch (Exception exception)
                    when (FoxRunExceptionPolicy
                        .IsRecoverable(exception))
                {
                    WarnOnce(
                        source,
                        index,
                        exception);
                }
            }

            _sources.Add(
                source,
                new SourceState(
                    topics,
                    timers,
                    accepted,
                    contracts,
                    origin));
            return true;
        }

    }
}
