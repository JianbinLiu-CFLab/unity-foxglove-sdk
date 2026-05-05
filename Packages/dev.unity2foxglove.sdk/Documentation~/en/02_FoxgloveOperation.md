# 1. Foxglove Desktop Operation Guide

This document covers how to use the core panels in Foxglove Desktop and how to import the pre-configured layout.

## 1.1 Purpose

Use this guide to operate Foxglove Desktop after Unity has started publishing data.

## 1.2 Application

Read this when you can connect to `ws://127.0.0.1:8765` but are unsure how to configure 3D, Image, Plot, Parameters, or Service Call panels.

## 1.3 Prerequisites

- Unity is in Play Mode and FoxgloveManager has started
- Foxglove Desktop is connected via `ws://127.0.0.1:8765`

## 1.2 Topics panel

The Topics panel is in the left sidebar and shows all currently available data topics for subscription.

### 1.2.1 Viewing topics

- The topic list updates automatically after a successful connection
- Each topic shows its name (e.g., `/tf`, `/scene`, `/unity/camera`)
- Click a topic to expand it and see schema info and message rate

### 1.2.2 Subscribing to topics

- Select a topic in the Raw Messages panel to view real-time data
- Topics are not subscribed by default; data is only pulled when displayed

### 1.2.3 Common topics

| Topic | Schema | Description |
|-------|--------|-------------|
| `/tf` | `foxglove.FrameTransform` | Coordinate transforms, from FoxgloveTransformPublisher |
| `/scene` | `foxglove.SceneUpdate` | Scene entities, from FoxgloveSceneCubePublisher |
| `/unity/camera` | `foxglove.CompressedImage` | JPEG compressed camera frames, from FoxgloveCameraPublisher |
| `/debug/*` | schemaless JSON | [FoxRun] auto-published custom fields |

## 1.3 3D panel

The 3D panel visualizes data in three-dimensional space.

### 1.3.1 Basic operations

1. Click **+** > select **3D** panel
2. Configure in the panel settings bar at the top:
   - **Display frame**: select the reference coordinate frame, usually `unity_world`
   - Check the topics you want to display (`/tf`, `/scene`, etc.)

### 1.3.2 View controls

| Action | Mouse |
|--------|-------|
| Rotate | Left-click drag |
| Pan | Right-click drag |
| Zoom | Scroll wheel |
| Focus | Double-click an object |

### 1.3.3 Coordinate system notes

- **LeftHand mode** (default): X=Right, Y=Up, Z=Forward (Unity native)
- **RightHand mode**: X=Forward, Y=Left, Z=Up (ROS/standard robotics coordinate system)

Switch via the **Coordinate Mode** setting in FoxgloveManager's Inspector.

### 1.3.4 Display grid

In the 3D panel's Layers settings, you can add a Grid layer to show a reference grid:
- **Size**: grid size (default 10)
- **Divisions**: number of divisions (default 10)

## 1.4 Image / Camera panel

Used to display Unity camera frames.

### 1.4.1 Setup steps

1. Ensure the scene Camera has the **FoxgloveCameraPublisher** component
2. In Foxglove, click **+** > select **Image** panel
3. In panel settings, set **Image topic** to `/unity/camera`
4. The frame is displayed in real time

### 1.4.2 Parameter adjustment

Adjustable in the FoxgloveCameraPublisher component:
- **Width / Height**: resolution (default 640x480)
- **Jpeg Quality**: 10-100 (default 70)
- **Publish Rate Hz**: publish frequency (default 10)

## 1.5 Plot panel

The Plot panel draws numerical values as curves over time.

### 1.5.1 Adding a series

1. Click **+** > select **Plot** panel
2. Click **+** in the panel to add a series
3. Enter a data path in the **value** field:
   - `/tf.translation.x` -- X axis translation
   - `/tf.translation.y` -- Y axis translation
   - `/tf.translation.z` -- Z axis translation
   - `/tf.rotation.w` -- rotation quaternion W component
4. Customize each series' color and line width

### 1.5.2 Typical usage

Add `/tf.translation.x`, `/tf.translation.y`, and `/tf.translation.z` as three series to observe the object's movement trajectory on all three axes.

