# Unity2Foxglove v1.9.7 Release Notes

Release date: 2026-11-02

Unity2Foxglove v1.9.7 extends FoxRun bidirectional data flow, strengthens optional ROS2 integration, and keeps release metadata synchronized across the Unity packages and validation tooling.

## Highlights

- Added typed JSON and Protobuf FoxRun publish, subscribe, and full-duplex bindings with bounded inbound staging, main-thread application, session-frozen policy, and loop suppression.
- Added optional ROS2 Native subscriptions and static ROS2 interface/typesupport generation for custom FoxRun DTOs without adding ROS2 dependencies to the core SDK.
- Unified Manager Inspector publish/subscribe transport settings and added conditional shared R2FU runtime controls.
- Added `Unity Replay Sync` ownership rules for bounded timeline and Plot seeks without republishing replay-applied values to live outputs.
- Hardened ROS2 typesupport selection, catalog identity, preflight diagnostics, descriptor chunking, batch acceptance, connection coalescing, and inbound admission diagnostics.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- Core WebSocket, MCAP, Replay, and FoxRun workflows remain ROS-free.
- ROS2 For Unity packages remain optional preview packages; local Editor acceptance does not certify every Player or network topology.

## Verification

The release gate covers Windows Unity Editor acceptance for Humble/FastDDS, Jazzy/FastDDS, Lyrical/FastDDS, and Lyrical/Zenoh, plus official MCAP differential checks, source-generator parity/freshness, package boundaries, and FoxRun panel behavior.

```bash
python Scripts/release/bump_version.py 1.9.7 --date 2026-11-02 --dry-run
python Scripts/package/validate_unity_package.py
```
