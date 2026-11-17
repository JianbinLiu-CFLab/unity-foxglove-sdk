// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;

namespace Unity.FoxgloveSDK.Core
{
    internal sealed class SessionChannelPublishGate
    {
        private readonly ReaderWriterLockSlim _gate =
            new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        internal IDisposable EnterRead()
        {
            _gate.EnterReadLock();
            return new Releaser(_gate, read: true);
        }

        internal IDisposable EnterWrite()
        {
            if (_gate.IsReadLockHeld && !_gate.IsWriteLockHeld)
                return Releaser.Noop;
            _gate.EnterWriteLock();
            return new Releaser(_gate, read: false);
        }

        private sealed class Releaser : IDisposable
        {
            internal static readonly IDisposable Noop = new Releaser(null, read: false);
            private readonly ReaderWriterLockSlim _gate;
            private readonly bool _read;

            internal Releaser(ReaderWriterLockSlim gate, bool read)
            {
                _gate = gate;
                _read = read;
            }

            public void Dispose()
            {
                if (_gate == null)
                    return;
                if (_read)
                    _gate.ExitReadLock();
                else
                    _gate.ExitWriteLock();
            }
        }
    }
}
