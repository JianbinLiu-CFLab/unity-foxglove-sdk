// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Core
{
    public partial class ReplayController
    {
        private readonly struct ReplayChannelContext
        {
            public readonly string Topic;
            public readonly string MessageEncoding;
            public readonly string SchemaName;
            public readonly string SchemaEncoding;

            public ReplayChannelContext(McapChannel channel, McapSchema schema)
            {
                Topic = channel?.Topic ?? string.Empty;
                MessageEncoding = channel?.MessageEncoding ?? string.Empty;
                SchemaName = schema?.Name ?? string.Empty;
                SchemaEncoding = schema?.Encoding ?? string.Empty;
            }
        }

        private readonly struct ReplayCallbackDispatch
        {
            private ReplayCallbackDispatch(ReplayMessageContext? messageContext, ReplayBatchContext? batchContext, bool isBatch)
            {
                MessageContext = messageContext;
                BatchContext = batchContext;
                IsBatch = isBatch;
            }

            public ReplayMessageContext? MessageContext { get; }
            public ReplayBatchContext? BatchContext { get; }
            public bool IsBatch { get; }

            public static ReplayCallbackDispatch ForMessage(ReplayMessageContext context)
                => new ReplayCallbackDispatch(context, null, isBatch: false);

            public static ReplayCallbackDispatch ForBatch(ReplayBatchContext context)
                => new ReplayCallbackDispatch(null, context, isBatch: true);
        }
    }
}
