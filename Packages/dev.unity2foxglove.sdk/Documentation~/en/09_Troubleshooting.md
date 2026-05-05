# 1. Troubleshooting

## 1.1 Purpose

Use this guide to diagnose common connection, topic, schema, build, and replay problems.

## 1.2 Application

Start here when Foxglove connects but shows no data, a Player build behaves differently from Editor Play Mode, or MCAP replay does not match the live scene.

## 1.3 Foxglove cannot connect

### 1.1.1 Check the port

Confirm that the **Port** field of `FoxgloveManager` is `8765` and the URL in Foxglove is `ws://127.0.0.1:8765`.

### 1.1.2 Check the firewall

Windows Firewall may block port access. When Unity starts the WebSocket server for the first time, the system should show a firewall prompt -- click "Allow access".

If no prompt appears, add it manually:
1. Control Panel > Windows Defender Firewall > Allow an app through Windows Firewall
2. Ensure Unity Editor or the built Player .exe is in the allowed list

### 1.1.3 0.0.0.0 vs 127.0.0.1

- `127.0.0.1` only listens for local connections (default)
- To connect from another device, set **Host** to `0.0.0.0` and connect Foxglove to that machine's LAN IP

Change the `_host` field in FoxgloveManager's Inspector, then use `ws://<IP address>:8765` in Foxglove.

### 1.1.4 Confirm Unity is in Play Mode

FoxgloveManager only starts the server when in Play Mode and `_startOnEnable = true`. The Console should show:
```
[Foxglove] Server started on ws://127.0.0.1:8765
```

If this log is absent, check that `FoxgloveManager` is on a scene GameObject and **Start On Enable** is checked.

## 1.2 Topics not visible

### 1.2.1 Topics panel is empty

Possible causes:
1. **Not in Play Mode** -- FoxgloveManager only starts during Play
2. **Connected before Play** -- If Foxglove connected before Play started, the session has not begun. Disconnect and reconnect.
3. **FoxgloveManager.Start On Enable unchecked** -- Check it manually or call `StartServer()` via code

Resolution: enter Play Mode, then disconnect and reconnect in Foxglove.

### 1.2.2 Specific topics not appearing

- `/tf`: confirm a GameObject in the scene has `FoxgloveTransformPublisher`
- `/scene`: confirm there is a `FoxgloveSceneCubePublisher`
- `/unity/camera`: confirm the Camera has `FoxgloveCameraPublisher`
- `/debug/*`: confirm a partial class script with `[FoxRun]` is attached

### 1.2.3 Publisher cannot find Manager

Publisher components automatically find the `FoxgloveManager` in the scene during `OnEnable`. If you see "No FoxgloveManager found in scene", ensure:
1. FoxgloveManager's GameObject is active in the scene
2. The Publisher and Manager are in the same scene

## 1.3 Schema not found error

Occurs when `PublishJson` uses an unregistered schema name.

### 1.3.1 Common causes

1. **Schema name typo** -- confirm casing, e.g., `foxglove.FrameTransform` (camelCase)
2. **Using a non-existent schema** -- only built-in schemas are available; see `DefaultSchemaRegistry` for the registered list
3. **[FoxRun] SchemaName typo** -- check that the SchemaName parameter uses a correct schema name

### 1.3.2 Correct usage

```csharp
// schemaless (no schema registration needed) -- always works
[FoxRun("/debug/test")]
private float _val;

// schema publishing -- 3D/Plot panels can render
[FoxRun("/debug/tf", SchemaName = "foxglove.FrameTransform")]
private Vector3 _pos;
```

## 1.4 [FoxRun] topics not publishing

### 1.4.1 Compile error FOXRUN001

The class is not declared `partial`. Add the `partial` keyword:

```csharp
// Wrong
public class MyLogger : MonoBehaviour { /* ... */ }

// Correct
public partial class MyLogger : MonoBehaviour { /* ... */ }
```

### 1.4.2 No topic in Play Mode

1. Script not attached to a GameObject -- drag the script onto a scene GameObject
2. Hub scan interval is 2 seconds -- wait 3-5 seconds and check again
3. The script's GameObject is inactive -- ensure `isActiveAndEnabled` is true

