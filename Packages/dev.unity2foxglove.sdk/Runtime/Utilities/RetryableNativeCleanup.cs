using System;

namespace Unity.FoxgloveSDK.Utilities
{
    public static class RetryableNativeCleanup
    {
        public static bool TryRemove<T>(ref T handle, Action<T> remover, Action<Exception> report)
            where T : class
        {
            if (handle == null)
                return true;

            try
            {
                remover(handle);
                handle = null;
                return true;
            }
            catch (Exception ex)
            {
                report?.Invoke(ex);
                return false;
            }
        }
    }
}
