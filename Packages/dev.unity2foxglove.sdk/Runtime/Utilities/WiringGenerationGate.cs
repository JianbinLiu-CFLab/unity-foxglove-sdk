using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
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
            Exception firstFailure = null;
            for (var i = _cleanup.Count - 1; i >= 0; i--)
            {
                try
                {
                    _cleanup[i]();
                }
                catch (Exception ex)
                {
                    firstFailure ??= ex;
                }
            }

            _cleanup.Clear();
            if (firstFailure != null)
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    public sealed class OptionalStartExecutorGate
    {
        private bool _attempted;
        private bool _started;

        public bool Attempted => _attempted;
        public bool Started => _started;

        public bool TryStart(Action start, Action warnMissing)
        {
            if (_attempted)
                return _started;

            if (start == null)
            {
                _attempted = true;
                warnMissing?.Invoke();
                return false;
            }

            start();
            _attempted = true;
            _started = true;
            return true;
        }

        public void Reset()
        {
            _attempted = false;
            _started = false;
        }
    }
}
