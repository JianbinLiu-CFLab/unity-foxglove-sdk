// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase189
// Purpose: Deterministic scalar and nested generated Component MessagePack fixtures.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Protocol;

namespace Unity2Foxglove.ManualAcceptance
{
    [FoxgloveSchema("phase189.component.Scalar")]
    public sealed class Phase189ScalarMessage
    {
        public int Value;
        public string Label;
    }

    public sealed class Phase189NestedValue
    {
        public bool Enabled;
        public string Label;
    }

    [FoxgloveSchema("phase189.component.Nested")]
    public sealed class Phase189NestedMessage
    {
        public int Sequence;
        public Phase189NestedValue Nested;
    }

    public sealed class Phase189ScalarPublisher : FoxglovePublisher<Phase189ScalarMessage>
    {
        public string AcceptanceTopic => Topic;
        public PublisherEncodingOverride AcceptanceEncoding => EncodingOverride;
        public void SetAcceptanceContract(string topic, PublisherEncodingOverride encoding) { _topic = topic ?? string.Empty; _encodingOverride = encoding; }
        protected override Phase189ScalarMessage CreateMessage()
            => new Phase189ScalarMessage { Value = 189, Label = "phase189-scalar" };
    }

    public sealed class Phase189NestedPublisher : FoxglovePublisher<Phase189NestedMessage>
    {
        public string AcceptanceTopic => Topic;
        public PublisherEncodingOverride AcceptanceEncoding => EncodingOverride;
        public void SetAcceptanceContract(string topic, PublisherEncodingOverride encoding) { _topic = topic ?? string.Empty; _encodingOverride = encoding; }
        protected override Phase189NestedMessage CreateMessage()
            => new Phase189NestedMessage
            {
                Sequence = 189,
                Nested = new Phase189NestedValue { Enabled = true, Label = "phase189-nested" }
            };
    }
}
