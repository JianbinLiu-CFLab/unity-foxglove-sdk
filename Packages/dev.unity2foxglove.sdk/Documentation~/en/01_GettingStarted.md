# 1. Getting Started

This guide helps new users go from installation to first visualization in under 5 minutes.

## 1.1 Purpose

Use this guide to connect your own Unity project to Foxglove for the first time.

## 1.2 Application

Start here when you want the shortest path from package installation to a visible `/tf` topic in Foxglove.

## 1.3 Install the package

### 1.3.1 Option A: Via manifest.json (recommended)

Add to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "dev.unity2foxglove.sdk": "file:../../Packages/dev.unity2foxglove.sdk"
  }
}
```

The path is relative to the project root. Adjust according to your actual repository location.

### 1.1.2 Option B: Unity Package Manager local path

1. Open Unity Editor
2. Window > Package Manager
3. Top-left `+` > **Add package from disk...**
4. Select `Packages/dev.unity2foxglove.sdk/package.json`

After installation, Unity automatically resolves the `com.unity.nuget.newtonsoft-json` dependency.

## 1.2 Create a basic scene

1. Create a new scene (or use an empty scene)
2. GameObject > Create Empty, name it `Foxglove`
3. In the Inspector, click **Add Component**, search for and add **FoxgloveManager**

FoxgloveManager's default configuration is ready to use:
- **Host**: `127.0.0.1`
- **Port**: `8765`
- **Start On Enable**: checked (server auto-starts on Play)
- **Run In Background**: checked (runs in background in the Editor)

## 1.3 Add a Transform publisher

1. Create a Cube in the scene: GameObject > 3D Object > Cube
2. Select the Cube, Add Component > search **FoxgloveTransformPublisher**
   - **Parent Frame Id**: `unity_world` (root coordinate frame)
   - **Child Frame Id**: leave empty to use the GameObject name (defaults to `Cube`)
   - **Publish Rate Hz**: `10` (10 times per second)
   - **Topic**: leave empty to default to `/tf`

## 1.4 Run and connect to Foxglove

1. Press Unity's **Play** button
2. The Console should show: `[Foxglove] Server started on ws://127.0.0.1:8765`
3. Open [Foxglove Desktop](https://foxglove.dev/download)
4. Click **Open connection** (or the connection icon on the left)
5. Select **Foxglove WebSocket**
6. Enter URL `ws://127.0.0.1:8765`, click **Open**

Once connected, the `/tf` topic appears in the Topics panel on the left.

## 1.5 View in the 3D panel

1. In Foxglove, click **+** to add a panel, select the **3D** panel
2. In 3D panel settings:
   - **Display frame**: select `unity_world`
   - Ensure the `/tf` topic is checked (selected by default)
3. Move the Cube in Unity and Foxglove's 3D view updates the Cube's position and rotation in real time

### 1.5.1 Expected result

- An axis marker appears in the 3D panel, tracking the Cube's Transform
- When you select and drag the Cube, the marker moves in real time
- The `/tf` topic shows a message rate of ~10 Hz in the Topics panel

## 1.6 Add more publishers (optional)

### 1.6.1 Scene cube publisher

Add **FoxgloveSceneCubePublisher** to the Cube:
- Select the `/scene` topic in the 3D panel to see a green cube
- Dynamically modify color and size via the Parameters panel

### 1.6.2 Camera publisher

Add **FoxgloveCameraPublisher** to the scene's Camera:
- Add an **Image** panel in Foxglove and select the `/unity/camera` topic
- Default: 640x480, JPEG quality 70, 10 Hz

## 1.7 Complete code snippet

A minimal test script created from scratch:

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;

public class QuickStart : MonoBehaviour
{
    private void Start()
    {
        // FoxgloveManager is already in the scene; no manual creation needed
        // FoxgloveTransformPublisher auto-publishes this GameObject's transform
        Debug.Log("Foxglove is streaming. Open Foxglove Desktop and connect to ws://127.0.0.1:8765");
    }
}
```

Attach this script to the Cube (on the same GameObject as FoxgloveTransformPublisher) and press Play.

## 1.8 Next steps

- [02_FoxgloveOperation.md](02_FoxgloveOperation.md) -- in-depth guide to Foxglove Desktop panels
- [05_FoxRun.md](05_FoxRun.md) -- zero-code auto-publishing with attributes
- [06_MCAP.md](06_MCAP.md) -- recording and replaying data
