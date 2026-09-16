// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.ManualAcceptance
{
    /// <summary>Typed nested publisher used by the Phase189 maintained scene.</summary>
    public sealed class Phase189NestedPublisher : FoxglovePublisher<Phase189NestedMessage>
    {
        public string AcceptanceTopic => Topic;
        public PublisherEncodingOverride AcceptanceEncoding => EncodingOverride;

        public void SetAcceptanceContract(string topic, PublisherEncodingOverride encoding)
        {
            _topic = topic ?? string.Empty;
            _encodingOverride = encoding;
        }

        protected override Phase189NestedMessage CreateMessage()
            => new Phase189NestedMessage
            {
                Sequence = 189,
                Nested = new Phase189NestedValue { Enabled = true, Label = "phase189-nested" }
            };
    }
}
