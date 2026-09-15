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

}
