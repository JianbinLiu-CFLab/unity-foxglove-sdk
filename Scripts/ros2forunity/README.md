# ROS2 For Unity Tooling

This folder contains ROS2 For Unity operator scripts. Tooling is split first by
platform and then by ROS distro so Windows, Ubuntu, Jazzy, and Lyrical flows do
not drift into one large release bucket.

```text
Scripts/ros2forunity/
  windows/
    jazzy/    Windows Jazzy build, sync, inspect, validation, and tests.
    lyrical/  Reserved for Windows Lyrical tooling.
  ubuntu/
    jazzy/    Reserved for Ubuntu/WSL2 Jazzy tooling.
    lyrical/  Reserved for Ubuntu/WSL2 Lyrical tooling.
```

## Windows Jazzy Refresh

Run from the repository root:

```powershell
python "Scripts\ros2forunity\windows\jazzy\sync_r2fu_artifact_to_unity2foxglove.py"
```

The default artifact path is:

```text
<repo-root>/r2fu-runtime-artifacts/jazzy/windows_x86_64/Ros2ForUnity_jazzy_standalone_windows_x86_64.zip
```

The script:

- Verifies the artifact SHA-256 against the adjacent manifest when present.
- Regenerates `Packages/dev.unity2foxglove.ros2forunity/Compliance/r2fu-jazzy-win64-runtime-inventory.json`.
- Rebuilds `Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64`.
- Reapplies Unity2Foxglove local patches, including package path support and the
  `ITimeSource.GetTime` bool-return contract for newer ros2cs.
- Runs `Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py`.
- Writes a summary JSON under `build/r2fu-sync-evidence`.

Unit tests for the Windows Jazzy tooling live beside the tools:

```powershell
python -m unittest discover -s "Scripts\ros2forunity\windows\jazzy\regression_checks" -p "test_*.py"
```

## Explicit Artifact

```powershell
python "Scripts\ros2forunity\windows\jazzy\sync_r2fu_artifact_to_unity2foxglove.py" `
  --artifact-zip "r2fu-runtime-artifacts\jazzy\windows_x86_64\Ros2ForUnity_jazzy_standalone_windows_x86_64.zip"
```

## Dry Run

Use this before changing files:

```powershell
python "Scripts\ros2forunity\windows\jazzy\sync_r2fu_artifact_to_unity2foxglove.py" --dry-run
```

## Optional Unity Import Check

After the package sync passes, run a Unity batch import when you need machine
evidence that the project imports and compiles:

```powershell
python "Scripts\ros2forunity\windows\jazzy\sync_r2fu_artifact_to_unity2foxglove.py" --run-unity-import
```

Unity runtime smoke, ROS2 graph checks, RViz2/Foxglove checks, and scene-specific
acceptance are separate manual gates. The Phase138B Windows Jazzy build
orchestrator is kept here because it is distro/platform-specific tooling rather
than a general smoke helper.
