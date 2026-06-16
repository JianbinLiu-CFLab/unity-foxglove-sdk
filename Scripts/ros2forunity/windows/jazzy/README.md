# Windows Jazzy ROS2 For Unity Tooling

This directory contains the current Windows Jazzy ROS2 For Unity runtime
package build, sync, inspect, validation, and distro-specific acceptance scripts.

Expected artifact input root:

```text
<repo-root>/r2fu-runtime-artifacts/jazzy/windows_x86_64/
```

Primary refresh command:

```powershell
python "Scripts\ros2forunity\windows\jazzy\sync_r2fu_artifact_to_unity2foxglove.py"
```

Tooling tests:

```powershell
python -m unittest discover -s "Scripts\ros2forunity\windows\jazzy\regression_checks" -p "test_*.py"
```
