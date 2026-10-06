using System;

namespace Unity.FoxgloveSDK.Utilities
{
    public sealed class RunInBackgroundLease
    {
        private static readonly object Sync = new object();
        private static int ActiveLeaseCount;
        private static bool BaselineValue;
        private readonly Func<bool> _read;
        private readonly Action<bool> _write;
        private bool _active;

        private RunInBackgroundLease(Func<bool> read, Action<bool> write)
        {
            _read = read;
            _write = write;
            _active = true;
        }

        public static RunInBackgroundLease Acquire(Func<bool> read, Action<bool> write)
        {
            if (read == null)
                throw new ArgumentNullException(nameof(read));
            if (write == null)
                throw new ArgumentNullException(nameof(write));

            lock (Sync)
            {
                if (ActiveLeaseCount == 0)
                    BaselineValue = read();

                ActiveLeaseCount++;
                try
                {
                    write(true);
                    return new RunInBackgroundLease(read, write);
                }
                catch
                {
                    ActiveLeaseCount--;
                    throw;
                }
            }
        }

        public bool Release()
        {
            lock (Sync)
            {
                if (!_active)
                    return false;

                _active = false;
                ActiveLeaseCount--;
                if (ActiveLeaseCount > 0)
                    return true;

                try
                {
                    if (!_read())
                        return false;

                    _write(BaselineValue);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
