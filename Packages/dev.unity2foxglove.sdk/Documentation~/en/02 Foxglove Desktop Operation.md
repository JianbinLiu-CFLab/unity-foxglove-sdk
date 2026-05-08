# 2. Foxglove Operation

## Who should read this

Read this if Unity is already running a Unity2Foxglove WebSocket server and you want to operate Foxglove Desktop.

## What you will do

You will connect Foxglove, inspect topics, use common panels, import layouts, edit Parameters, call Services, and diagnose Problems panel messages.

## 2.1 Connect to Unity

1. Start Play Mode in Unity.
2. Open Foxglove Desktop.
3. Click **Open connection**.
4. Select **Foxglove WebSocket**.
5. Enter `ws://127.0.0.1:8765`.
6. Click **Open**.

If the connection succeeds, the top bar shows `ws://127.0.0.1:8765` and the **Topics** panel starts listing Unity topics.

## 2.2 Import a Layout

Use layouts to avoid rebuilding panels by hand.

### 2.2.1 Basic layout

Use this for the Basic sample:

`Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/FoxgloveSimpleLayout.json`

It focuses on:

- 3D
- Image
- Plot
- Raw topic inspection

### 2.2.2 Full demo layout

Use this for the Full Demo sample or `Unity2Foxglove` project:

`Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/FoxgloveFullLayout.json`

The standalone demo project also keeps a copy at:

`Unity2Foxglove/Configs/FoxgloveFullLayout.json`

It includes:

- 3D
- Image
- Plot
- Parameters
- Service Call
- Topic Graph
- Publish
- Debug topics

## 2.3 Topics Panel

Use **Topics** to confirm what Unity is publishing.

Expected common topics:

- `/tf`
- `/scene`
- `/unity/camera`
- `/debug/...` if FoxRun sources are active

If a topic is missing, first check whether the related Unity component is enabled and whether its `Publish Rate Hz` is greater than `0`.

## 2.4 3D Panel

Use **3D** for transforms, scene primitives, and frame relationships.

1. Add a **3D** panel.
2. Set the display frame to `unity_world` for the default Unity sample.
3. Enable `/tf` and `/scene`.
4. Move or rotate the object in Unity.

If the object appears mirrored or rotated unexpectedly, check `FoxgloveManager > Coordinate Mode` and the Transform publisher settings.

## 2.5 Image Panel

Use **Image** to view camera output.

1. Add an **Image** panel.
2. Select `/unity/camera`.
3. Verify the image updates while Play Mode is running.

If the panel is black, check the Unity Camera, resolution, JPEG quality, and whether another camera is rendering over it.

## 2.6 Plot Panel

Use **Plot** to watch numeric fields over time.

Useful paths for `/tf` include:

- `/tf.translation.x`
- `/tf.translation.y`
- `/tf.translation.z`

Move the Cube in Unity and watch the curve change.

## 2.7 Parameters Panel

Use **Parameters** to read and edit runtime values.

In the Full Demo, expected parameters include:

- `/cube/color`
- `/cube/scale`

Click a value, edit the JSON or number, and confirm the Unity object changes.

## 2.8 Service Call Panel

Use **Service Call** for actions such as reset.

For the Full Demo:

1. Add a **Service Call** panel.
2. In panel settings, choose service `/cube/reset_pose`.
3. Put `{}` in the request box.
4. Click **Call service /cube/reset_pose**.

Do not put `/cube/reset_pose` inside the JSON request. The service name is selected in panel settings.

## 2.9 Problems Panel

Use **Problems** when something looks connected but does not work.

Common messages:

- `service has not been advertised`: reconnect Foxglove or verify the Unity service registration.
- `Service call timed out`: verify the Unity side completed the service handler.
- JSON parse error: the request box must contain valid JSON such as `{}`.

## 2.10 Layout Tips

- Keep a simple layout for first connection tests.
- Keep a full layout for demo acceptance.
- If Foxglove looks stale after changing Unity capabilities, reconnect the WebSocket connection.
