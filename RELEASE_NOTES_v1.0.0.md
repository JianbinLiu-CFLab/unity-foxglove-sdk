# Unity2Foxglove v1.0.0 Release Notes

---

## Overview

Unity2Foxglove v1.0.0 is the first public release. It is a Unity SDK that streams real-time data (Transform, 3D scene, Camera frames, etc.) from Unity to the Foxglove visualization platform via WebSocket, with MCAP file recording and replay.

---

## Installation

### Local package path

1. Clone the repository: `git clone https://github.com/JianbinLiu-CFLab/Unity2Foxglove.git`
2. Unity menu: `Window > Package Manager > + > Add package from disk...`
3. Select `Packages/dev.unity2foxglove.sdk/package.json`

### Git URL (Unity Package Manager)

```
https://github.com/JianbinLiu-CFLab/Unity2Foxglove.git?path=Packages/dev.unity2foxglove.sdk
```

---

## Running the demo

### Open the Demo project

1. Unity Hub > Open > select the `Untiy2Foxglove` directory in the repository
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

- **Protobuf binary encoding**: Currently only JSON encoding is supported. Protobuf is planned for v1.1.0
- **WebGL**: Not supported due to dependency on `System.Net.Sockets.TcpListener`
- **macOS / Linux**: Not yet verified; potential compatibility issues may exist
- **Native Backend**: C native backend implementation exists in `Plugins/native/` but has not yet been integrated into the transport layer

---

## v1.1.0 plan

- Protobuf binary encoding support (full Foxglove WebSocket protocol compliance)
- macOS platform verification
- Native Backend integration

---

## Related documentation

- [Project README](README.md)
- [Package documentation](Packages/dev.unity2foxglove.sdk/Documentation~/README.md)
- [Demo project](Untiy2Foxglove/README.md)
- [Changelog](CHANGELOG.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

---

> Unity2Foxglove is an independent project and is not affiliated with or endorsed by Foxglove.
