using System;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Abstract logger bridge for protocol errors and warnings.
    /// </summary>
    public interface IFoxgloveLogger
    {
        void LogWarning(string message);
        void LogError(string message);
    }

    /// <summary>Default logger that writes to Console.Error.</summary>
    public class ConsoleLogger : IFoxgloveLogger
    {
        public void LogWarning(string message) => Console.Error.WriteLine($"[Foxglove] {message}");
        public void LogError(string message) => Console.Error.WriteLine($"[Foxglove] {message}");
    }
}
