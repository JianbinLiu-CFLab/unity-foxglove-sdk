// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Provider-neutral semantic model consumed by FoxRun source emission.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    [Flags]
    public enum FoxRunNamedArgumentPresence : long
    {
        None = 0,
        Hz = 1L << 0,
        Tolerance = 1L << 1,
        OnlyIf = 1L << 2,
        SchemaName = 1L << 3,
        Policy = 1L << 4,
        Mode = 1L << 5,
        Encoding = 1L << 6,
        ProtobufFieldNumber = 1L << 9,
        Reliability = 1L << 11,
        Durability = 1L << 12,
        History = 1L << 13,
        Depth = 1L << 14,
        PublishTransportIds = 1L << 15,
        SubscribeTransportId = 1L << 16
    }

    public enum FoxRunConditionMemberKind
    {
        None = 0,
        Field = 1,
        Property = 2,
        Method = 3,
        Missing = 4,
        Invalid = 5,
        Unresolved = 6
    }

    /// <remarks>Phase192 source sections: NormalizedScheduleTuple.cs, EncodingVariantAvailability.cs, GenerationModel.cs, GenerationType.cs, GenerationMember.cs</remarks>
}
