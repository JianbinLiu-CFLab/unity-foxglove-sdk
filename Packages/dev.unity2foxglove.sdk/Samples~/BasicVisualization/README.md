# Basic Visualization Sample

## Purpose

Use this sample for the smallest possible Unity2Foxglove package smoke test.

It proves that an imported package can start the in-process Foxglove WebSocket server and publish the three core live topics:

- `/tf`
- `/scene`
- `/unity/camera`

## When to Use This Sample

Use **Basic Visualization** when:

- you just installed the package and want a fast first check;
- you want a small scene to copy from;
- you do not want Input System, URP sample assets, Parameters, Services, or FoxRun examples yet.

Use **Full Demo Visualization** instead when you want Parameters, Services, FoxRun, MCAP workflows, mouse controls, Input System, and URP sample assets.

Use the repository `Unity2Foxglove/` project when you are contributing to the SDK or running release/manual acceptance workflows.

## What This Sample Includes

- `Scenes/BasicVisualization.unity`
- A `FoxgloveManager`
- A cube with transform and scene publishers
- A camera with camera-image publishing
- `FoxgloveSimpleLayout.json`

This sample intentionally does not demonstrate:

- Parameters
- Services
- FoxRun
- MCAP recording/replay
- IL2CPP build validation

## Import Steps

1. Install `dev.unity2foxglove.sdk` through Unity Package Manager.
2. Open **Window > Package Manager**.
3. Select **Unity2Foxglove SDK**.
4. Expand **Samples**.
5. Click **Import** next to **Basic Visualization**.

After import, Unity places the sample under:

```text
Assets/Samples/Unity2Foxglove SDK/<version>/Basic Visualization/
```

## Run Steps

1. Open:

   ```text
   Assets/Samples/Unity2Foxglove SDK/<version>/Basic Visualization/Scenes/BasicVisualization.unity
   ```

2. Press Play in the Unity Editor.
3. Open Foxglove Desktop.
4. Open a Foxglove WebSocket connection to:

   ```text
   ws://127.0.0.1:8765
   ```

5. Import the simple layout:

   ```text
   Assets/Samples/Unity2Foxglove SDK/<version>/Basic Visualization/FoxgloveSimpleLayout.json
   ```

## Expected Result

Foxglove should show:

| Topic | Expected result |
|-------|-----------------|
| `/tf` | A `unity_world` to `unity_cube` transform stream |
| `/scene` | A cube primitive in the 3D panel |
| `/unity/camera` | The Unity camera image in the Image panel |

The exact panel arrangement depends on the imported Foxglove layout, but the topics above should be visible in the Topics panel.

## If It Does Not Work

- If Foxglove cannot connect, confirm Unity is in Play Mode.
- If no topics appear, confirm the `FoxgloveManager` GameObject is enabled.
- If `/unity/camera` is missing, confirm the Camera GameObject and camera publisher are enabled.
- If 3D appears empty, set the 3D display frame to `unity_world`.
- If you need Parameters, Services, FoxRun, or MCAP examples, import **Full Demo Visualization**.
