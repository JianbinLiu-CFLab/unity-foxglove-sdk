// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Single-pass enumerable helper for lazy MCAP readers.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Unity.FoxgloveSDK.IO
{
    internal sealed class McapSinglePassEnumerable<T> : IEnumerable<T>
    {
        private readonly Func<IEnumerator<T>> _enumeratorFactory;
        private readonly string _name;
        private int _started;

        public McapSinglePassEnumerable(string name, Func<IEnumerator<T>> enumeratorFactory)
        {
            _name = string.IsNullOrEmpty(name) ? "MCAP lazy enumerable" : name;
            _enumeratorFactory = enumeratorFactory ?? throw new ArgumentNullException(nameof(enumeratorFactory));
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
                throw new InvalidOperationException(_name + " is forward-only and can be enumerated only once.");

            return _enumeratorFactory();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
