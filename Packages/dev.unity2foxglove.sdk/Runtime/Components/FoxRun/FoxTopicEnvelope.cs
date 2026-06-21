// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Typed runtime envelope for local FoxRun topic dispatch.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Typed local envelope for a FoxRun topic publish.</summary>
    public readonly struct FoxTopicEnvelope<T>
    {
        public FoxTopicEnvelope(FoxTopicContract contract, ulong timestampNs, T payload, string origin)
        {
            Contract = contract;
            TimestampNs = timestampNs;
            Payload = payload;
            Origin = origin ?? string.Empty;
        }

        public FoxTopicContract Contract { get; }
        public ulong TimestampNs { get; }
        public T Payload { get; }
        public string Origin { get; }
    }
}
