## 1. Purpose

Use this page for the first package installation and the first live Foxglove connection from a Unity scene.

## 2. Workflow

You will install the package, add a `FoxgloveManager`, publish a Transform, optionally publish a Scene cube and Camera image, and connect Foxglove Desktop.

> [!TIP]
> If you have not installed Unity or Foxglove Desktop yet, start with [01_Prerequisites](01_Prerequisites.md).

## 3. Install the Package

### 3.1 Install from the Git URL

1. Open Unity.
2. Open **Window > Package Manager**.

![open-package-manager](../Pictures/01/open-package-manager.png)
<figcaption>Figure 1: Open Package Manager</figcaption>

3. Click **+ > Install package from git URL...** and enter:

```http
https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk.git?path=/Packages/dev.unity2foxglove.sdk
```

![package-manager-add-menu](../Pictures/01/package-manager-add-menu.png)
<figcaption>Figure 2: Package Manager Add Menu</figcaption>

4. Wait for Unity to resolve dependencies.

![package-imported-confirmation](../Pictures/01/package-imported-confirmation.png)
<figcaption>Figure 3: Package Import Confirmation</figcaption>

### 3.2 Install from a local checkout

Use this path if you already cloned the repository.

1. Open Unity.
2. Open **Window > Package Manager**.
3. Choose **+ > Add package from disk...**.
4. Select `Packages/dev.unity2foxglove.sdk/package.json`.

![select-package-json](../Pictures/01/select-package-json.png)
<figcaption>Figure 4: Select package.json File</figcaption>

5. Wait for Unity to resolve dependencies.

![package-imported-confirmation](../Pictures/01/package-imported-confirmation.png)
<figcaption>Figure 5: Package Import Confirmation</figcaption>

### 3.3 Alternative: edit `manifest.json`

This method is not recommended for general use - Unity may rewrite relative `file:` paths to absolute paths, and the result is not portable. Prefer the Package Manager UI above unless you have a specific reason to edit the manifest.

```json
{
  "dependencies": {
    "dev.unity2foxglove.sdk": "file:../../Packages/dev.unity2foxglove.sdk"
  }
}
```

Adjust the path to match where your project sits relative to the package.

## 4. Add the Server Component

1. Create an empty GameObject named `Foxglove`.
2. Add **FoxgloveManager**.

![add-foxglove-manager](../Pictures/01/add-foxglove-manager.png)
<figcaption>Figure 6: Add Foxglove Manager Component</figcaption>

3. Keep the default settings for the first test:
   - Host: `127.0.0.1`
   - Port: `8765`
   - Start On Enable: enabled
   - Coordinate Mode: `LeftHand`

![foxglove-manager-default-settings](../Pictures/01/foxglove-manager-default-settings.png)
<figcaption>Figure 7: Foxglove Manager Default Settings</figcaption>

Press **Play**. The Unity Console should show a server started message for `ws://127.0.0.1:8765`.

![websocket-server-started](../Pictures/01/websocket-server-started.png)
<figcaption>Figure 8: WebSocket Server Start Confirmation</figcaption>

## 5. Publish a Transform

1. Create a Cube.
2. Add **Foxglove Transform Publisher** to the Cube.

![add-transform-publisher](../Pictures/01/add-transform-publisher.png)
<figcaption>Figure 9: Add Transform Publisher Component</figcaption>

3. Use these first-test values:
   - Topic: leave empty or use `/tf`
   - Parent Frame Id: `unity_world`
   - Child Frame Id: leave empty to use the GameObject name
   - Publish Rate Hz: `10`

![transform-publisher-tf-config](../Pictures/01/transform-publisher-tf-config.png)
<figcaption>Figure 10: Transform Publisher `/tf` Configuration</figcaption>

Move or rotate the Cube while Unity is in Play Mode.

## 6. Connect Foxglove Desktop

1. Open Foxglove Desktop.
2. Click **Open connection**.
3. Choose **Foxglove WebSocket**.
4. Enter `ws://127.0.0.1:8765`.
5. Click **Open**.

Confirm that `/tf` appears in the Topics panel. To inspect the raw transform message, add a **Raw Messages** panel and select `/tf`. To visualize the frame, add a **3D** panel.

![foxglove-tf-topic-connected](../Pictures/01/foxglove-tf-topic-connected.png)
<figcaption>Figure 11: `/tf` Topic Connected in Foxglove</figcaption>

