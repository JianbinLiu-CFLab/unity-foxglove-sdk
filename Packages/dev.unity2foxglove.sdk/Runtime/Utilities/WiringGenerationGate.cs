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
}