### 1.4.3 FOXRUN002 / FOXRUN003 warnings

- **FOXRUN002**: Multiple fields on the same topic have different `SchemaName` values, which may cause unexpected behavior. Unify SchemaName or split into different topics.
- **FOXRUN003**: Field name collision after underscore removal (e.g., `_pos` and `pos` both become `pos` in the published JSON). Rename one of the fields.

## 1.5 IL2CPP build failure

### 1.5.1 MissingMethodException / TypeLoadException

IL2CPP code stripping removed types used by reflection. Check:

1. **link.xml exists** -- confirm `Runtime/link.xml` content is correct and has not been accidentally overwritten
2. **Newtonsoft.Json is preserved** -- `<assembly fullname="Newtonsoft.Json" preserve="all" />`
3. **Unity.FoxgloveSDK is preserved** -- `<assembly fullname="Unity.FoxgloveSDK" preserve="all" />`

### 1.5.2 No [FoxrunBuildPreprocess] in build log

`FoxrunBuildPreprocess.OnPreprocessBuild` should execute in the early stages of the build. If no log output appears:

1. Confirm `FoxrunBuildPreprocess.cs` is in the `Editor/` directory
2. Confirm `callbackOrder = -100` (negative to ensure it runs before other handlers)
3. Check that the Editor assembly includes `FoxrunBuildPreprocess` and `FoxrunCodeGenerator`

### 1.5.3 Compression DLL platform error

DLLs under `Runtime/Plugins/compression/` need correct platform settings:

1. Select `IonKiwi.lz4.dll` and `ZstdSharp.dll` in Unity
2. Confirm in the Inspector that **Exclude Platforms** only includes **WebGL**
3. Confirm `.asmdef` platform exclusion settings are correct

## 1.6 MCAP file corruption

### 1.6.1 Cannot open .mcap file

MCAP files can only be read after `Close()` has been called correctly. `Close()` is responsible for writing:
- Footer record (containing Summary offset and CRC)
- Trailing magic bytes (file integrity marker)
- Flushing data to disk

Possible causes:
1. **Unity exited abnormally** -- Unity crashed or was force-quit during Play Mode
2. **Process killed directly** -- no wait for OnDestroy to be called
3. **Insufficient disk space** -- incomplete file write

### 1.6.2 Resolution

- Always stop Play Mode normally to end recording
- Ensure sufficient free disk space (at least several hundred MB)
- Do not manually delete or move .tmp files in the recording directory

## 1.7 Parameters not writable

### 1.7.1 Cause

The parameter was registered with `Writable` set to `false`, or `FoxgloveParameterComponent` had `Writable` unchecked.

### 1.7.2 Resolution

```csharp
// Programmatic registration
manager.RegisterParameter("/my_param", value, "number", writable: true);
```

Or check `Writable` in `FoxgloveParameterComponent`'s Inspector.

### 1.7.3 Parameters panel empty

1. Confirm `FoxgloveParameterComponent` is on the same GameObject as `FoxgloveManager`
2. Confirm the `Name` in the parameter list is not empty
3. Confirm FoxgloveManager has started

## 1.8 Service call timeout

### 1.8.1 Timeout mechanism

`FoxgloveServiceRegistry.DefaultTimeout` is 10 seconds. `SweepTimeouts` periodically checks timed-out service calls and auto-marks them as failed.

### 1.8.2 Causes

1. Service did not register a handler delegate
2. Service handling logic takes too long (e.g., involves IO or waiting)
3. `DrainServiceCalls()` was not called on the main thread

### 1.8.3 Resolution

```csharp
// Register a service with a handler
manager.Runtime.RegisterService(descriptor, request =>
{
    // Handler executes on the main thread
    return JToken.FromObject(new { status = "ok" });
});
```

## 1.9 Port already in use

### 1.9.1 Symptom

No `Server started` log on launch, or a port binding error appears.

### 1.9.2 Resolution

1. **Stop previous Unity instances** -- only one Unity process can bind port 8765 at a time
2. **Check port usage** -- run `netstat -ano | findstr 8765` (Windows) or `lsof -i :8765` (Linux/macOS) to see if another process is using it
3. **Change the port** -- modify FoxgloveManager's `Port` to a non-conflicting value
