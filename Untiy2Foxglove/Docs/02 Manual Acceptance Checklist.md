# 1. Foxglove Manual Acceptance Checklist

This document is used to systematically verify the correctness of each Unity2Foxglove SDK feature in Foxglove Desktop. Check each item in order.

## 1.1 Purpose

Use this checklist to verify a build or release candidate manually.

## 1.2 Application

Use it after Editor Play Mode starts successfully, after an IL2CPP Player build, or before tagging a release.

## 1. Prerequisites

- [ ] Unity Editor has opened the `Untiy2Foxglove` project
- [ ] The sample scene `Assets/Scenes/SampleScene.unity` is loaded
- [ ] Unity is in Play Mode
- [ ] Foxglove Desktop is launched and connected via `ws://127.0.0.1:8765`
- [ ] Layout has been imported from `FoxgloveLayout.json`

---

## 2. 3D panel -- /tf coordinate frame

**Expected behavior**:
- Coordinate frames named `unity_world` and `unity_cube` are visible in the 3D panel
- `unity_world` is a fixed reference frame at the origin
- `unity_cube` follows the Cube GameObject's position/rotation
- A line connects the two showing the parent-child relationship

**Interaction verification**:
1. Drag the cube in Unity with the mouse (left-drag to rotate, right-drag to pan)
2. Observe whether the `unity_cube` frame follows in the 3D panel
3. Click **Call service** to invoke `/cube/reset_pose` and confirm `unity_cube` returns to origin

**If not passing**:
- Confirm the `/tf` topic exists and is green in the Foxglove Topics list
- Check the Unity Console for "FoxgloveManager" related errors
- Check that the `FoxgloveTransformPublisher` component on the `Cube` GameObject is enabled
- Try disconnecting and reconnecting Foxglove WebSocket

---

## 3. 3D panel -- /scene cube primitive

**Expected behavior**:
- A colored cube is visible in the 3D panel (default green)
- The cube's position matches the Cube GameObject in Unity
- The cube's color and size can be modified via the Parameters panel

**Interaction verification**:
1. Drag the cube in Unity and confirm the 3D panel cube moves synchronously
2. Change `/cube/color` to `[1.0, 0.0, 0.0, 1.0]` (red) and confirm the 3D cube turns red
3. Change `/cube/scale` to `2.0` and confirm the 3D cube becomes larger
4. Call `/cube/reset_pose` and confirm the cube returns to origin and restores green

**If not passing**:
- Confirm the `/scene` topic exists in the Topics list
- Confirm `/scene` visibility is ON in the 3D panel's Topics settings (pre-configured in the layout)
- Check that the `FoxgloveSceneCubePublisher` component on `Cube` is enabled
- Try manually checking the `/scene` topic's visible option in the 3D panel settings

---

## 4. Camera/Image panel -- /unity/camera

**Expected behavior**:
- The Image panel shows the Unity Camera's live render
- The content matches the Game view in the Unity Editor
- The image refreshes continuously at a consistent frame rate

**Interaction verification**:
1. Compare the Image panel content with the Unity Editor Game view; confirm they match
2. Drag the cube or rotate the scene view; confirm the image updates in real time
3. Confirm the image has no obvious tearing, artifacts, or freezes

**If not passing**:
- If the image is black:
  - Confirm the Unity Editor Game view is visible (not minimized or on the Scene tab)
  - Check that the `FoxgloveCameraPublisher` component on `Main Camera` is enabled
  - Confirm the `_camera` field of `FoxgloveCameraPublisher` references `Main Camera` (not null)
  - Check Unity Console for rendering-related errors
- If the image is very choppy: check network conditions, confirm local loopback latency is normal
- Confirm the `/unity/camera` topic exists in the Topics list and has a non-zero data rate

---

## 5. Plot panel -- /tf.translation curves

**Expected behavior**:
- The Plot panel shows three curves (x blue, y orange, z yellow)
- Curves update in real time, reflecting the cube's world-space position
- Curves are smooth with no obvious breaks or jumps

