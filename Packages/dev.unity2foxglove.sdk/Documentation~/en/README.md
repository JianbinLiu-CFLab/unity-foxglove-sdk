# Unity2Foxglove English User Manual

## Who should read this

Read this manual if you want to install Unity2Foxglove, connect Unity to Foxglove, record or replay MCAP files, or build an IL2CPP Player.

## What you will do

You will follow the same path most users need: install the package, connect Foxglove, publish Unity data, use Parameters and Services, record and replay MCAP, build a Player, and troubleshoot common problems.

> [!NOTE]
> SDK core targets Unity 6000.0 LTSC or later (compatible with 6000.0.74f1 LTSC). Unity 2022 is not supported. The included `Unity2Foxglove` demo project is developed and tested on Unity 6000.3.14f1 LTSC.

## 1. Start Here

- [00_Prerequisites](00_Prerequisites.md): install Unity, Foxglove Desktop, and optional developer tools.
- [01_Installation_and_Quick_Start](01_Installation_and_Quick_Start.md): install the package and see `/tf`, `/scene`, and `/unity/camera`.
- [02_Foxglove_Desktop_Operation](02_Foxglove_Desktop_Operation.md): use Foxglove Desktop panels and layouts.
- [03_Verifying_Basic_Visualization](03_Verifying_Basic_Visualization.md): import the minimal package sample.

## 2. Runtime Control

- [04_Parameters_and_Services](04_Parameters_and_Services.md): edit `/cube/color`, edit `/cube/scale`, and call `/cube/reset_pose`.
- [05_FoxRun_Zero_Code_Publishing](05_FoxRun_Zero_Code_Publishing.md): publish debug topics with `[FoxRun]` attributes.
- [10_Inspector_Reference](10_Inspector_Reference.md): understand the main Unity component fields.

## 3. Recording, Replay, and Builds

- [06_MCAP_Recording_and_Replay](06_MCAP_Recording_and_Replay.md): record Unity data, open MCAP in Foxglove, and replay in Unity.
- [07_IL2CPP_Build_Guide](07_IL2CPP_Build_Guide.md): build and verify a standalone Player with `Scripts/build_unity_il2cpp.py`.

## 4. Advanced Reading

- [08_Architecture](08_Architecture.md): runtime, protocol, MCAP, and FoxRun internals for advanced users.
- [09_Troubleshooting](09_Troubleshooting.md): symptom-based fixes.

## 5. Which Project Should I Open?

- Use your own Unity project when you want to integrate the SDK into an existing application.
- Use `Samples~/BasicVisualization` when you want the smallest importable sample.
- Use `Samples~/FullDemoVisualization` when you want the complete importable sample with Parameters, Services, FoxRun, MCAP, Input System, and URP.
- Use `Unity2Foxglove` when you want a ready-to-open development and acceptance project for this repository.
