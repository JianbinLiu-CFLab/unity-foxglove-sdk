# 1. Demo Troubleshooting

This document summarizes common issues you may encounter with the demo project and their solutions.

## 1.1 Purpose

Use this guide to diagnose demo-project-specific failures.

## 1.2 Application

Start here when Unity cannot open the demo, Foxglove cannot connect, `/debug/*` topics are missing, the camera is black, or service/parameter panels do not behave as expected.

## 1. Unity cannot open the project

**Symptom**: After clicking the project in Unity Hub, it does not respond for a long time or reports "Project cannot be opened".

**Troubleshooting steps**:

1. **Confirm Unity version**: the project requires Unity 2022.3 or later. In Unity Hub > Installs, check the installed Unity editor version. If the version is too low, install 2022.3 LTS or newer.

2. **Rebuild the Library directory**:
   - Close Unity Hub
   - Delete the `Untiy2Foxglove/Library/` directory (keep other files and directories)
   - Re-open the project with Unity Hub
   - Unity automatically re-imports all assets and compiles scripts (may take several minutes the first time)

3. **Check disk space**: the Library directory and compilation caches can consume significant disk space. Ensure sufficient free space.

4. **Kill processes**: if you are working on another Unity project simultaneously, Library conflicts may occur. Close all Unity instances and retry.

## 2. Compilation errors

**Symptom**: red compilation errors appear in the Console after opening the project.

**Troubleshooting steps**:

1. **Check Package linkage**:
   - Open `Window > Package Manager`
   - Confirm `dev.unity2foxglove.sdk` appears in the package list (usually under "In Project" or "Custom")
   - If the package does not exist:
     - Check that `Packages/manifest.json` contains a reference to `dev.unity2foxglove.sdk`
     - Confirm the `Packages/dev.unity2foxglove.sdk/` directory exists and contains `package.json`

2. **Check for package version conflicts**:
   - The SDK depends on Newtonsoft.Json. Confirm `Packages/manifest.json` includes `com.unity.nuget.newtonsoft-json`
   - Confirm no other package with the same name but a different version is conflicting

3. **Reimport**:
   - Right-click `Assets` in the Project window > **Reimport All** (takes a long time; use as a last resort)

4. **Check .NET compatibility**:
   - `Edit > Project Settings > Player > Other Settings > Configuration > Api Compatibility Level` should be `.NET Standard 2.1` or `.NET Framework`
   - `Scripting Backend` should be `Mono` in the Editor (IL2CPP is only used during builds)

## 3. Port 8765 is occupied

**Symptom**: after entering Play Mode, the Unity Console reports a port binding error, or Foxglove cannot connect.

**Troubleshooting steps**:

1. **Check port occupancy**:
   ```powershell
   netstat -ano | findstr 8765
   ```
   If the output contains entries in `LISTENING` state, the port is occupied.

2. **Close other Unity instances**: if you are running multiple Unity Editors or previously ran this project, a previous instance may still hold the port. Close all Unity processes and retry.

3. **End the occupying process**: find the PID occupying port 8765 and end it via Task Manager.

4. **Change the port** (alternative):
   - Select the `Foxglove` GameObject in Unity's Hierarchy
   - Find the `FoxgloveManager` component in the Inspector
   - Modify the `Port` field (default 8765)
   - Use the new port when connecting Foxglove

## 4. Foxglove cannot connect

**Symptom**: in Foxglove Desktop, after clicking "Open", the connection fails (red error or perpetual spinner).

**Troubleshooting steps**:

1. **Confirm IP address**:
   - The connection URL should be `ws://127.0.0.1:8765` (local loopback address)
   - Do not use `localhost` (may resolve abnormally under certain network configurations)
   - Do not use `ws://0.0.0.0:8765` (server bind address; clients cannot connect to this directly)

2. **Confirm Foxglove and Unity are on the same machine**:
   - The demo binds `127.0.0.1` by default, meaning only the local machine can connect
   - To access from another machine, modify the FoxgloveManager bind address

3. **Firewall check**:
   - Check whether Windows Defender Firewall is blocking the connection
   - Temporarily disable the firewall for testing (**for testing only; do not leave it off permanently**)

4. **Confirm Unity is in Play Mode**:
   - The Unity Editor must be in Play Mode for the WebSocket server to start
   - In Edit Mode (Play not pressed), FoxgloveManager is not initialized and cannot accept connections

5. **Check Console logs**:
   - Open Unity Console (`Window > General > Console`)
   - Server startup log is printed when entering Play Mode
   - Confirm there are no WebSocket-related red errors

## 5. No /debug/ topics (FoxRun log topics absent)

**Symptom**: `/debug/position` or `/debug/health` are not visible in the Foxglove Topics list.

**Troubleshooting steps**:

1. **Confirm the Hub GameObject exists**:
   - Find the GameObject named `Hub` in Unity's Hierarchy
   - Confirm `Hub` has the `TestLog` component (a partial class with `[FoxRun]` attributes)

2. **Confirm FoxRun Source Generator is working**:
   - In Unity Editor, `[FoxRun]` attributes are processed by the Roslyn Source Generator, which dynamically generates IL code
   - Confirm the project compiles without errors (no red errors in the Console)
   - Check that `Assets/Scripts/Generated/TestLog_FoxRun.g.cs` exists (as a physical fallback for IL2CPP builds)

