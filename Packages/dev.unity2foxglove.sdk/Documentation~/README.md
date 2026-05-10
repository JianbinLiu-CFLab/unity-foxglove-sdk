# Unity2Foxglove Package Documentation

This folder contains the user-facing documentation for the Unity2Foxglove SDK package.

If you only want to run the ready-made demo project, start with `Unity2Foxglove/README.md` from the repository root. If you are installing the SDK into your own Unity project, start with the English user manual below.

## Languages

- [English user manual](en/README.md) is the canonical documentation for the current release.
- Chinese documentation remains under `zh/` and is maintained separately.

## English Manual

- [00_Prerequisites](en/00_Prerequisites.md): required Unity, Foxglove, IDE, command-line, and build tools.
- [01_Installation_and_Quick_Start](en/01_Installation_and_Quick_Start.md): install the package and publish your first `/tf`, `/scene`, and `/unity/camera` topics.
- [02_Samples_and_Demo_Project](en/02_Samples_and_Demo_Project.md): choose between your own project, Basic sample, Full Demo sample, and the repository demo project.
- [03_Foxglove_Desktop_Operation](en/03_Foxglove_Desktop_Operation.md): connect Foxglove and use 3D, Image, Plot, Parameters, Service Call, and Problems panels.
- [04_Verifying_Basic_Visualization](en/04_Verifying_Basic_Visualization.md): import and verify the minimal Basic Visualization sample.
- [05_Parameters_and_Services](en/05_Parameters_and_Services.md): edit runtime parameters and call Unity services from Foxglove.
- [06_FoxRun_Zero_Code_Publishing](en/06_FoxRun_Zero_Code_Publishing.md): publish debug values with `[FoxRun]`.
- [07_MCAP_Recording_and_Replay](en/07_MCAP_Recording_and_Replay.md): record `.mcap` files, open them in Foxglove, and replay them in Unity.
- [08_IL2CPP_Build_Guide](en/08_IL2CPP_Build_Guide.md): build and verify standalone Players with the repository build script.
- [09_Architecture](en/09_Architecture.md): advanced overview of runtime, protocol, FoxRun, MCAP, and replay internals.
- [10_Troubleshooting](en/10_Troubleshooting.md): symptom-based fixes for connection, topics, panels, builds, and replay.
- [11_Inspector_Reference](en/11_Inspector_Reference.md): field-by-field reference for the main Unity components.
- [12_Schema_Coverage](en/12_Schema_Coverage.md): official protobuf schema coverage, generic parity, and typed publisher UX boundaries.

## Related Entry Points

- Package root: `Packages/dev.unity2foxglove.sdk`
- Basic sample: `Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization`
- Full demo sample: `Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization`
- Standalone demo project: `Unity2Foxglove`
- Repository build scripts: `Scripts`
