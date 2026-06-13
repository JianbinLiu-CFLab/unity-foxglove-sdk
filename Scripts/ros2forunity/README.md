# ROS2 For Unity Runtime Sync

This folder contains operator scripts for refreshing the optional ROS2 For Unity
Jazzy Win64 runtime package from a vetted runtime artifact zip.

## Default Refresh

Run from the repository root:

```powershell
python "Scripts\ros2forunity\sync_r2fu_artifact_to_unity2foxglove.py"
```

The default artifact path is:

```text
D:\ros2unity\artifacts\ros2-for-unity\jazzy\windows_x86_64\Ros2ForUnity_jazzy_standalone_windows_x86_64.zip
```

The script:

- Verifies the artifact SHA-256 against the adjacent manifest when present.
- Regenerates `Packages/dev.unity2foxglove.ros2forunity/Compliance/r2fu-jazzy-win64-runtime-inventory.json`.
- Rebuilds `Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64`.
- Reapplies Unity2Foxglove local patches, including package path support and the
  `ITimeSource.GetTime` bool-return contract for newer ros2cs.
- Runs `Scripts/release/validate_r2fu_runtime_package.py`.
- Writes a summary JSON under `D:\ros2unity\logs\unity2foxglove-r2fu`.

## Explicit Artifact

```powershell
python "Scripts\ros2forunity\sync_r2fu_artifact_to_unity2foxglove.py" `
  --artifact-zip "D:\ros2unity\artifacts\ros2-for-unity\jazzy\windows_x86_64\Ros2ForUnity_jazzy_standalone_windows_x86_64.zip"
```

## Dry Run

Use this before changing files:

```powershell
python "Scripts\ros2forunity\sync_r2fu_artifact_to_unity2foxglove.py" --dry-run
```

## Optional Unity Import Check

After the package sync passes, run a Unity batch import when you need machine
evidence that the project imports and compiles:

```powershell
python "Scripts\ros2forunity\sync_r2fu_artifact_to_unity2foxglove.py" --run-unity-import
```

Unity runtime smoke, ROS2 graph checks, RViz2/Foxglove checks, and scene-specific
acceptance are separate manual gates.
