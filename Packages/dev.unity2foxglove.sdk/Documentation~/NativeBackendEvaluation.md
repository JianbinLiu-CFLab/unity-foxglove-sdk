# Native Backend Feasibility Evaluation

**Date:** 2026-05-01
**Phase:** 5
**Status:** Evaluated — not implemented

## Source

- C FFI header: `foxglove-sdk/c/include/foxglove-c/foxglove-c.h`
- SDK version: `main@b298c3d1649e6e5dfd77a53b12ab7c27f97c7aba`

## Key C API Surface

### Server Lifecycle

| Function | Purpose |
|----------|---------|
| `foxglove_server_start` | Start WebSocket server |
| `foxglove_server_stop` | Stop server |
| `foxglove_server_clear_session` | Reset session state |
| `foxglove_server_get_port` | Get assigned port |
| `foxglove_server_get_client_count` | Get connected client count |

### Channel & Publish

| Function | Purpose |
|----------|---------|
| `foxglove_channel_create_*` | Create typed channel (per schema) |
| `foxglove_channel_log_*` | Publish data to channel |

## Phase 5 Decision: Continue Pure C#

**Reason:** The pure C# `ManagedWsBackend` has completed Phase 0-4 verification including:
- RFC 6455 WebSocket handshake with subprotocol negotiation
- Channel advertise/subscribe/unsubscribe/MessageData routing
- FrameTransform, SceneUpdate, CompressedImage typed JSON channels
- Unity MonoBehaviour integration (Manager, Transform/SceneCube/Camera publishers)
- 160/160 automated tests

Introducing `NativeFoxgloveBackend` at this stage would add:
- **DLL build and distribution** — platform-specific native binaries for Windows, macOS, Linux
- **P/Invoke complexity** — string lifetime, callback threading, opaque pointer ownership
- **Error code mapping** — C error codes → managed exceptions
- **Shutdown ordering** — native cleanup must happen after managed teardown
- **Testing burden** — native backend requires Unity IL2CPP or native test infrastructure

## Trigger Conditions for Native Implementation

Switch to `NativeFoxgloveBackend` when any of these occur:

1. IL2CPP build fails and cannot be fixed with `link.xml`
2. `ManagedWsBackend` stability cannot meet production requirements
3. Performance cannot meet camera/high-frequency message needs
4. Official SDK capabilities (Parameters, Services, PlaybackControl) are needed and maintaining a parallel C# implementation would be more costly than P/Invoke

## Expected Native Plugin Artifacts

- Windows: `foxglove_c.dll` → `Plugins/native/x86_64/`
- macOS: `libfoxglove_c.dylib` → corresponding platform directory
- Linux: `libfoxglove_c.so` → corresponding platform directory

## P/Invoke Risks

| Risk | Mitigation |
|------|-----------|
| String lifetime | Use `Marshal.PtrToStringAnsi` / byte[] callbacks, avoid persistent string marshaling |
| Callback threading | Native callbacks may arrive on arbitrary threads — must marshal to Unity main thread |
| Opaque pointer ownership | `IntPtr` handles must be properly paired with create/destroy |
| Shutdown ordering | Native `stop` before DLL unload; dispose managed wrappers before native teardown |
| Error code mapping | Enum-based error codes with explicit conversion layer |

## Files to Update

- `Runtime/Transport/NativeFoxgloveBackend.cs` — current stub, no `[DllImport]` calls
- `Plugins/native/` — empty directory, reserved for future native DLLs

## Conclusion

Continue with pure C# `ManagedWsBackend` through Phase 5 IL2CPP verification. If the Windows IL2CPP build succeeds with `link.xml`, the native backend remains a deferred option.
