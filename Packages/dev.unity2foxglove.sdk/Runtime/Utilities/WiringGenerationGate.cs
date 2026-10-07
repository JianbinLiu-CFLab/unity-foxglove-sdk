using System;
using System.Collections.Generic;
using System.Threading;

namespace Unity.FoxgloveSDK.Utilities
{
    public sealed class WiringGenerationGate
    {
        private int _generation;
        private int _active;

        public int Activate()
        {
            var generation = Interlocked.Increment(ref _generation);
            Volatile.Write(ref _active, 1);
            return generation;
        }

        public int Invalidate()
        {
            Interlocked.Exchange(ref _active, 0);
            return Interlocked.Increment(ref _generation);
        }

        public int Capture()
        {
            return Volatile.Read(ref _generation);
        }

        public bool IsCurrent(int generation)
        {
            return Volatile.Read(ref _active) == 1
                   && Volatile.Read(ref _generation) == generation;
        }
    }

    public sealed class DemoWiringOwnership : IDisposable
    {
        private readonly List<Action> _cleanup = new List<Action>();
        private bool _disposed;

        public void Add(IDisposable resource)
        {
            if (resource == null)
                return;

            Add(resource.Dispose);
        }

        public void Add(Action cleanup)
        {
            if (cleanup == null)
                throw new ArgumentNullException(nameof(cleanup));
            if (_disposed)
                throw new ObjectDisposedException(nameof(DemoWiringOwnership));

            _cleanup.Add(cleanup);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            for (var i = _cleanup.Count - 1; i >= 0; i--)
                _cleanup[i]();
            _cleanup.Clear();
        }
    }
}
