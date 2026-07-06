# Unity2Foxglove Remote Gateway - Win64

Optional Windows x64 package for publishing Unity2Foxglove topics to
Foxglove Cloud through the official Foxglove Remote Access Gateway C ABI.

This package is default-off and disabled by default. Enabling it publishes live
Unity scene data to Foxglove Cloud. Phase 171 starts as outbound-only visualization: local
channel registrations and publishes can be mirrored to the native gateway,
while ClientPublish, Services, Parameters, Assets, and ConnectionGraph remain
future opt-ins.

Use `FOXGLOVE_DEVICE_TOKEN` or editor-only storage for device tokens. Do not
serialize production tokens into scenes and do not log them.

Supported platform:

- Windows x64 Editor and WindowsStandalone64.

Unsupported in Phase 171:

- WebGL, consoles, macOS, Linux, mobile, and inbound cloud control.

Native artifacts are built by `Scripts/remotegateway/build_foxglove_c_win64.py`.
The script builds outside `Packages/` first, records artifact metadata, and only
copies approved plugin files into `Runtime/Plugins/Windows/x86_64/` when
explicitly requested.

## Real Foxglove Cloud acceptance

This package does not commit generated native artifacts. For manual cloud
acceptance on Windows, build the official C ABI DLL locally and launch Unity
from the same shell that owns the token environment variable.

Recommended helper:

```powershell
$env:FOXGLOVE_DEVICE_TOKEN="YOUR_TOKEN"
python Scripts/remotegateway/run_cloud_acceptance.py
```

If Unity is not found automatically:

```powershell
$env:FOXGLOVE_DEVICE_TOKEN="YOUR_TOKEN"
python Scripts/remotegateway/run_cloud_acceptance.py --unity-exe "C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe"
```

The helper runs `Scripts/remotegateway/build_foxglove_c_win64.py
--copy-to-package`, checks that `foxglove.dll` is present in the optional
package, starts Unity with `-projectPath Unity2Foxglove`, and writes a
run-specific checklist under `build/remotegateway/cloud-acceptance/`.

Manual validation steps:

1. Wait for Unity import/compile to finish.
2. Enter Play Mode.
3. Confirm the local `FoxgloveManager` is running and the local WebSocket path
   still works.
4. Enable `Enable Remote Gateway` on `FoxgloveRemoteGatewayController`.
5. Expect Unity Console to log:
   `[Foxglove] Remote gateway started. Publishing to Foxglove Cloud.`
6. In Foxglove Cloud, confirm the device is online, outbound topics appear, and
   visualization data is live.
7. Toggle `Enable Remote Gateway` off and on multiple times.
8. Exit and re-enter Play Mode, then repeat one enable/disable cycle.

Pass criteria:

- Default closed: with the gateway checkbox off, no cloud connection starts and
  local publishing remains unaffected.
- Failure closed: missing token, invalid token, or missing native DLL produces a
  warning/failure state without breaking the local WebSocket link.
- Cloud outbound path: existing visualization topics mirror to Cloud after the
  success log.
- Lifecycle: repeated toggles and Play Mode shutdown do not hang Unity and do
  not leave native gateway threads running.
- Scope: Phase 171 v1 validates outbound visualization only. ClientPublish,
  Services, Parameters, Assets, and ConnectionGraph are intentionally not
  expected.

Do not commit generated `foxglove.dll`, `foxglove.dll.lib`, or `foxglove.pdb`.
