// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Idempotent caller-owned lease for a taken FoxRun stream sample.

using System;
using System.Threading;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Caller-owned lease returned by a stream take operation. The value remains
    /// valid until this lease is disposed.
    /// </summary>
    public sealed class FoxRunStreamSample<T> : IDisposable
    {
        private Ownership _ownership;

        internal FoxRunStreamSample(
            T value,
            Action<T> disposer,
            Action<Exception> reportDisposalFailure)
        {
            _ownership = new Ownership(value, disposer, reportDisposalFailure);
            Value = value;
        }

        public T Value { get; }

        public void Dispose()
        {
            var ownership = Interlocked.Exchange(ref _ownership, null);
            ownership?.Dispose();
        }

        private sealed class Ownership
        {
            private readonly T _value;
            private readonly Action<T> _disposer;
            private readonly Action<Exception> _reportDisposalFailure;

            internal Ownership(
                T value,
                Action<T> disposer,
                Action<Exception> reportDisposalFailure)
            {
                _value = value;
                _disposer = disposer;
                _reportDisposalFailure = reportDisposalFailure;
            }

            internal void Dispose()
            {
                try
                {
                    _disposer(_value);
                }
                catch (Exception exception)
                {
                    _reportDisposalFailure(exception);
                }
            }
        }
    }
}
