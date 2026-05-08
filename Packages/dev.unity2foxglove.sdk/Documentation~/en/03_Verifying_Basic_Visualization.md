# 3. Basic Visualization Sample

## Who should read this

Read this if you want the smallest package sample that proves Unity can publish to Foxglove.

## What you will do

You will import the Basic sample, open its scene, connect Foxglove, and verify `/tf`, `/scene`, and `/unity/camera`.

## 3.1 What This Sample Contains

The Basic sample is intentionally small. It is for first contact, not a full feature tour.

It contains:

- A Unity scene with a `FoxgloveManager`
- A transform publisher
- A scene cube publisher
- A camera publisher
- `FoxgloveSimpleLayout.json`

It does not focus on:

- Parameters
- Services
- FoxRun
- MCAP recording/replay
- IL2CPP acceptance

Use `FullDemoVisualization` or `Unity2Foxglove` for those.

## 3.2 Import the Sample

1. Open **Window > Package Manager**.
2. Select **Unity2Foxglove SDK**.
3. Open the **Samples** section.
4. Import **Basic Visualization**.

After import, Unity places the sample under:

`Assets/Samples/Unity2Foxglove SDK/<version>/Basic Visualization/`

Open:

`Assets/Samples/Unity2Foxglove SDK/<version>/Basic Visualization/Scenes/BasicVisualization.unity`

## 3.3 Run the Sample

1. Open the sample scene.
2. Press **Play**.
3. Confirm the Unity Console shows a WebSocket server on `ws://127.0.0.1:8765`.
4. Open Foxglove Desktop.
5. Connect to `ws://127.0.0.1:8765`.

## 3.4 Import the Simple Layout

In Foxglove, import:

`FoxgloveSimpleLayout.json`

The layout is included with the imported sample and in the package at:

`Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/FoxgloveSimpleLayout.json`

## 3.5 Verify the Result

Expected topics:

- `/tf`
- `/scene`
- `/unity/camera`

Expected panels:

- **3D** shows the cube or frame.
- **Image** shows the Unity camera.
- **Plot** can show `/tf.translation.x`, `/tf.translation.y`, and `/tf.translation.z`.

Move the Cube in Unity. Foxglove should update live.

## 3.6 If It Does Not Work

- If Foxglove cannot connect, verify Unity is in Play Mode.
- If `/unity/camera` is missing, verify the camera publisher is enabled.
- If 3D looks empty, set the display frame to `unity_world`.
- If topics appear but panels do not update, reconnect Foxglove.
