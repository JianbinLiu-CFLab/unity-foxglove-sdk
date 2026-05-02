using UnityEngine;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// IFoxgloveLogger implementation that routes to Unity's Debug.LogWarning/Debug.LogError.
    /// </summary>
    public class UnityLogger : IFoxgloveLogger
    {
        public void LogWarning(string message) => Debug.LogWarning($"[Foxglove] {message}");
        public void LogError(string message) => Debug.LogError($"[Foxglove] {message}");
    }
}
