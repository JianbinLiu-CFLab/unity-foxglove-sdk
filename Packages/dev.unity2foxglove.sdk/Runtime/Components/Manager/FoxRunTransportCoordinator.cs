// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Pure fan-out coordinator over one frozen FoxRun transport session.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    internal sealed class FoxRunTransportCoordinator
    {
        private FoxRunTransportSessionSnapshot _session;

        public void SetSession(FoxRunTransportSessionSnapshot session)
            => _session = session;

        public void ClearSession()
            => _session = null;

        public FoxRunOrdinaryTransportFanoutResult PublishOrdinary(
            in FoxRunOrdinaryPayloadRequest request)
            => FoxRunOrdinaryTransportFanout.Publish(
                _session?.PublishTransports,
                in request);

        public FoxRunGeneratedTransportFanoutResult PublishGenerated(
            IFoxRunGeneratedTransportSource source,
            int topicIndex,
            string topic,
            IReadOnlyList<string> explicitTransportIds,
            ulong logTimeNs,
            string suppressedTransportId = "",
            ulong suppressedGeneration = 0)
        {
            var request = new FoxRunGeneratedTransportPublishRequest(
                source,
                topicIndex,
                topic,
                logTimeNs);
            return FoxRunGeneratedTransportFanout.Publish(
                _session?.PublishTransports,
                explicitTransportIds,
                _session?.PublishTransportIds,
                in request,
                suppressedTransportId,
                suppressedGeneration);
        }
    }
}
