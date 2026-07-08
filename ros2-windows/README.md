# ros2-windows

Local entry points for Windows ROS 2 distributions.

This directory contains directory junctions, not the ROS 2 distributions
themselves:

```text
ros2_humble  -> C:\ros2_humble\ros2-windows
ros2_jazzy   -> C:\ros2_jazzy\ros2-windows
ros2_lyrical -> C:\ros2_lyrical\ros2-windows
```

Keep this folder lightweight. Do not copy full ROS 2 installs into this
directory. Install or extract each distro under `C:\ros2_*`, then create a
junction here.

## Official Windows Archives

Use stable GitHub release URLs in notes and scripts. Browser download links from
`release-assets.githubusercontent.com` are temporary signed URLs and should not
be saved as canonical references.

### Humble

ROS 2 Humble Hawksbill Patch Release 14:

```text
release-humble-20260220
```

Stable download URL:

```text
https://github.com/ros2/ros2/releases/download/release-humble-20260220/ros2-humble-20260220-windows-release-amd64.zip
```

Expected local install root:

```text
C:\ros2_humble\ros2-windows
```

### Jazzy

Tested ROS 2 Jazzy Jalisco Patch Release 7:

```text
release-jazzy-20260128
```

Stable download URL:

```text
https://github.com/ros2/ros2/releases/download/release-jazzy-20260128/ros2-jazzy-20260128-windows-release-amd64.zip
```

Expected local install root:

```text
C:\ros2_jazzy\ros2-windows
```

### Lyrical

Tested ROS 2 Lyrical Windows archive:

```text
release-lyrical-20260522
```

Stable download URL:

```text
https://github.com/ros2/ros2/releases/download/release-lyrical-20260522/ros2-lyrical-2026-05-22-windows-AMD64.zip
```

Expected local install root:

```text
C:\ros2_lyrical\ros2-windows
```

## Recreate Junctions

Run from this directory:

```powershell
if (Test-Path .\ros2_humble)  { Remove-Item .\ros2_humble  -Force }
if (Test-Path .\ros2_jazzy)   { Remove-Item .\ros2_jazzy   -Force }
if (Test-Path .\ros2_lyrical) { Remove-Item .\ros2_lyrical -Force }
New-Item -ItemType Junction -Path .\ros2_humble  -Target C:\ros2_humble\ros2-windows
New-Item -ItemType Junction -Path .\ros2_jazzy   -Target C:\ros2_jazzy\ros2-windows
New-Item -ItemType Junction -Path .\ros2_lyrical -Target C:\ros2_lyrical\ros2-windows
```

If a junction already exists and points to the correct target, leave it alone.
If it points somewhere else, verify the intended local install path before
removing or recreating it.
