# Phase 50 Critical Stability and Security Fixes

## Purpose

Phase 50 closes confirmed P0/P1 stability and security findings before adding more feature surface. The work is intentionally surgical: each fix preserves existing public SDK behavior and adds a regression check in `Phase50Validation`.

## Fix Matrix

| Batch | Issue | Fix | Regression evidence |
| --- | --- | --- | --- |
| 50A | `PlaybackClock.NowNs` advanced playback time and let `ToState()` mutate state. | Added explicit `Tick()` / internal `Tick(DateTime)`, made `NowNs` and `ToState()` pure reads, and ticked playback once in `FoxgloveRuntime.Tick()`. | `50A-1` through `50A-6` |
| 50B | `HandleClient` disposed `TcpClient` while the async send loop could still write. | Moved socket ownership into `WsConnection`, completed the queue, waited for the send loop with a bounded timeout, then closed stream/socket. Reset write timeout after handshake. | `50B-1` through `50B-3` |
| 50C | Physical `TestLog_FoxRun.g.cs` fallback was stale and missed `/debug/position2` policy metadata. | Refreshed the fallback file, included `IFoxgloveLogPolicySource`, and unignored the specific demo fallback so freshness is durable. | `50C-1` through `50C-3` |
| 50D | `_runtime` and `_recorder` used ordinary cross-thread reads/writes. | Switched publication and hot-path snapshots to `Volatile.Read` / `Volatile.Write`. | `50D-1`, `50D-2` |
| 50E | `ClearSession()` left client channel and graph state; `Dispose()` kept `OnClientMessage` subscribers. | Added `ConnectionGraphRegistry.Clear()`, cleared client channels and graph state, and nulled `OnClientMessage` on dispose. | `50E-1` through `50E-3` |
| 50F | WebSocket handshake accepted unbounded line/header input. | Added 8192-byte line limit and 100-header limit while preserving no-Origin Foxglove clients. | `50F-1` through `50F-4` |
| 50G | Shared emitter inserted topic/schema strings into C# literals without escaping. | Added a generated C# string literal escaping helper and used it for topic/schema emission. | `50G-1`, `50G-2` |
| 50H | `FoxgloveLogHub` static instance could survive no-domain-reload Play Mode. | Added `SubsystemRegistration` reset for static hub state. | `50H-1`, `50H-2` |
| 50I | `RecordingController.AttachToSession()` could double-subscribe parameter metadata writes. | Made attach detach any active recorder first and unsubscribe before subscribing. | `50I-1` |
| 50J | `McapReplayEngine` cast oversized chunk inner record lengths to `int`. | Guarded `len > int.MaxValue` and malformed/truncated message lengths with `InvalidDataException`. | `50J-1`, `50J-2` |
| 50K | `SceneCubeColor` equality guard was inverted. | Same color now returns immediately; changed color updates state, renderer, and event. | `50K-1`, `50K-2` |

## Excluded

- CSWSH Origin validation remains excluded because Phase 28 already covers it.
- P2/P3 allocation, style, naming, and broader maintainability items remain deferred to later phases.
- WSS/TLS/auth, FoxRun triggered events, and Ouster workflows were not started.

## Verification

Runtime validation:

```powershell
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
```

Result: `All checks passed.` Phase 50 reported `32 checks passed.`

Quick performance:

```powershell
python Scripts/run_performance_baseline.py --quick --output build/performance/phase50-stability
```

Result: `PASS`, 10/10 scenarios passed. JSON evidence written under `build/performance/phase50-stability/phase35_performance_quick_20260511-140727.json`.

Release package validation:

```powershell
python Scripts/validate_release_package.py
```

Result: `validate_release_package: 27 check(s) passed.`

## Manual Smoke Recommendation

Open `Unity2Foxglove`, enter Play Mode, and confirm Foxglove Desktop still connects to `ws://127.0.0.1:8765`. In the FullDemo scene, verify the FoxRun topics include `/debug/position`, `/debug/health`, and `/debug/position2`, and that replay controls still advance only through runtime ticks.
