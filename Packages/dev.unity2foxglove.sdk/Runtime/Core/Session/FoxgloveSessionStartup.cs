using System;

namespace Unity.FoxgloveSDK.Core
{
    internal static class FoxgloveSessionStartup
    {
        internal static void Start(
            object gate,
            Func<bool> readFrozen,
            Action<bool> writeFrozen,
            Action startTransport)
        {
            bool wasFrozen;
            lock (gate)
            {
                wasFrozen = readFrozen();
                writeFrozen(true);
            }

            try
            {
                startTransport();
            }
            catch
            {
                lock (gate)
                    writeFrozen(wasFrozen);
                throw;
            }
        }
    }
}
