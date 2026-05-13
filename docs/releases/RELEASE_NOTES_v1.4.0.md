# Unity2Foxglove v1.4.0 Release Notes

Release date: 2026-05-13

Unity2Foxglove v1.4.0 focuses on secure local/LAN connection options, event-driven FoxRun telemetry, and more stable MCAP replay scrubbing.

## Highlights

- Added optional Unity-native `wss://` transport through `ManagedWssBackend`.
- Added Inspector-assisted local WSS certificate generation, root CA fingerprint display, and an optional root CA download helper.
- Added an optional shared query-token gate. This is a lightweight local/LAN connection gate, not user identity or full authorization.
- Added FoxRun `OnTrigger` publishing for explicit event snapshots, with generated `FoxRun_Trigger_...()` methods.
- Improved paused MCAP replay scrubbing: seeks update Unity scene state immediately, stale replay queues are cleared, and bounded panel history is rebuilt after the scrub settles.
- Kept plain `ws://127.0.0.1:8765` as the default and recommended same-machine development mode.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- Existing scenes continue to use plain `WebSocket` mode unless `SecureWebSocket` is selected.
- WSS mode requires a PFX certificate containing a private key. The local certificate generator defaults to the built-in Unity/Mono certificate backend and writes ignored files under `UserSettings/Unity2Foxglove/Certificates/`. OpenSSL remains available only as an explicit fallback generator.
- Foxglove Web on local loopback does not require WSS. Use WSS when Unity should own TLS for demos, lab setups, or LAN scenarios.
- FoxRun `OnTrigger` topics publish only when user code calls the generated trigger method after updating the value.
- Replay scrub improvements target coherent scene reproduction and bounded panel history. They are not deterministic physics/input replay.

## Documentation

- Start with [package documentation](../../Packages/dev.unity2foxglove.sdk/Documentation~/README.md).
- Use [Secure WSS](../../Packages/dev.unity2foxglove.sdk/Documentation~/en/15_Secure_WSS.md) for WSS/TLS setup and token-gate limitations.
- Use [FoxRun Zero-Code Publishing](../../Packages/dev.unity2foxglove.sdk/Documentation~/en/07_FoxRun_Zero_Code_Publishing.md) for `OnTrigger` examples.
- Use [MCAP Recording and Replay](../../Packages/dev.unity2foxglove.sdk/Documentation~/en/08_MCAP_Recording_and_Replay.md) for replay workflow boundaries.

## Verification

Run before publishing the release:

```bash
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/release/validate_package.py
python Scripts/performance/run_baseline.py --quick --output build/performance/release
```
