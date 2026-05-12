# Unity2Foxglove v1.0.0 Release Notes

---

## Overview

Unity2Foxglove v1.0.0 is the first public release. It is a Unity SDK that streams real-time data (Transform, 3D scene, Camera frames, custom fields) from Unity to the Foxglove visualization platform via WebSocket, with MCAP file recording, replay, and protobuf support.

---

## Installation

### Local package path

1. Clone the repository: `git clone https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk.git`
2. Unity menu: `Window > Package Manager > + > Add package from disk...`
3. Select `Packages/dev.unity2foxglove.sdk/package.json`

### Git URL (Unity Package Manager)

```
https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk.git?path=/Packages/dev.unity2foxglove.sdk
```

---

## Running the demo

### Open the Demo project

1. Unity Hub > Open > select the `Unity2Foxglove` directory in the repository
2. The scene is pre-configured with `FoxgloveManager` and all Publisher components
3. Press Play
4. Open Foxglove Desktop > Open connection > Foxglove WebSocket > `ws://127.0.0.1:8765`

### Run dotnet tests

```bash
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
```

---

## Verified platforms

| Platform | Status |
|----------|--------|
| **Windows 10** -- Editor | Verified |
| **Windows 10** -- IL2CPP Standalone Player | Verified |

---

## Known limitations

- **WebGL**: Not supported due to dependency on `System.Net.Sockets.TcpListener`
- **macOS / Linux**: Not yet verified
- **WSS / TLS**: Not implemented; default bind is `127.0.0.1`
- **Authentication**: Not implemented
- **Native Backend**: C native backend implementation exists but has not been integrated

---

## Related documentation

- [Project README](../../README.md)
- [Package documentation](../../Packages/dev.unity2foxglove.sdk/Documentation~/README.md)
- [Demo project](../../Unity2Foxglove/README.md)
- [Changelog](../../CHANGELOG.md)
- [Roadmap](../../ROADMAP.md)
- [Third-party notices](../../THIRD_PARTY_NOTICES.md)

---

> Unity2Foxglove is an independent project and is not affiliated with or endorsed by Foxglove.
