// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Publishing/MessagePack
// Purpose: Canonical cache identity for raw and typed MessagePack channels.

namespace Unity.FoxgloveSDK.Components.Publishing.MessagePack
{
    internal static class FoxgloveMsgPackChannelIdentity
    {
        internal static (string topic, string schemaName, string encoding, string shapeIdentity) GetCacheKey(
            string topic,
            string encoding,
            string logicalSchema,
            string shapeIdentity)
        {
            var hasComponentIdentity = !string.IsNullOrEmpty(logicalSchema);
            return (
                topic,
                hasComponentIdentity ? logicalSchema : string.Empty,
                encoding,
                hasComponentIdentity ? shapeIdentity ?? string.Empty : string.Empty);
        }
    }
}
