# 1. Running the Demo -- Step-by-Step Guide

This document walks you through running the Unity2Foxglove Demo project from scratch and verifying each feature.

## 1.1 Purpose

Use this guide to open the demo project, enter Play Mode, connect Foxglove, and confirm the main panels display data.

## 1.2 Application

Start here after cloning the repository if you want to see the SDK working before integrating it into your own Unity project.

## 1. Step 1: Open the project

1. Launch **Unity Hub**
2. Click **Add** in the top-right, select **Add project from disk**
3. Browse to the `Untiy2Foxglove/` directory and click **Select Folder**
4. A project named `Untiy2Foxglove` appears in the Unity Hub list
5. Click the project name to open it and wait for the Unity Editor to start and compile scripts (may take 1-3 minutes the first time)

> **Confirm Unity version**: Menu `Help > About Unity`. The version should be 2022.3 or later.

## 2. Step 2: Open the sample scene

1. In the **Project** window at the bottom of the Unity Editor, expand `Assets/Scenes/`
2. Double-click `SampleScene` to open it
3. Confirm you can see in the scene:
   - **Main Camera** (with `FoxgloveCameraPublisher` component)
   - **Cube** (with `FoxgloveTransformPublisher`, `FoxgloveSceneCubePublisher`, `MouseDragCube` components)
   - **Foxglove** (with `FoxgloveManager`, `FoxgloveDemoSetup` components)
   - **Hub** (with `TestLog` component, FoxRun log source)

## 3. Step 3: Enter Play Mode

1. Click the **Play** button on the Unity Editor toolbar (triangle icon), or press `Ctrl+P`
2. The Game view should show the scene with a colored cube
3. In the Console window (`Window > General > Console`), you should see the Foxglove WebSocket server startup log

## 4. Step 4: Open Foxglove Desktop

1. Launch **Foxglove Desktop** (download from https://foxglove.dev/download if not installed)
2. Confirm Foxglove and Unity are running on the same machine

## 5. Step 5: Connect via WebSocket

1. In Foxglove Desktop, click the **Open connection** button on the left
2. In the dialog, select **Foxglove WebSocket**
3. Enter `ws://127.0.0.1:8765` in the **WebSocket URL** field
4. Click **Open**
5. After a successful connection, the left panel shows all available topics

## 6. Step 6: Import the layout

1. In Foxglove Desktop's top menu bar, click **Layout**
2. Select **Import from file...**
3. In the file picker, navigate to (path relative to repository root):
   ```
   Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/FoxgloveLayout.json
   ```
   > If this path is not visible (the Samples~ directory may be hidden in Unity file dialogs), use the alternative:
   > - Find the file directly with your file manager (e.g., Windows Explorer)
   > - In Foxglove, click **Layout > Import > Paste layout JSON**, open the `.json` file in a text editor, select all, copy, and paste

After a successful import, the Foxglove interface automatically arranges into the pre-configured panel layout.

## 7. Step 7: Verify each panel

### 7.1 3D panel

- **Position**: layout top-left
- **Expected content**:
  - A coordinate grid is visible
  - A colored cube is visible (from the `/scene` topic)
  - The `unity_world` -> `unity_cube` coordinate frame is visible (from the `/tf` topic)
- **Interaction**: left-drag to rotate view, scroll to zoom, right-drag to pan

### 7.2 Camera/Image panel

- **Position**: layout upper-center
- **Expected content**: live Unity Game view render (from the `/unity/camera` topic)
- **Confirmation**: the image should match the Unity Editor Game view
- **Note**: if the Game view is not focused in the Unity Editor or camera settings are abnormal, the image may appear black; keep the Game view visible

### 7.3 Plot panel

- **Position**: layout upper-right
- **Expected content**: three curves -- x (blue), y (orange), z (yellow) for `/tf.translation.x`, `/tf.translation.y`, `/tf.translation.z`
- **Interaction verification**: go back to the Unity Game view, left-drag the cube to rotate, right-drag to pan, and watch the Plot panel curves change accordingly

### 7.4 Parameters panel

- **Position**: layout lower-right area
- **Expected content**:
  - `/cube/color`: displayed as a color array, e.g., `[0.0, 1.0, 0.0, 1.0]` (green)
  - `/cube/scale`: displayed as a number, e.g., `1.0`
- **Interaction verification**: change `/cube/color` (e.g., to `[1.0, 0.0, 0.0, 1.0]` for red) and click **Set**. The cube color in the 3D panel should change to red.
- **Interaction verification**: change `/cube/scale` (e.g., to `2.0`) and click **Set**. The cube in the 3D panel should become larger.
- **Note**: if the cube does not change after modification, confirm Foxglove is connected and Unity is in Play Mode. Check the Unity Console for parameter change logs.

### 7.5 Service Call panel

- **Position**: layout lower-center-left
- **Expected content**: service name `/cube/reset_pose`, request body empty object `{}`
- **Interaction verification**: click the **Call service** button
  - The cube in Unity should return to origin (position = (0,0,0), rotation = identity)
  - The cube color should reset to green and scale to 1.0
  - The Plot panel curves should jump to zero
- **Confirm response**: the panel bottom should show the service result (`{"status":"ok"}`)

### 7.6 Topic Graph panel

- **Position**: layout lower-center-center
- **Expected content**: a node-link graph showing the topology relationships among all active topics
- You should see `/tf`, `/scene`, `/unity/camera`, `/debug/position`, `/debug/health` topic nodes

### 7.7 Raw Messages panel (/tf)

- **Position**: layout lower-left
- **Expected content**: real-time scrolling raw JSON messages from the `/tf` topic

### 7.8 Publish panel

- **Position**: layout lower-right area
- **Expected content**: pre-configured target topic `/unity/camera` and message template
- Can be used for manual testing of message publishing to Unity (advanced, not required)

### 7.9 Raw Messages panel (/debug/position)

- **Position**: layout lower-right corner
- **Expected content**: real-time scrolling raw JSON messages from the `/debug/position` topic
- This proves the FoxRun Source Generator is functioning correctly in the Editor

## 8. Completion

If all panels and interactions above work correctly, the demo feature verification has passed.

> For IL2CPP Player verification, continue to **[03 Building IL2CPP Standalone](03%20Building%20IL2CPP%20Standalone.md)**.
