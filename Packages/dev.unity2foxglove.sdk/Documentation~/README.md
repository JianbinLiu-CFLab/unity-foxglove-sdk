# Unity2Foxglove Package Documentation

This folder contains the user-facing documentation for the Unity2Foxglove SDK package.

If you only want to run the ready-made demo project, start with `Unity2Foxglove/README.md` from the repository root. If you are installing the SDK into your own Unity project, start with the English user manual below.

## Languages

- [English user manual](en/00_Overview.md) is the canonical documentation for the current release.
- Chinese documentation remains under `zh/` and is maintained separately.
- German documentation starts under [deu/00_Uebersicht.md](deu/00_Uebersicht.md) and is being synchronized progressively.

## English Manual

- [00_Overview](en/00_Overview.md): user manual map and recommended reading order.
- [01_Prerequisites](en/01_Prerequisites.md): required Unity, Foxglove, IDE, command-line, and build tools.
- [02_Installation_and_Quick_Start](en/02_Installation_and_Quick_Start.md): install the package and publish your first `/tf`, `/scene`, and `/unity/camera` topics.
- [03_Samples_and_Demo_Project](en/03_Samples_and_Demo_Project.md): choose between your own project, Basic sample, Full Demo sample, and the repository demo project.
- [04_Foxglove_Desktop_Operation](en/04_Foxglove_Desktop_Operation.md): connect Foxglove and use 3D, Image, Plot, Parameters, Service Call, and Problems panels.
- [05_Verifying_Basic_Visualization](en/05_Verifying_Basic_Visualization.md): import and verify the minimal Basic Visualization sample.
- [06_Parameters_and_Services](en/06_Parameters_and_Services.md): edit runtime parameters and call Unity services from Foxglove.
- [07_FoxRun_Zero_Code_Publishing](en/07_FoxRun_Zero_Code_Publishing.md): publish debug values with `[FoxRun]`.
- [08_MCAP_Recording_and_Replay](en/08_MCAP_Recording_and_Replay.md): record `.mcap` files, open them in Foxglove, and replay them in Unity.
- [09_IL2CPP_Build_Guide](en/09_IL2CPP_Build_Guide.md): build and verify standalone Players with the repository build script.
- [10_Architecture](en/10_Architecture.md): advanced overview of runtime, protocol, FoxRun, MCAP, and replay internals.
- [11_Troubleshooting](en/11_Troubleshooting.md): symptom-based fixes for connection, topics, panels, builds, and replay.
- [12_Inspector_Reference](en/12_Inspector_Reference.md): field-by-field reference for the main Unity components.
- [13_Schema_Coverage](en/13_Schema_Coverage.md): official protobuf schema coverage, generic parity, and typed publisher UX boundaries.
- [14_Typed_Sensor_Publishers](en/14_Typed_Sensor_Publishers.md): PointCloud, LaserScan, and CameraCalibration publishers.
- [15_Secure_WSS](en/15_Secure_WSS.md): optional Unity-native WSS/TLS setup, root CA distribution, and token-gate limitations.

## Related Entry Points

- Package root: `Packages/dev.unity2foxglove.sdk`
- Basic sample: `Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization`
- Full demo sample: `Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization`
- Standalone demo project: `Unity2Foxglove`
- Repository build scripts: `Scripts`
