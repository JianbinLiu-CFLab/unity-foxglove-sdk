# 7. IL2CPP Build Guide

## Who should read this

Read this if you need to build and verify a standalone Unity Player with Unity2Foxglove.

## What you will do

You will run the cross-platform build script, watch progress logs, find the output Player and log file, and verify Foxglove topics in the Player.

## 7.1 Before You Build

Use the `Untiy2Foxglove` demo project for the standard repository build.

Check:

- Unity Hub has the target platform module installed.
- Python is available from your terminal.
- The repository package dependency uses a relative path.
- The demo runs in Editor Play Mode first.

## 7.2 Build from the Repository Root

Run:

```powershell
python Scripts/build_unity_il2cpp.py --target win64
```

Other targets:

```powershell
python Scripts/build_unity_il2cpp.py --target linux64
python Scripts/build_unity_il2cpp.py --target macos
```

If Unity is not auto-detected, pass it explicitly:

```powershell
python Scripts/build_unity_il2cpp.py --target win64 --unity "path/to/Unity"
```

## 7.3 Watch Build Progress

The script prints elapsed time and important Unity log lines so the terminal does not look frozen.

Important log lines:

```text
[FoxgloveBuild] Starting Windows IL2CPP Player build...
[FoxrunBuildPreprocess] Generating FoxRun source files...
[FoxrunCodeGenerator] Generated TestLog_FoxRun.g.cs
Build succeeded
```

If FoxRun debug topics are expected in the Player, the preprocess lines are important.

## 7.4 Output Files

Build output and logs are written under:

`build/Unity/`

Typical Windows Player output:

`build/Unity/WindowsIL2CPP/FoxgloveDemo.exe`

Typical build log:

`build/Unity/<target>-il2cpp-<timestamp>.log`

## 7.5 Verify the Player

1. Run the Player.
2. Open Foxglove Desktop.
3. Connect to `ws://127.0.0.1:8765`.
4. Verify expected topics:
   - `/tf`
   - `/scene`
   - `/unity/camera`
   - `/debug/...` if the demo includes FoxRun sources
5. Open Parameters and Service Call panels if using the Full Demo.
6. Call `/cube/reset_pose` with `{}`.

## 7.6 Common Failures

| Symptom | Likely cause | Fix |
|---|---|---|
| Unity exits with code `1` | Build failed. | Open the log path printed by the script. |
| No `/debug/...` topics in Player | FoxRun fallback did not generate. | Look for `[FoxrunBuildPreprocess]` and generated `.g.cs` logs. |
| Foxglove connects but topics are empty | Player is not running or server did not start. | Check Player log and port `8765`. |
| JSON messages become `{}` in Player | Linker preservation problem. | Verify project `Assets/link.xml` exists and preserves Newtonsoft.Json and `Unity.FoxgloveSDK`. |
| Compression-related build error | Compression DLLs are missing or excluded. | Verify package plugin DLLs and asmdef references. |

## 7.7 What This Guide Does Not Cover

This page is the practical build path. For source generator internals, linker behavior, and runtime architecture, read [08 Architecture](08%20Architecture.md).
