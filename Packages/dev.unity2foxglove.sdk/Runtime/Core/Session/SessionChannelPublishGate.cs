// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;

namespace Unity.FoxgloveSDK.Core
{
    internal sealed class SessionChannelPublishGate
    {
        private readonly object _sync = new object();
        private readonly ThreadLocal<int> _threadReads = new ThreadLocal<int>();
        private int _reads;
        private int _waitingWriters;
        private Thread _writer;
        private int _writerDepth;
        private Thread _upgrader;

        internal IDisposable EnterRead()
        {
            lock (_sync)
            {
                var current = Thread.CurrentThread;
                while (_writer != null && _writer != current
                       || _threadReads.Value == 0 && _writer == null && _waitingWriters != 0)
                    Monitor.Wait(_sync);
                _reads++;
                _threadReads.Value++;
            }
            return new Releaser(this, read: true);
        }

        internal IDisposable EnterWrite()
        {
            lock (_sync)
            {
                var current = Thread.CurrentThread;
                if (_writer == current)
                {
                    _writerDepth++;
                    return new Releaser(this, read: false);
                }

                var ownReads = _threadReads.Value;
                if (ownReads != 0)
                {
                    if (_upgrader != null)
                        throw new InvalidOperationException(
                            "Concurrent channel lifecycle changes from inside publish callbacks would deadlock.");
                    _upgrader = current;
                }

                _waitingWriters++;
                try
                {
                    while (_writer != null || _reads != ownReads)
                        Monitor.Wait(_sync);
                }
                finally
                {
                    _waitingWriters--;
                    if (ownReads != 0)
                        _upgrader = null;
                }

                _writer = current;
                _writerDepth = 1;
            }
            return new Releaser(this, read: false);
        }

        private void Exit(bool read)
        {
            lock (_sync)
            {
                if (read)
                {
                    _reads--;
                    _threadReads.Value--;
                }
                else if (--_writerDepth == 0)
                {
                    _writer = null;
                }
                Monitor.PulseAll(_sync);
            }
        }

        private sealed class Releaser : IDisposable
        {
            private SessionChannelPublishGate _gate;
            private readonly bool _read;

            internal Releaser(SessionChannelPublishGate gate, bool read)
            {
                _gate = gate;
                _read = read;
            }

            public void Dispose()
                => Interlocked.Exchange(ref _gate, null)?.Exit(_read);
        }
    }
}
