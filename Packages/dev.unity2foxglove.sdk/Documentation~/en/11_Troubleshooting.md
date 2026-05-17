## 1. Purpose

Use this page when Unity, Foxglove, MCAP, FoxRun, or IL2CPP behavior does not match the expected guide results.

## 2. Workflow

You will diagnose problems by symptom instead of by internal module.

## 3. Foxglove Cannot Connect

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

## 4. Connected but No Topics

Check:

1. Publisher components are enabled.
2. Their `Publish Rate Hz` is greater than `0`.
3. Topic fields are valid or left empty for default topics.
4. The Manager is running.
5. If replay is enabled, **Disable Live Publishers** may have turned off live publishers.

## 5. 3D Panel Is Empty

Check:

- Display frame is `unity_world` for the default sample.
- `/tf` is visible in Topics.
- `/scene` is visible if you expect cube primitives.
- Coordinate mode matches your expectation.

For point clouds:

- Raw `Point Cloud Output Mode` publishes `foxglove.PointCloud` on `/unity/point_cloud`.
- Draco `Point Cloud Output Mode` publishes protobuf-only `foxglove.CompressedPointCloud` on `/unity/point_cloud_draco` with format = `draco`.
- If you switched output modes while Foxglove was already connected, reconnect so the 3D panel sees the updated topic and schema.
- If Draco mode is selected, click `Check Draco` in the Inspector and fix any native plugin warning before expecting points to appear.

Draco mode uses the bundled Windows native plugin `Unity2FoxgloveDracoNative.dll`. Missing or incompatible native plugin binaries mean Draco mode logs a warning and publishes nothing. Switch back to raw mode for dependency-free or unsupported-platform point clouds.

Phase 89 uses a synchronous native Draco encode path. Large frames can block publish/update work while they encode; lower point budgets or return to raw mode while diagnosing.

For custom ROS 2 `.msg` channels:

- ROS 2 schema channels use `schemaEncoding = ros2msg` and message encoding `cdr`.
- The SDK registers the official Foxglove ROS 2 `.msg` schema catalog and can advertise or record those channel/schema records.
- The SDK does not currently serialize CDR payload bytes for user messages. If a `ros2msg` topic advertises correctly but Foxglove cannot decode data, verify that the published payload is valid ROS 2 CDR for that exact schema.
- If you need dependency-free working payloads today, use the existing JSON or protobuf publisher paths.

## 6. Camera Image Is Blank

Check:

- `FoxgloveCameraPublisher` is on an active Camera.
- `Camera Output Mode` matches the Foxglove panel topic and schema you are viewing.
- Width and height are greater than `0`.
- JPEG quality is not extremely low.
- The Camera renders something in Unity Game view.
- Async readbacks are not piling up because publish rate is too high.

For JPEG mode, use an Image panel on `/unity/camera`.

For H.264/H.265 FFmpeg mode:

1. Set `Camera Output Mode` to `H.264 (FFmpeg)` or `H.265 / HEVC (FFmpeg)`.
2. Leave `FFmpeg Path` empty to resolve `ffmpeg` from process, user, or machine `PATH`, or click `...` and browse to the exact executable. You may also enter a folder that directly contains `ffmpeg.exe`.
3. Click `Check FFmpeg`; the SDK checks only configured paths and `PATH` entries, and does not scan common folders or modify `PATH`. After a successful check, `Reveal Folder` opens the resolved executable folder.
4. Use `FFmpeg Help...` for manual setup notes and license guidance.
5. Use an Image panel on `/unity/camera` unless you intentionally changed the topic. If you switched modes while Foxglove was already connected, reconnect so the panel sees the updated schema.

For Asset Store and commercial distribution safety, the SDK does not bundle, download, install, or modify `PATH` for FFmpeg. H.264/H.265 modes use only the executable configured in `FFmpeg Path`, or the system `PATH` when that field is empty. Many FFmpeg builds that support H.264/H.265 use GPL components such as `libx264`/`libx265`; confirm the chosen build's license is appropriate for your project before using it.

If FFmpeg is missing or invalid, FFmpeg video mode publishes nothing and does not silently fall back to JPEG.

For H.264 OpenH264 mode:

1. Set `Camera Output Mode` to `H.264 (OpenH264)`.
2. Use `...` next to `OpenH264 Helper` to choose the helper executable.
3. Use `...` next to `OpenH264 DLL`, or click `Install OpenH264 Runtime...` to download the pinned Cisco DLL, build the local helper executable, and fill both fields.
4. Click `Check OpenH264`; the check validates both paths and performs a tiny encode smoke test.
5. Use an Image panel on `/unity/camera` unless you intentionally changed the topic. If you switched modes while Foxglove was already connected, reconnect so the panel sees the updated schema.

The SDK does not bundle OpenH264 binaries. `Install OpenH264 Runtime...` does not modify `PATH` or write into the Unity project. It installs into a per-user cache and updates only the selected camera component's helper/DLL paths. If video mode is missing or invalid, switch back to JPEG mode for dependency-free camera output.

For H.264 Windows Native mode:

1. Set `Camera Output Mode` to `H.264 (Windows Native, Experimental)`.
2. Use an Image panel on `/unity/camera` unless you intentionally changed the topic.
3. Reconnect Foxglove after switching modes so the panel sees the updated `foxglove.CompressedVideo` schema.
4. If Unity logs that the native encoder is unavailable, use `H.264 (OpenH264)` or `JPEG` instead.

Windows Native mode uses Windows Media Foundation and does not use FFmpeg, OpenH264, or external binaries. It is Windows-only and experimental because encoder availability, driver behavior, and low-latency support vary by machine.

## 7. Parameters Panel Is Empty

Check:

- You are running Full Demo or `Unity2Foxglove`, not Basic sample.
- The demo setup script is enabled.
- Foxglove connected after Play Mode started.
- Reconnect the WebSocket connection.

## 8. Service Call Fails

If Foxglove says the service has not been advertised:

- Reconnect Foxglove.
- Verify the service is registered after the Manager starts.
- Use the Full Demo or `Unity2Foxglove` for `/cube/reset_pose`.

If Foxglove shows JSON errors:

- Put `{}` in the request box.
- Do not put the service name in the JSON request.

If it times out:

- Check Unity Console for handler errors.
- Verify the GameObject that registers the service is enabled.

## 9. FoxRun Topics Missing in Player

Check the IL2CPP build log for:

```text
[FoxrunBuildPreprocess] Generating FoxRun source files...
```

If it is missing, the Player build did not run the FoxRun fallback generation step.

## 10. MCAP File Missing or Empty

Check:

- **Enable Recording** was enabled before Play Mode.
- At least one publisher was active.
- Unity stopped Play Mode cleanly so the file could close.
- The recording directory is writable.

## 11. Replay Looks Wrong

Check:

- Replay file path points to an existing `.mcap`.
- Coordinate mode matches the recording.
- **Disable Live Publishers** is enabled during replay tests.
- A replay adapter exists for the object type you expect to drive.

## 12. IL2CPP Build Fails

Check:

- Target platform module is installed in Unity Hub.
- Run from the repository root.
- Use `python Scripts/build_tools/unity_il2cpp.py --target win64` for the standard Windows build.
- Open the log file printed by the script.

If JSON messages become `{}` only in Player, verify the project has linker preservation for Newtonsoft.Json and `Unity.FoxgloveSDK`.

## 13. Package Missing in Demo Project

The demo project expects the local package dependency to remain relative:

```json
"dev.unity2foxglove.sdk": "file:../../Packages/dev.unity2foxglove.sdk"
```

If Unity rewrote it to an absolute path, change it back before sharing or committing.
