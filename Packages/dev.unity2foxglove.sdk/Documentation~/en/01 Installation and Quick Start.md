# 1. Getting Started

## Who should read this

Read this if you are adding Unity2Foxglove to a Unity project for the first time.

## What you will do

You will install the package, add a `FoxgloveManager`, publish a Transform, optionally publish a Scene cube and Camera image, and connect Foxglove Desktop.

> [!TIP]
> If you have not installed Unity or Foxglove Desktop yet, start with [00 Prerequisites](00%20Prerequisites.md).

## 1.1 Install the Package

### 1.1.1 Install from a local package

1. Open Unity.
2. Open **Window > Package Manager**.

![Pasted image 20260506072040](../Pictures/01/Pasted%20image%2020260506072040.png)
<figcaption>Open Package Manager from the Unity Window menu.</figcaption>

3. Click **+ > Add package from disk...**, or select "Install package from git URL" below and enter the URL:  

![Pasted image 20260506074137](../Pictures/01/Pasted%20image%2020260506074137.png)
<figcaption>Choose a package installation method from the Package Manager add menu.</figcaption>

4. Select `Packages/dev.unity2foxglove.sdk/package.json`.

![Pasted image 20260506074335](../Pictures/01/Pasted%20image%2020260506074335.png)
<figcaption>Select the Unity2Foxglove package.json file.</figcaption>

5. Wait for Unity to resolve dependencies.

![Pasted image 20260506074411](../Pictures/01/Pasted%20image%2020260506074411.png)
<figcaption>Confirm that Unity imports the Unity2Foxglove package.</figcaption>

### 1.1.2 Alternative: edit `manifest.json`

This method is not recommended for general use - Unity may rewrite relative `file:` paths to absolute paths, and the result is not portable. Prefer the Package Manager UI above unless you have a specific reason to edit the manifest.

```json
{
  "dependencies": {
    "dev.unity2foxglove.sdk": "file:../../Packages/dev.unity2foxglove.sdk"
  }
}
```

Adjust the path to match where your project sits relative to the package.

## 1.2 Add the Server Component

1. Create an empty GameObject named `Foxglove`.
2. Add **FoxgloveManager**.

![Pasted image 20260506081846](../Pictures/01/Pasted%20image%2020260506081846.png)
<figcaption>Add the Foxglove Manager component to an empty GameObject.</figcaption>

3. Keep the default settings for the first test:
   - Host: `127.0.0.1`
   - Port: `8765`
   - Start On Enable: enabled
   - Coordinate Mode: `LeftHand`

![Pasted image 20260506082007](../Pictures/01/Pasted%20image%2020260506082007.png)
<figcaption>Use the default Foxglove Manager settings for the first connection test.</figcaption>

Press **Play**. The Unity Console should show a server started message for `ws://127.0.0.1:8765`.

![Pasted image 20260506082224](../Pictures/01/Pasted%20image%2020260506082224.png)
<figcaption>Start Play Mode and verify that the WebSocket server starts.</figcaption>

## 1.3 Publish a Transform

1. Create a Cube.
2. Add **Foxglove Transform Publisher** to the Cube.

![Pasted image 20260506084745](../Pictures/01/Pasted%20image%2020260506084745.png)
<figcaption>Add the Foxglove Transform Publisher component to the Cube.</figcaption>

3. Use these first-test values:
   - Topic: leave empty or use `/tf`
   - Parent Frame Id: `unity_world`
   - Child Frame Id: leave empty to use the GameObject name
   - Publish Rate Hz: `10`

![Pasted image 20260506084524](../Pictures/01/Pasted%20image%2020260506084524.png)
<figcaption>Configure the Transform publisher for the default `/tf` topic.</figcaption>

Move or rotate the Cube while Unity is in Play Mode.

## 1.4 Connect Foxglove Desktop

1. Open Foxglove Desktop.
2. Click **Open connection**.
3. Choose **Foxglove WebSocket**.
4. Enter `ws://127.0.0.1:8765`.
5. Click **Open**.

Confirm that `/tf` appears in the Topics panel. To inspect the raw transform message, add a **Raw Messages** panel and select `/tf`. To visualize the frame, add a **3D** panel.

![Pasted image 20260506082635](../Pictures/01/Pasted%20image%2020260506082635.png)
<figcaption>Inspect the connected `/tf` topic in Foxglove.</figcaption>

Expected topics:

- `/tf` with schema `foxglove.FrameTransform`

