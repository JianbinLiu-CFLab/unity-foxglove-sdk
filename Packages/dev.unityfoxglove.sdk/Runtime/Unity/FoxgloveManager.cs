// Phase 4 will implement the full MonoBehaviour component.
// For Phase 0, this is a minimal placeholder to prove the
// Unity-facing API surface compiles and fits the architecture.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// MonoBehaviour entry point. Manages FoxgloveRuntime lifecycle
    /// within Unity's game loop. Full implementation in Phase 4.
    /// </summary>
    // public class FoxgloveManager : MonoBehaviour
    // {
    //     [SerializeField] private string _serverName = "Unity App";
    //     [SerializeField] private int _port = 8765;
    //
    //     private FoxgloveRuntime _runtime;
    //
    //     private void Awake()  { _runtime = new FoxgloveRuntime(); }
    //     private void OnEnable() { _runtime?.Start(_serverName, port: _port); }
    //     private void OnDisable() { _runtime?.Stop(); }
    //     private void OnDestroy() { _runtime?.Dispose(); }
    // }
}