**Interaction verification**:
1. Right-drag the cube in Unity to pan; observe whether the three curves change direction intuitively
2. Call `/cube/reset_pose`; confirm all three curves immediately jump to zero
3. Drag the cube again; confirm curves restart from zero
4. Let the cube sit still for a few seconds; confirm curves stay flat

**If not passing**:
- If curves show no data:
  - Confirm the `/tf` topic has data in the Foxglove Topics list
  - Try manually adding series to the Plot panel: select `/tf.translation.x`, `/tf.translation.y`, `/tf.translation.z`
  - Check that the Plot panel's time range is appropriate (use scroll wheel to zoom the time axis)
- If curves oscillate heavily: confirm `/tf` publish frequency is normal (should be 10 Hz); check for multiple publishers

---

## 6. Parameters panel -- /cube/color

**Expected behavior**:
- The `/cube/color` parameter appears in the Parameters panel
- Type is array `number[]` with value in `[r, g, b, a]` format
- Modifying the value changes the cube color in the 3D panel synchronously
- The parameter has write permission (writable)

**Interaction verification**:
1. Confirm initial `/cube/color` value is `[0.0, 1.0, 0.0, 1.0]` (green)
2. Change to `[1.0, 0.0, 0.0, 1.0]` (red) > click **Set** > confirm cube turns red
3. Change to `[0.0, 0.0, 1.0, 0.5]` (semi-transparent blue) > confirm cube turns blue (transparency limitations may exist in both Unity Editor and 3D panel)
4. Restore to `[0.0, 1.0, 0.0, 1.0]` (green) > confirm restoration

**If not passing**:
- If Parameters panel is empty:
  - Confirm the **Parameters** tab is selected in the Foxglove left panel (not the Topics tab)
  - Check Unity Console for `RegisterParameter` related logs
  - Check that the `FoxgloveDemoSetup` component is on the `Foxglove` GameObject and `_manager` reference is assigned
- If color does not change after modification:
  - Check Unity Console for `[ClientMsg]` logs (indicating parameter change messages arrived)
  - Check whether `/cube/color` is marked as writable (grayed out if read-only)

---

## 7. Parameters panel -- /cube/scale

**Expected behavior**:
- The `/cube/scale` parameter appears in the Parameters panel
- Type is `number` with initial value `1.0`
- Modifying the value changes the cube size in the 3D panel synchronously

**Interaction verification**:
1. Change `/cube/scale` to `2.0` > click **Set** > confirm cube becomes larger
2. Change to `0.5` > click **Set** > confirm cube becomes smaller
3. Change to `3.0` > click **Set** > confirm cube is noticeably larger

**If not passing**:
- Same troubleshooting steps as `/cube/color`
- Note: extremely small scale values (e.g., 0.01) may make the cube invisible

---

## 8. Service Call panel -- /cube/reset_pose

**Expected behavior**:
- The Service Call panel is correctly configured with `/cube/reset_pose`
- Request body is `{}` (empty object)
- Calling returns `{"status":"ok"}`
- Cube position resets to zero, rotation to identity, scale to 1.0, color to green

**Interaction verification**:
1. Drag the cube in Unity to a non-zero position
2. Change `/cube/color` to red and `/cube/scale` to 2.0
3. Click **Call service** in the Service Call panel
4. Confirm:
   - The cube in the 3D panel returns to origin
   - The cube restores green and original size
   - Plot panel curves jump to zero
   - Panel bottom shows response `{"status":"ok"}`
5. Drag the cube to a new position and modify parameters again; repeat the call to confirm it works every time

**If not passing**:
- If service name does not appear in the dropdown:
  - Manually enter `Service name` as `/cube/reset_pose` in the Service Call panel settings (gear icon)
  - Check that serverInfo capabilities include `"services"`
  - Check Unity Console for `RegisterService` related logs
- If the call has no response or times out:
  - Check timeout setting (default is long enough)
  - Check Unity Console for `FoxgloveDemoSetup` related errors
  - Confirm `FoxgloveDemoSetup._manager` reference is correctly assigned

---

## 9. Topic Graph panel -- connection graph

