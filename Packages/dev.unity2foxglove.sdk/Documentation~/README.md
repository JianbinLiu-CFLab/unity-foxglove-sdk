# Unity2Foxglove Package Documentation

This folder contains the user-facing documentation for the Unity2Foxglove SDK package.

If you only want to run the ready-made demo project, start with `Unity2Foxglove/README.md` from the repository root. If you are installing the SDK into your own Unity project, start with the English user manual below.

## Languages

- [English user manual](en/README.md) is the canonical documentation for the current release.
- Chinese documentation remains under `zh/` and is maintained separately.

## English Manual

- [00_Prerequisites](00_Prerequisites.md): required Unity, Foxglove, IDE, command-line, and build tools.
- [01_Installation_and_Quick_Start](01_Installation_and_Quick_Start.md): install the package and publish your first `/tf`, `/scene`, and `/unity/camera` topics.
- [02_Foxglove_Desktop_Operation](02_Foxglove_Desktop_Operation.md): connect Foxglove and use 3D, Image, Plot, Parameters, Service Call, and Problems panels.
- [03_Verifying_Basic_Visualization](03_Verifying_Basic_Visualization.md): import and verify the minimal Basic Visualization sample.
- [04_Parameters_and_Services](04_Parameters_and_Services.md): edit runtime parameters and call Unity services from Foxglove.
- [05_FoxRun_Zero_Code_Publishing](05_FoxRun_Zero_Code_Publishing.md): publish debug values with `[FoxRun]`.
- [06_MCAP_Recording_and_Replay](06_MCAP_Recording_and_Replay.md): record `.mcap` files, open them in Foxglove, and replay them in Unity.
- [07_IL2CPP_Build_Guide](07_IL2CPP_Build_Guide.md): build and verify standalone Players with the repository build script.
- [08_Architecture](08_Architecture.md): advanced overview of runtime, protocol, FoxRun, MCAP, and replay internals.
- [09_Troubleshooting](09_Troubleshooting.md): symptom-based fixes for connection, topics, panels, builds, and replay.
- [10_Inspector_Reference](10_Inspector_Reference.md): field-by-field reference for the main Unity components.

## Related Entry Points

- Package root: `Packages/dev.unity2foxglove.sdk`
- Basic sample: `Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization`
- Full demo sample: `Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization`
- Standalone demo project: `Unity2Foxglove`
- Repository build scripts: `Scripts`
