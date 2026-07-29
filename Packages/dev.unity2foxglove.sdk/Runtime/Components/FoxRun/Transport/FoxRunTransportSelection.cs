// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Canonical immutable Manager routing selection.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxRunTransportSelection
    {
        private readonly FoxRunTransportId[] _publishTransportIds;
        private readonly IReadOnlyList<FoxRunTransportId> _publishView;

        public FoxRunTransportSelection(
            IEnumerable<string> publishTransportIds,
            bool subscriptionsEnabled,
            string subscribeTransportId)
        {
            var publishValues = publishTransportIds?.ToArray()
                                ?? Array.Empty<string>();
            var seen = new HashSet<FoxRunTransportId>();
            _publishTransportIds = new FoxRunTransportId[publishValues.Length];
            for (var i = 0; i < publishValues.Length; i++)
            {
                var id = new FoxRunTransportId(publishValues[i]);
                if (!seen.Add(id))
                    throw new ArgumentException(
                        "Publish transport IDs must be unique.",
                        nameof(publishTransportIds));
                _publishTransportIds[i] = id;
            }

            Array.Sort(
                _publishTransportIds,
                (left, right) => string.CompareOrdinal(left.Value, right.Value));
            _publishView = Array.AsReadOnly(_publishTransportIds);

            if (!string.IsNullOrWhiteSpace(subscribeTransportId))
                SubscribeTransportId = new FoxRunTransportId(subscribeTransportId);
            else if (subscriptionsEnabled)
                throw new ArgumentException(
                    "An enabled subscription selection requires exactly one transport ID.",
                    nameof(subscribeTransportId));

            SubscriptionsEnabled = subscriptionsEnabled;
        }

        public IReadOnlyList<FoxRunTransportId> PublishTransportIds => _publishView;
        public bool SubscriptionsEnabled { get; }
        public FoxRunTransportId? SubscribeTransportId { get; }

        public string DeterministicKey
        {
            get
            {
                var publish = string.Join(
                    ",",
                    _publishTransportIds.Select(id => id.Value));
                var subscribe = SubscriptionsEnabled
                    ? SubscribeTransportId.Value.Value
                    : string.Empty;
                return publish + "|" + (SubscriptionsEnabled ? "1" : "0") + "|" + subscribe;
            }
        }
    }
}
