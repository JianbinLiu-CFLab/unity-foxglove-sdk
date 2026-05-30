# Phase 138D Acceptance Report (Virtual IMU Sensor)

Date: 2026-05-30

Scope: `138D_PHASE138D_VIRTUAL_IMU_SENSOR_PLAN.md`

This document records the manual acceptance steps and results for 138D only.

## Plan completion snapshot

- [x] `Imu` contract and vendored descriptor added under `Runtime/Schemas/Proto/*`.
- [x] Hand-written protobuf serializer (`ImuMessageBuilder`) implemented.
- [x] `VirtualImu` publisher component added with `Rigidbody` body-frame specific-force computation and bounded queue/back-pressure.
- [x] Schema registration wired through `ProtobufSchemaRegistryLoader.FromBytes(...).RegisterAll()`.
- [x] Offline validation hook added and registered (`--phase138d`).
- [x] Maze bootstrap integrated with IMU publisher for `/imu/data`.

## 自动验证（Offline checks）

- Command:
  - `dotnet run --project "Packages\dev.unity2foxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj" -- --phase138d`
- Status: user-confirmed PASS.

## 人工验收（Unity + Foxglove）

1. Open the `Virtual LiDAR Maze Demo` scene and enter Play mode.
2. Connect Foxglove Studio to Unity WebSocket endpoint.
3. Confirm `/imu/data` topic is present and schema is `unity2foxglove.Imu`.
4. Validate stream behavior:
   - startup publishes after a short warm-up,
   - timestamps are monotonic,
   - sample stream is continuous.
5. Validate sensor semantics:
   - static body yields gravity-consistent acceleration behavior,
   - acceleration and angular velocity follow vehicle movement/rotation.
6. Confirm no startup first-tick spike.

- Status: PASS.

## Decision

- 138D (Virtual IMU Sensor) manual acceptance: PASS.