### 1.5.3 Time range

The time axis at the bottom of the Plot panel can be dragged to display data over different time windows. Default: last 30 seconds.

## 1.6 Parameters panel

The Parameters panel lets you view and modify writable parameters registered on the Unity side.

### 1.6.1 Viewing parameters

1. Click **+** > select **Parameters** panel
2. The parameter list automatically shows all registered parameters and their current values

### 1.6.2 Modifying parameter values

- Only parameters with `Writable` set to `true` can be edited
- Click a parameter value, enter a new value, and press Enter to confirm
- Changes sync to Unity in real time, triggering `OnParameterChanged` callbacks

### 1.6.3 Cube example parameters

When FoxgloveSceneCubePublisher is present in the scene, the following parameters can be registered to dynamically control the cube appearance:

| Parameter name | Type | Default | Description |
|----------------|------|---------|-------------|
| `/cube/color` | number[] | `[0, 1, 0, 1]` | RGBA color, range 0-1 |
| `/cube/scale` | number[] | `[1, 1, 1]` | XYZ scale |
| `/cube/reset_pose` | -- | -- | Corresponding Service, not a parameter |

Parameters are registered via the `FoxgloveParameterComponent` component or programmatically via `FoxgloveManager.RegisterParameter()`.

## 1.7 Service Call panel

The Service Call panel calls remote services registered on the Unity side.

### 1.7.1 Invoking a service

1. Click **+** > select **Call Service** panel
2. Select the service from the **Service** dropdown, e.g., `/cube/reset_pose`
3. Fill in a JSON request body in the **Request** area (usually `{}`)
4. Click the **Call service** button
5. The response is displayed at the bottom

### 1.7.2 Notes

- Service processing is driven by Unity's main thread via `DrainServiceCalls`
- Default timeout is 10 seconds (`FoxgloveServiceRegistry.DefaultTimeout`)
- Maximum request body size is 1 MiB

## 1.8 Problems panel

The Problems panel displays issues and errors in the current connection.

### 1.8.1 Common errors and meanings

| Error message | Meaning | Resolution |
|---------------|---------|------------|
| Schema not found | An unregistered schema name was used | Check spelling; confirm it is registered in `DefaultSchemaRegistry` |
| Channel not found | A non-existent channel was referenced | Check the Channel ID |
| Connection refused | Cannot connect to the Unity server | Confirm Unity is in Play Mode and the port is not occupied |
| Timeout | Service call timed out | Check service handling logic or adjust the timeout parameter |
| Unsupported compression | Unsupported compression algorithm | Only lz4, zstd, and uncompressed are supported |

## 1.9 Layout import

The SDK provides a pre-configured Foxglove layout file `FoxgloveLayout.json`, located in the package directory:

```
Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/FoxgloveLayout.json
```

### 1.9.1 Import steps

1. In Foxglove Desktop, click the top menu **Layout** > **Import layout...**
2. Select the `FoxgloveLayout.json` file
3. After import, the following pre-configured panels open automatically:

| Panel | Configuration |
|-------|---------------|
| 3D (top-left) | Coordinate frame `unity_world`, display `/scene`, hide `/unity/camera` |
| Raw Messages (left-middle) | Subscribed to `/tf` |
| Image (middle-left) | Display `/unity/camera` |
| Call Service (middle-lower-left) | Pre-configured `/cube/reset_pose`, request body `{}` |
| Topic Graph (middle-lower-center) | Show topic topology |
| Plot (bottom-left) | Plot `/tf.translation.x/y/z` curves |
| Parameters (bottom-right) | Show writable parameters |
| Publish (bottom-right) | Pre-configured `/unity/camera` with `foxglove.CompressedImage` publish |

### 1.9.2 Layout structure

The imported layout has three columns:
- **Left column**: 3D panel (main view) + Raw Messages (/tf)
- **Center column**: Image (camera frames) + Call Service + Topic Graph
- **Right column**: Plot (/tf curves) + Parameters + Publish + Raw Messages (/debug)

You can freely adjust panel positions and sizes. After adjusting, save your custom layout via **Layout** > **Export layout...**.