**Expected behavior**:
- The Topic Graph panel shows a node graph
- Nodes include all active topics: `/tf`, `/scene`, `/unity/camera`, `/debug/position`, `/debug/health`
- Publish-subscribe relationships between topics are clearly visible

**Interaction verification**:
1. Confirm all five topic categories are visible
2. Drag nodes to adjust layout and confirm connection relationships are unchanged
3. If a node is missing, check in Unity whether its corresponding publisher is running

**If not passing**:
- If a topic does not appear in the graph: confirm it exists and has data in the Topics list
- The Topic Graph is auto-generated by Foxglove; no extra configuration is needed

---

## 10. MCAP recording

**Expected behavior**:
- MCAP recording can be started to save data files locally via Foxglove
- File size > 0 after recording stops
- Recorded data can be replayed

**Steps**:
1. In Foxglove Desktop, click the record button (red dot) in the bottom status bar
2. Select save location and filename
3. Let Unity run for 10-30 seconds to collect enough data
4. Click the record button again to stop
5. Navigate to the save location in your file manager and confirm the `.mcap` file exists with size > 0 KB

**If not passing**:
- If file size is 0:
  - Confirm data was transmitting during recording (Topics list shows data rate)
  - Check that the save path has write permission
  - Check available disk space
- If the record button is gray and unclickable: confirm Foxglove is successfully connected

---

## 11. MCAP replay

**Expected behavior**:
- Previously recorded `.mcap` files can be loaded for offline playback
- Playback supports play, pause, and seek

**Steps**:
1. Disconnect the current WebSocket connection
2. In Foxglove, click **Open connection** > **Open local file...**
3. Select the previously recorded `.mcap` file
4. After Foxglove loads it, a timeline and playback controls should appear at the bottom
5. Click play and observe whether panel data replays in recording-time order
6. Test pause, seek, and other controls

**If not passing**:
- If the file cannot be loaded: confirm the `.mcap` file is not corrupted; try checking with `mcap doctor` (MCAP CLI tool)
- If playback shows nothing: check panel topic settings; some panels may need manual topic selection
- Note: playback shows historical data and cannot affect Unity in real time

---

## 12. FoxRun logging -- /debug/position and /debug/health

**Expected behavior**:
- `/debug/position` publishes the `Hub` GameObject's position at ~10 Hz
- `/debug/health` publishes a health value of `100.0` at ~5 Hz
- Both topics are visible in the Foxglove Topics list and Raw Messages panel

**Interaction verification**:
1. Confirm `/debug/position` and `/debug/health` exist in the Foxglove Topics list
2. Confirm both topics have data rates (not 0 bps)
3. View raw JSON for `/debug/position` in the Raw Messages panel; confirm correct structure
4. Move the `Hub` GameObject in Unity; confirm `/debug/position` data changes accordingly

**If not passing**:
- If `/debug/position` and `/debug/health` are completely absent:
  - Confirm the `Hub` GameObject has the `TestLog` component
  - In Unity Editor, `[FoxRun]` is dynamically generated by the Roslyn Source Generator; confirm no compilation errors
  - Check whether the `Hub` GameObject is active in the scene
- If data does not change:
  - Confirm Unity is in Play Mode
  - Check that `TestLog` component's `Update()` is executing normally

---

## 13. Acceptance results summary

| Item | Result | Notes |
|------|--------|-------|
| 3D panel /tf | [ ] Pass / [ ] Fail | |
| 3D panel /scene | [ ] Pass / [ ] Fail | |
| Camera/Image | [ ] Pass / [ ] Fail | |
| Plot panel | [ ] Pass / [ ] Fail | |
| Parameters /cube/color | [ ] Pass / [ ] Fail | |
| Parameters /cube/scale | [ ] Pass / [ ] Fail | |
| Services /cube/reset_pose | [ ] Pass / [ ] Fail | |
| Topic Graph | [ ] Pass / [ ] Fail | |
| MCAP recording | [ ] Pass / [ ] Fail | |
| MCAP replay | [ ] Pass / [ ] Fail | |
| FoxRun /debug/* | [ ] Pass / [ ] Fail | |

**Acceptance date**: _______
**Unity version**: _______
**Foxglove version**: _______
**Tester**: _______
