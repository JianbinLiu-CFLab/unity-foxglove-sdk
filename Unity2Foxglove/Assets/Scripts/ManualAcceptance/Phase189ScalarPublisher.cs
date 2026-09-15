// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.ManualAcceptance
{
    /// <summary>Typed scalar publisher used by the Phase189 maintained scene.</summary>
    public sealed class Phase189ScalarPublisher : FoxglovePublisher<Phase189ScalarMessage>
    {
        public string AcceptanceTopic => Topic;
        public PublisherEncodingOverride AcceptanceEncoding => EncodingOverride;

        public void SetAcceptanceContract(string topic, PublisherEncodingOverride encoding)
        {
            _topic = topic ?? string.Empty;
            _encodingOverride = encoding;
        }

        protected override Phase189ScalarMessage CreateMessage()
            => new Phase189ScalarMessage { Value = 189, Label = "phase189-scalar" };
    }
}
