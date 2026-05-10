## 1. Purpose

Use this manual as the entry point for installing Unity2Foxglove, connecting Unity to Foxglove, recording or replaying MCAP files, and building an IL2CPP Player.

## 2. Workflow

You will follow the same path most users need: install the package, connect Foxglove, publish Unity data, use Parameters and Services, record and replay MCAP, build a Player, and troubleshoot common problems.

> [!NOTE]
> SDK core targets Unity 6000.0 LTSC or later (compatible with 6000.0.74f1 LTSC). Unity 2022 is not supported. The included `Unity2Foxglove` demo project is developed and tested on Unity 6000.3.14f1 LTSC.

## 3. Start Here

- [01_Prerequisites](01_Prerequisites.md): install Unity, Foxglove Desktop, and optional developer tools.
- [02_Installation_and_Quick_Start](02_Installation_and_Quick_Start.md): install the package and see `/tf`, `/scene`, and `/unity/camera`.
- [03_Samples_and_Demo_Project](03_Samples_and_Demo_Project.md): choose the right project or package sample before opening Unity.
- [04_Foxglove_Desktop_Operation](04_Foxglove_Desktop_Operation.md): use Foxglove Desktop panels and layouts.
- [05_Verifying_Basic_Visualization](05_Verifying_Basic_Visualization.md): import the minimal package sample.

## 4. Runtime Control

- [06_Parameters_and_Services](06_Parameters_and_Services.md): edit `/cube/color`, edit `/cube/scale`, and call `/cube/reset_pose`.
- [07_FoxRun_Zero_Code_Publishing](07_FoxRun_Zero_Code_Publishing.md): publish debug topics with `[FoxRun]` attributes.
- [12_Inspector_Reference](12_Inspector_Reference.md): understand the main Unity component fields.

## 5. Recording, Replay, and Builds

- [08_MCAP_Recording_and_Replay](08_MCAP_Recording_and_Replay.md): record Unity data, open MCAP in Foxglove, and replay in Unity.
- [09_IL2CPP_Build_Guide](09_IL2CPP_Build_Guide.md): build and verify a standalone Player with `Scripts/build_unity_il2cpp.py`.

## 6. Advanced Reading

- [10_Architecture](10_Architecture.md): runtime, protocol, MCAP, and FoxRun internals for advanced users.
- [11_Troubleshooting](11_Troubleshooting.md): symptom-based fixes.
- [13_Schema_Coverage](13_Schema_Coverage.md): official protobuf schema coverage and typed publisher boundaries.

## 7. Project Selection

- Use your own Unity project when you want to integrate the SDK into an existing application.
- Use `Samples~/BasicVisualization` when you want the smallest importable sample.
- Use `Samples~/FullDemoVisualization` when you want the complete importable sample with Parameters, Services, FoxRun, MCAP, Input System, and URP.
- Use `Unity2Foxglove` when you want a ready-to-open development and acceptance project for this repository.
