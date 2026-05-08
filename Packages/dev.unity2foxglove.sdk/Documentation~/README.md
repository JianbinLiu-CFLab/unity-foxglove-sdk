# Unity2Foxglove Package Documentation

This folder contains the user-facing documentation for the Unity2Foxglove SDK package.

If you only want to run the ready-made demo project, start with `Unity2Foxglove/README.md` from the repository root. If you are installing the SDK into your own Unity project, start with the English user manual below.

## Languages

- [English user manual](en/README.md) is the canonical documentation for the current release.
- Chinese documentation remains under `zh/` and is maintained separately.

## English Manual

- [00 Prerequisites](en/00%20Prerequisites.md): required Unity, Foxglove, IDE, command-line, and build tools.
- [01 Installation and Quick Start](en/01%20Installation%20and%20Quick%20Start.md): install the package and publish your first `/tf`, `/scene`, and `/unity/camera` topics.
- [02 Foxglove Desktop Operation](en/02%20Foxglove%20Desktop%20Operation.md): connect Foxglove and use 3D, Image, Plot, Parameters, Service Call, and Problems panels.
- [03 Verifying Basic Visualization](en/03%20Verifying%20Basic%20Visualization.md): import and verify the minimal Basic Visualization sample.
- [04 Parameters and Services](en/04%20Parameters%20and%20Services.md): edit runtime parameters and call Unity services from Foxglove.
- [05 FoxRun Zero Code Publishing](en/05%20FoxRun%20Zero%20Code%20Publishing.md): publish debug values with `[FoxRun]`.
- [06 MCAP Recording and Replay](en/06%20MCAP%20Recording%20and%20Replay.md): record `.mcap` files, open them in Foxglove, and replay them in Unity.
- [07 IL2CPP Build Guide](en/07%20IL2CPP%20Build%20Guide.md): build and verify standalone Players with the repository build script.
- [08 Architecture](en/08%20Architecture.md): advanced overview of runtime, protocol, FoxRun, MCAP, and replay internals.
- [09 Troubleshooting](en/09%20Troubleshooting.md): symptom-based fixes for connection, topics, panels, builds, and replay.
- [10 Inspector Reference](en/10%20Inspector%20Reference.md): field-by-field reference for the main Unity components.

## Related Entry Points

- Package root: `Packages/dev.unity2foxglove.sdk`
- Basic sample: `Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization`
- Full demo sample: `Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization`
- Standalone demo project: `Unity2Foxglove`
- Repository build scripts: `Scripts`
