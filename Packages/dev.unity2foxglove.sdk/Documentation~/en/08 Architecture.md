# 8. Architecture

## Who should read this

Read this if you are extending Unity2Foxglove, debugging protocol behavior, or reviewing how the runtime fits together.

## What you will do

You will understand the main runtime components, how Unity data reaches Foxglove, how MCAP recording/replay fits in, and where build-time helpers are used.

## 8.1 Runtime Overview

Unity2Foxglove embeds the Foxglove WebSocket server inside Unity.

Main pieces:

- `FoxgloveManager`: MonoBehaviour entry point and Inspector-facing configuration.
- `FoxgloveRuntime`: runtime coordinator for session, recording, replay, assets, parameters, and services.
- `FoxgloveSession`: protocol-level WebSocket session handling server info, channels, messages, Parameters, Services, client publish, assets, playback control, and connection graph.
- `ManagedWsBackend`: managed WebSocket transport.
- Publisher components: Unity-facing components that publish Transform, Scene, Camera, and FoxRun data.

## 8.2 Live Data Path

1. A Unity component gathers data.
2. The component resolves a `FoxgloveManager`.
3. The Manager registers a schema/channel if needed.
4. The component publishes JSON or binary payloads.
5. `FoxgloveSession` sends data over the WebSocket transport.
6. Foxglove Desktop renders the topic.

## 8.3 Coordinate Mode

`FoxgloveManager` exposes:

- `LeftHand`: Unity native left-handed coordinates.
- `RightHand`: converts to ROS/Foxglove right-handed coordinates.

Use one mode consistently for live publish, MCAP recording, and replay. Recorder channel metadata stores coordinate mode so replay can warn about mismatches.

## 8.4 Parameters and Services

Parameters are stored in a runtime parameter store and served through Foxglove's parameter operations.

Services are registered with descriptors and handled on the Unity main thread so handlers can safely touch Unity objects.

For user steps, see [04 Parameters and Services](04%20Parameters%20and%20Services.md).

## 8.5 MCAP Recording and Replay

Recording attaches to the runtime publish path and writes MCAP records for channels, schemas, messages, metadata, and indexes.

Replay loads an MCAP file, seeks by time, and forwards replay messages through the runtime. Live publishers can be disabled during replay to avoid duplicate data.

For user steps, see [06 MCAP Recording and Replay](06%20MCAP%20Recording%20and%20Replay.md).

## 8.6 FoxRun Build Behavior

In Editor, FoxRun uses a Roslyn source generator. For Player builds, a pre-build step writes physical `.g.cs` files so IL2CPP compiles the generated sources as normal Unity scripts.

Generated files are meant to be build artifacts, not hand-edited source.

## 8.7 IL2CPP Preservation

Unity2Foxglove uses JSON serialization and reflection-heavy dependencies. IL2CPP builds need preservation rules for Newtonsoft.Json and the SDK runtime assembly.

The practical build checklist is in [07 IL2CPP Build Guide](07%20IL2CPP%20Build%20Guide.md).

## 8.8 Extension Points

Common extension points:

- Add a new publisher component by deriving from `FoxglovePublisherBase`.
- Register Parameters and Services through `FoxgloveManager`.
- Add FoxRun attributes for debug values.
- Add MCAP replay adapters for custom scene objects.
- Add asset roots for file-backed resources.

## 8.9 Compatibility Notes

- SDK core targets Unity 6000.0 LTSC or later. Unity 2022 is not supported.
- The demo project is developed on Unity 6000.3.14f1 LTSC; compatible with Unity 6000.0.74f1 LTSC.
- WebGL is excluded.
- Foxglove WebSocket is bidirectional: Unity publishes data, Foxglove can send Parameters, Services, playback, assets, and client-published messages when those features are enabled.
