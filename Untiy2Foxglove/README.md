# Untiy2Foxglove — Dev & Test Project

This is the repository development project, **not a user template**. It is used for:

- Manual verification (Editor Play Mode)
- Windows IL2CPP Player builds
- Bug reproduction and debugging
- Full demo experience during development

## For Users

If you installed `dev.unity2foxglove.sdk` and want a ready-to-run demo, import the samples from Package Manager:

- **Basic Visualization** — minimal scene, no extra dependencies
- **Full Demo Visualization** — complete demo (Parameters, Services, FoxRun, MCAP); requires Input System + URP

Do **not** copy this project as a template. Use the samples instead.

## For Developers

### IL2CPP Build

```powershell
python Scripts/build_unity_il2cpp.py
```

Or manually:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe" -batchmode -quit `
  -projectPath "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\Untiy2Foxglove" `
  -executeMethod FoxgloveBuild.BuildWindowsIl2Cpp `
  -logFile "build\Unity\il2cpp.log"
```

### Foxglove Layout

Layout presets for Foxglove Desktop are in `Configs/`:

- `FoxgloveFullLayout.json` — full demo layout (3D, Image, Plot, Parameters, Service Call, TopicGraph, Publish)