3. **Confirm FoxgloveManager configuration**:
   - The `FoxgloveManager` component should have an `_isgRegistry` field (ISG Registry reference)
   - If this field is empty, the Source Generator cannot auto-register

4. **Rebuild Generated files**:
   - Delete `.cs` files under `Assets/Scripts/Generated/`
   - Trigger script recompilation in the Unity Editor (`Assets > Reimport` or `Edit > Preferences > External Tools > Regenerate project files`)
   - Re-enter Play Mode

## 6. Camera shows a black screen

**Symptom**: the Foxglove Image panel shows a completely black image for `/unity/camera`.

**Troubleshooting steps**:

1. **Confirm camera target**:
   - Select `Main Camera` in Unity's Hierarchy
   - Confirm the `FoxgloveCameraPublisher`'s `_camera` field in the Inspector references `Main Camera`
   - Confirm `Target Texture` is None (using the camera's default render target)

2. **Confirm rendering is working**:
   - Check that the Unity Editor Game view displays normally (if the Game view is also black, it is a rendering issue, not an SDK issue)
   - Confirm the scene has at least basic lighting and visible objects

3. **Confirm the Game view is visible**:
   - The Unity Editor's Game view must be visible (not minimized or in a hidden tab)
   - Keep the Game view in front or drag it to a standalone floating window

4. **Check compressed image quality**:
   - FoxgloveCameraPublisher sends images in JPEG compressed format
   - Lowering Quality may make the image blurry but should not cause a black screen

## 7. Parameters panel is empty

**Symptom**: switching to the Parameters tab on the left side of Foxglove shows no parameters.

**Troubleshooting steps**:

1. **Confirm you selected the Parameters panel, not the Topics panel**: Foxglove has two tabs on the left -- **Topics** and **Parameters**. Ensure you have switched to the Parameters tab.

2. **Confirm FoxgloveDemoSetup is working**:
   - Select the `Foxglove` GameObject in Unity's Hierarchy
   - Confirm the `FoxgloveDemoSetup` component exists and is enabled
   - Confirm the `_manager` field references the `FoxgloveManager` component on the same GameObject

3. **Check capabilities**:
   - Confirm FoxgloveManager's serverInfo.capabilities includes `"parameters"`
   - Parameter registration happens in `FoxgloveDemoSetup.Start()`; confirm corresponding logs in the Console

4. **Disconnect and reconnect**:
   - Disconnect in Foxglove
   - Exit Play Mode in Unity and re-enter
   - Reconnect Foxglove

## 8. Services /cube/reset_pose not found

**Symptom**: the `/cube/reset_pose` service is not found in the Foxglove Service Call panel.

**Troubleshooting steps**:

1. **Manually enter the service name**:
   - In the Service Call panel, click the gear icon (Settings) in the top-right
   - Manually enter `/cube/reset_pose` in the **Service name** field
   - Close settings; the panel should display the service

2. **Confirm the service is registered**:
   - Same as Parameters panel troubleshooting; check the `FoxgloveDemoSetup` component
   - Check that serverInfo.capabilities includes `"services"`

3. **Check Console logs**:
   - `FoxgloveDemoSetup.Start()` calls `rt.RegisterService()`
   - If this call fails, the Console should show error details

## 9. MCAP recording file is empty

**Symptom**: after recording, the `.mcap` file size is 0 or very small.

**Troubleshooting steps**:

1. **Confirm data was transmitting during recording**:
   - Before recording, confirm topics in the Foxglove Topics list have data rates (bps > 0)
   - If all topics have a data rate of 0, no data is being transmitted and recording is naturally empty

2. **Check output directory permissions**:
   - The MCAP file save location must have write permission
   - Try saving to the Desktop or Documents directory (usually always writable)

3. **Confirm `_enableRecording` is on** (if FoxgloveManager has relevant configuration):
   - Check the FoxgloveManager component's recording-related settings

4. **Try recording for a longer time**:
   - Record for at least 10 seconds to ensure enough data

## 10. Layout import fails

**Symptom**: Foxglove cannot import the `FoxgloveLayout.json` file.

**Troubleshooting steps**:

1. **Confirm Foxglove version**:
   - Foxglove Layout format may vary slightly between versions
   - Ensure you are using a newer version of Foxglove Desktop (2.0 or above)

2. **Confirm correct file path**:
   - The file is at `Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/FoxgloveLayout.json`
   - The path is relative to the repository root (not the `Untiy2Foxglove/` project directory)

3. **Use paste import**:
   - Open the `.json` file in a text editor (e.g., VS Code, Notepad)
   - Select all and copy
   - In Foxglove: `Layout > Import > Paste layout JSON`
   - Paste into the dialog and confirm

4. **Check JSON format**:
   - If the file was accidentally modified, the JSON may be corrupted
   - Verify the file format with an online JSON validator

## 11. IL2CPP build issues

For IL2CPP build-specific issues, refer to the troubleshooting section in **[03_BuildIL2CPP.md](03_BuildIL2CPP.md)**.

## 12. Still unresolved

If none of the above steps resolve the issue, collect the following information:

- Unity version (`Help > About Unity`)
- Foxglove Desktop version (`Help > About`)
- Operating system version
- Complete error logs from the Unity Console (select all red errors, right-click > `Copy All Compendium`)
- `.mcap` recording file (if there are recording issues)

Then consult the SDK documentation (`Packages/dev.unity2foxglove.sdk/Documentation~/`) or file an issue.
