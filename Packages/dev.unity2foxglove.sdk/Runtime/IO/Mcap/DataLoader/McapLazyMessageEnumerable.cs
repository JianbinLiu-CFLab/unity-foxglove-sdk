// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Single-pass lazy DataLoader message enumerable.

using System.Collections;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    internal sealed class McapLazyMessageEnumerable : IEnumerable<McapDataLoaderMessage>
    {
        private readonly McapSinglePassEnumerable<McapDataLoaderMessage> _inner;

        public McapLazyMessageEnumerable(McapDataLoader loader, McapReadOptions options)
        {
            _inner = new McapSinglePassEnumerable<McapDataLoaderMessage>(
                nameof(McapDataLoader) + "." + nameof(McapDataLoader.CreateLazyIterator),
                () => loader.EnumerateLazyMessages(options).GetEnumerator());
        }

        public IEnumerator<McapDataLoaderMessage> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
