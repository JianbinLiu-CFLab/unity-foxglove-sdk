# Unity2Foxglove v1.5.0 Release Notes

Release date: 2026-05-14

Unity2Foxglove v1.5.0 is a maintainability-focused release that reorganizes the SDK source tree by feature/category while keeping runtime behavior, namespaces, public APIs, serialized fields, and Unity `.meta` GUIDs stable.

## Highlights

- Runtime and editor source folders are now easier to scan by feature:
  - `Runtime/Components` replaces the previous broad `Runtime/Unity` source root.
  - `Runtime/Utilities` replaces the previous `Runtime/Util` source root.
  - Core, Transport, Components, Editor, IO, and Schemas files are grouped by responsibility.
- Schema assets and code are clearer:
  - JSON message definitions live under `Runtime/Schemas/MessageDefinitions`.
  - JSON schema files live under `Runtime/Schemas/Json`.
  - Schema registry code lives under `Runtime/Schemas/Registry`.
  - Generated protobuf messages live under `Runtime/Schemas/Proto/Generated/Messages`.
  - Generated protobuf descriptors live under `Runtime/Schemas/Proto/Generated/Descriptors`.
- WSS client churn is quieter: normal TLS/WebSocket pre-handshake disconnects are quiet by default, with opt-in diagnostics available through `ManagedWebSocketOptions.LogPreHandshakeClientDisconnects`.
- A new 200-series follow-up note records the observed multi-client playback-control timeout storm when Foxglove Desktop and Foxglove Web are connected at the same time.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- Namespaces and public APIs are preserved despite physical folder moves.
- Unity `.meta` files were moved with their paired assets to preserve GUIDs.
- The package version is now `1.5.0`.
- The multi-client playback-control timeout issue is documented for a future phase; it is not fixed in this release.

## Verification

Completed before preparing this release:

```bash
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
uv run --python 3.11 Scripts/release/validate_package.py
uv run --python 3.11 Scripts/performance/run_baseline.py --quick --output build/performance/phase63-schemas-proto-generated-organization
```

The quick performance run may report a NuGet vulnerability-index warning when `https://api.nuget.org/v3/index.json` is unavailable; the recorded run completed with `Result: PASS`.
