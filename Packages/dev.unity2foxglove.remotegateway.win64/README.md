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
