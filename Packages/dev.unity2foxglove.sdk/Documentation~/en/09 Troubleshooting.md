# 9. Troubleshooting

## Who should read this

Read this when Unity, Foxglove, MCAP, FoxRun, or IL2CPP behavior does not match the guides.

## What you will do

You will diagnose problems by symptom instead of by internal module.

## 9.1 Foxglove Cannot Connect

Check:

1. Unity is in Play Mode.
2. `FoxgloveManager` is enabled.
3. **Start On Enable** is enabled or `StartServer()` was called.
4. Foxglove URL is `ws://127.0.0.1:8765`.
5. Another app is not already using port `8765`.

Try:

- Stop and restart Play Mode.
- Change the Manager port if another process owns `8765`.
- Reconnect Foxglove after Unity starts the server.

## 9.2 Connected but No Topics

Check:

1. Publisher components are enabled.
2. Their `Publish Rate Hz` is greater than `0`.
3. Topic fields are valid or left empty for default topics.
4. The Manager is running.
5. If replay is enabled, **Disable Live Publishers** may have turned off live publishers.

## 9.3 3D Panel Is Empty

Check:

- Display frame is `unity_world` for the default sample.
- `/tf` is visible in Topics.
- `/scene` is visible if you expect cube primitives.
- Coordinate mode matches your expectation.

## 9.4 Camera Image Is Blank

Check:

- `FoxgloveCameraPublisher` is on an active Camera.
- Width and height are greater than `0`.
- JPEG quality is not extremely low.
- The Camera renders something in Unity Game view.
- Async readbacks are not piling up because publish rate is too high.

## 9.5 Parameters Panel Is Empty

Check:

- You are running Full Demo or `Untiy2Foxglove`, not Basic sample.
- The demo setup script is enabled.
- Foxglove connected after Play Mode started.
- Reconnect the WebSocket connection.

## 9.6 Service Call Fails

If Foxglove says the service has not been advertised:

- Reconnect Foxglove.
- Verify the service is registered after the Manager starts.
- Use the Full Demo or `Untiy2Foxglove` for `/cube/reset_pose`.

If Foxglove shows JSON errors:

- Put `{}` in the request box.
- Do not put the service name in the JSON request.

If it times out:

- Check Unity Console for handler errors.
- Verify the GameObject that registers the service is enabled.

## 9.7 FoxRun Topics Missing in Player

Check the IL2CPP build log for:

```text
[FoxrunBuildPreprocess] Generating FoxRun source files...
```

If it is missing, the Player build did not run the FoxRun fallback generation step.

## 9.8 MCAP File Missing or Empty

Check:

- **Enable Recording** was enabled before Play Mode.
- At least one publisher was active.
- Unity stopped Play Mode cleanly so the file could close.
- The recording directory is writable.

## 9.9 Replay Looks Wrong

Check:

- Replay file path points to an existing `.mcap`.
- Coordinate mode matches the recording.
- **Disable Live Publishers** is enabled during replay tests.
- A replay adapter exists for the object type you expect to drive.

## 9.10 IL2CPP Build Fails

Check:

- Target platform module is installed in Unity Hub.
- Run from the repository root.
- Use `python Scripts/build_unity_il2cpp.py --target win64` for the standard Windows build.
- Open the log file printed by the script.

If JSON messages become `{}` only in Player, verify the project has linker preservation for Newtonsoft.Json and `Unity.FoxgloveSDK`.

## 9.11 Package Missing in Demo Project

The demo project expects the local package dependency to remain relative:

```json
"dev.unity2foxglove.sdk": "file:../../Packages/dev.unity2foxglove.sdk"
```

If Unity rewrote it to an absolute path, change it back before sharing or committing.