Core expected topic:

- `/tf` with schema `foxglove.FrameTransform`

![tf-frametransform-topic](../Pictures/01/tf-frametransform-topic.png)
<figcaption>Figure 12: `/tf` FrameTransform Topic Verification</figcaption>


Move or rotate the Cube in the Scene view using the Move Tool or Rotate Tool while Unity is in Play Mode. The Transform changes are published to `/tf` immediately.

![cube-transform-update-foxglove](../Pictures/01/cube-transform-update-foxglove.png)
<figcaption>Figure 13: Cube Transform Live Update</figcaption>

## 7. Optional: Publish a Scene Cube

Add **Foxglove Scene Cube Publisher** to the Cube if you want Foxglove's 3D panel to show a simple cube primitive rather than only a frame transform.

![add-scene-cube-publisher](../Pictures/01/add-scene-cube-publisher.png)
<figcaption>Figure 14: Add Scene Cube Publisher Component</figcaption>

Recommended first-test values:

- Topic: `/scene`
- Frame Id: leave empty to reuse the object/frame name
- Color: green
- Size: `(1, 1, 1)`

![scene-cube-publisher-config](../Pictures/01/scene-cube-publisher-config.png)
<figcaption>Figure 15: Scene Cube Publisher `/scene` Configuration</figcaption>

Go back to Foxglove and add a new panel.

![foxglove-add-panel](../Pictures/01/foxglove-add-panel.png)
<figcaption>Figure 16: Add Foxglove Panel</figcaption>

Select the 3D panel.

![select-3d-panel](../Pictures/01/select-3d-panel.png)
<figcaption>Figure 17: Select 3D Panel</figcaption>

Optional expected topic after adding the Scene Cube publisher:

- `/scene` with schema `foxglove.SceneUpdate`

If you don't see the cube in the 3D panel, find `/scene` in the Topics list on the left Panel and click the visibility icon to enable it.

![enable-scene-topic-3d](../Pictures/01/enable-scene-topic-3d.png)
<figcaption>Figure 18: Enable `/scene` Topic in 3D Panel</figcaption>

## 8. Optional: Publish a Camera Image

1. Select a Unity Camera.
2. Add **Foxglove Camera Publisher**.

![add-camera-publisher](../Pictures/01/add-camera-publisher.png)
<figcaption>Figure 19: Add Camera Publisher Component</figcaption>

3. Use:
   - Topic: `/unity/camera`
   - Frame Id: `unity_camera`
   - Publish Rate Hz: `10`
   - Width: `640`
   - Height: `480`
   - JPEG Quality: `70`

![camera-publisher-config](../Pictures/01/camera-publisher-config.png)
<figcaption>Figure 20: Camera Publisher `/unity/camera` Configuration</figcaption>

Go back to Foxglove and add a new panel.

![foxglove-add-camera-panel](../Pictures/01/foxglove-add-camera-panel.png)
<figcaption>Figure 21: Add Camera Visualization Panel</figcaption>

Select the Image panel.

![select-image-panel](../Pictures/01/select-image-panel.png)
<figcaption>Figure 22: Select Image Panel</figcaption>

Expected topics at this point:

- Core: `/tf` with schema `foxglove.FrameTransform`
- Optional scene publisher: `/scene` with schema `foxglove.SceneUpdate`
- Optional camera publisher: `/unity/camera` with schema `foxglove.CompressedImage`

Move or rotate the Cube in the Scene view using the Move Tool or Rotate Tool while Unity is in Play Mode. Watch the cube move in Foxglove's 3D panel and see the position update in the panels.

![foxglove-live-updates](../Pictures/01/foxglove-live-updates.png)
<figcaption>Figure 23: Foxglove Live Update Verification</figcaption>

## 9. Expected Result

- The Foxglove **Topics** panel lists your Unity topics.
- The **3D** panel can display the Cube frame or primitive.
- The **Image** panel can display `/unity/camera`.
- Moving the Cube in Unity updates Foxglove live.

## 10. Next Steps

- Use [04_Foxglove_Desktop_Operation](04_Foxglove_Desktop_Operation.md) to set up panels and layouts.
- Use [05_Verifying_Basic_Visualization](05_Verifying_Basic_Visualization.md) if you want a packaged minimal scene.
- Use [12_Inspector_Reference](12_Inspector_Reference.md) when you need to tune component fields.