![Pasted image 20260506082758](../Pictures/01/Pasted%20image%2020260506082758.png)
<figcaption>Verify that `/tf` is published as a Foxglove FrameTransform topic.</figcaption>


Move or rotate the Cube in the Scene view using the Move Tool or Rotate Tool while Unity is in Play Mode. The Transform changes are published to `/tf` immediately.

![Pasted image 20260506082831](../Pictures/01/Pasted%20image%2020260506082831.png)
<figcaption>Move or rotate the Cube and watch the transform update in Foxglove.</figcaption>

## 1.5 Optional: Publish a Scene Cube

Add **Foxglove Scene Cube Publisher** to the Cube if you want Foxglove's 3D panel to show a simple cube primitive rather than only a frame transform.

![Pasted image 20260506085003](../Pictures/01/Pasted%20image%2020260506085003.png)
<figcaption>Add the Foxglove Scene Cube Publisher component.</figcaption>

Recommended first-test values:

- Topic: `/scene`
- Frame Id: leave empty to reuse the object/frame name
- Color: green
- Size: `(1, 1, 1)`

![Pasted image 20260506085036](../Pictures/01/Pasted%20image%2020260506085036.png)
<figcaption>Configure the scene cube publisher for the `/scene` topic.</figcaption>

Go back to Foxglove and add a new panel.

![Pasted image 20260506083151](../Pictures/01/Pasted%20image%2020260506083151.png)
<figcaption>Add another panel in Foxglove.</figcaption>

Select the 3D panel.

![Pasted image 20260506083229](../Pictures/01/Pasted%20image%2020260506083229.png)
<figcaption>Select a 3D panel to view scene primitives.</figcaption>

Expected topics:

- `/scene` with schema `foxglove.SceneUpdate`

If you don't see the cube in the 3D panel, find `/scene` in the Topics list on the left Panel and click the visibility icon to enable it.

![Pasted image 20260506083402](../Pictures/01/Pasted%20image%2020260506083402.png)
<figcaption>Enable the `/scene` topic in the 3D panel if the cube is hidden.</figcaption>
## 1.6 Optional: Publish a Camera Image

1. Select a Unity Camera.
2. Add **Foxglove Camera Publisher**.

![Pasted image 20260506085202](../Pictures/01/Pasted%20image%2020260506085202.png)
<figcaption>Add the Foxglove Camera Publisher component to a Unity Camera.</figcaption>

3. Use:
   - Topic: `/unity/camera`
   - Frame Id: `unity_camera`
   - Publish Rate Hz: `10`
   - Width: `640`
   - Height: `480`
   - JPEG Quality: `70`

![Pasted image 20260506085229](../Pictures/01/Pasted%20image%2020260506085229.png)
<figcaption>Configure the camera publisher for `/unity/camera`.</figcaption>

Go back to Foxglove and add a new panel.

![Pasted image 20260506083815](../Pictures/01/Pasted%20image%2020260506083815.png)
<figcaption>Add another panel for camera visualization.</figcaption>

Select the Image panel.

![Pasted image 20260506083935](../Pictures/01/Pasted%20image%2020260506083935.png)
<figcaption>Select an Image panel to view `/unity/camera`.</figcaption>

Expected topics:

- `/tf` with schema `foxglove.FrameTransform`
- `/scene` with schema `foxglove.SceneUpdate` if you added a scene publisher
- `/unity/camera` with schema `foxglove.CompressedImage` if you added a camera publisher

Move or rotate the Cube in the Scene view using the Move Tool or Rotate Tool while Unity is in Play Mode. Watch the cube move in Foxglove's 3D panel and see the position update in the panels.

![Pasted image 20260506085332](../Pictures/01/Pasted%20image%2020260506085332.png)
<figcaption>Verify live transform, scene, and camera updates in Foxglove.</figcaption>

## 1.7 What Success Looks Like

- The Foxglove **Topics** panel lists your Unity topics.
- The **3D** panel can display the Cube frame or primitive.
- The **Image** panel can display `/unity/camera`.
- Moving the Cube in Unity updates Foxglove live.

## 1.8 Next Steps

- Use [02 Foxglove Desktop Operation](02%20Foxglove%20Desktop%20Operation.md) to set up panels and layouts.
- Use [03 Verifying Basic Visualization](03%20Verifying%20Basic%20Visualization.md) if you want a packaged minimal scene.
- Use [10 Inspector Reference](10%20Inspector%20Reference.md) when you need to tune component fields.
