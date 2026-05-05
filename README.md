# Unity2Foxglove SDK

Real-time data streaming from Unity to [Foxglove](https://foxglove.dev) for visualization.

## Quick Start (Unity Package Manager)

1. Add `dev.unity2foxglove.sdk` to your project via local path or tarball
2. Import a sample from Package Manager:
   - **Basic Visualization** — minimal scene (Transform, SceneUpdate, Camera). No extra dependencies.
   - **Full Demo Visualization** — complete demo (Parameters, Services, FoxRun, MCAP). Requires Input System + URP.
3. Open the sample scene and press Play
4. In Foxglove Desktop, connect to `ws://127.0.0.1:8765`

## Repository Structure

```
Packages/dev.unity2foxglove.sdk/    ← UPM package (the SDK itself)
  Runtime/     Core SDK (protocol, transport, schemas, Unity components)
  Editor/      Inspector customizations, [FoxRun] source generator
  Tests/       dotnet runtime tests (cross-platform)
  Samples~/    Importable UPM samples
  Plugins/     Compression DLLs (LZ4, Zstd)
  Documentation~/

Untiy2Foxglove/                     ← Dev/test/IL2CPP verification project (NOT a user template)
  Assets/       Demo scene, scripts, settings
  Configs/      Foxglove layout presets

Scripts/                            ← Build automation, sync utilities
Plan/                               ← Implementation plans (Phase 0–19)
```

## Documentation

- [SDK Documentation](Packages/dev.unity2foxglove.sdk/Documentation~/README.md)
- [Architecture](Packages/dev.unity2foxglove.sdk/Documentation~/Architecture.md)
- [FoxRun / ISG](Packages/dev.unity2foxglove.sdk/Documentation~/FoxgloveLog.md)
- [Native Backend Evaluation](Packages/dev.unity2foxglove.sdk/Documentation~/NativeBackendEvaluation.md)

## Requirements

- Unity 2022.3+ (core SDK)
- Samples tested on Unity 6000.3 LTSC, Windows Editor
- WebGL not supported

## License

Apache-2.0 — see [LICENSE](LICENSE).
