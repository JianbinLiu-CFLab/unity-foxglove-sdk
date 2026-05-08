# Unity2Foxglove English User Manual

## Who should read this

Read this manual if you want to install Unity2Foxglove, connect Unity to Foxglove, record or replay MCAP files, or build an IL2CPP Player.

## What you will do

You will follow the same path most users need: install the package, connect Foxglove, publish Unity data, use Parameters and Services, record and replay MCAP, build a Player, and troubleshoot common problems.

> [!NOTE]
> SDK core targets Unity 2022.3+. The included `Unity2Foxglove` demo project is validated with Unity 6.

## 1. Start Here

- [00 Prerequisites](00%20Prerequisites.md): install Unity, Foxglove Desktop, and optional developer tools.
- [01 Installation and Quick Start](01%20Installation%20and%20Quick%20Start.md): install the package and see `/tf`, `/scene`, and `/unity/camera`.
- [02 Foxglove Desktop Operation](02%20Foxglove%20Desktop%20Operation.md): use Foxglove Desktop panels and layouts.
- [03 Verifying Basic Visualization](03%20Verifying%20Basic%20Visualization.md): import the minimal package sample.

## 2. Runtime Control

- [04 Parameters and Services](04%20Parameters%20and%20Services.md): edit `/cube/color`, edit `/cube/scale`, and call `/cube/reset_pose`.
- [05 FoxRun Zero Code Publishing](05%20FoxRun%20Zero%20Code%20Publishing.md): publish debug topics with `[FoxRun]` attributes.
- [10 Inspector Reference](10%20Inspector%20Reference.md): understand the main Unity component fields.

## 3. Recording, Replay, and Builds

- [06 MCAP Recording and Replay](06%20MCAP%20Recording%20and%20Replay.md): record Unity data, open MCAP in Foxglove, and replay in Unity.
- [07 IL2CPP Build Guide](07%20IL2CPP%20Build%20Guide.md): build and verify a standalone Player with `Scripts/build_unity_il2cpp.py`.

## 4. Advanced Reading

- [08 Architecture](08%20Architecture.md): runtime, protocol, MCAP, and FoxRun internals for advanced users.
- [09 Troubleshooting](09%20Troubleshooting.md): symptom-based fixes.

## 5. Which Project Should I Open?

- Use your own Unity project when you want to integrate the SDK into an existing application.
- Use `Samples~/BasicVisualization` when you want the smallest importable sample.
- Use `Samples~/FullDemoVisualization` when you want the complete importable sample with Parameters, Services, FoxRun, MCAP, Input System, and URP.
- Use `Unity2Foxglove` when you want a ready-to-open development and acceptance project for this repository.
