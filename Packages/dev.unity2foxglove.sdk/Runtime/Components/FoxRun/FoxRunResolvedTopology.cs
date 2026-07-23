// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Immutable effective FoxRun directional endpoint contract.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Effective endpoint and Foxglove-encoding selection for one declaration.
    /// A zero Source, Targets, or directional Encoding means that direction or
    /// Foxglove transport is not part of the resolved declaration.
    /// </summary>
    public readonly struct FoxRunResolvedTopology
    {
        internal FoxRunResolvedTopology(
            FoxRunFlow mode,
            FoxRunEndpoint source,
            FoxRunEndpoint targets,
            FoxRunEncoding publishEncoding,
            FoxRunEncoding subscribeEncoding)
        {
            Mode = mode;
            Source = source;
            Targets = targets;
            PublishEncoding = publishEncoding;
            SubscribeEncoding = subscribeEncoding;
        }

        public FoxRunFlow Mode { get; }
        public FoxRunEndpoint Source { get; }
        public FoxRunEndpoint Targets { get; }
        public FoxRunEncoding PublishEncoding { get; }
        public FoxRunEncoding SubscribeEncoding { get; }

        public bool Publishes => Mode == FoxRunFlow.Publish
                                 || Mode == FoxRunFlow.PublishAndSubscribe;

        public bool Subscribes => Mode == FoxRunFlow.Subscribe
                                  || Mode == FoxRunFlow.PublishAndSubscribe;
    }
}
