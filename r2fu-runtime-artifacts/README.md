# R2FU Runtime Artifacts

This directory is the repository-local entry point for optional ROS2 For Unity
runtime artifacts used by local and CI validation scripts.

Do not commit generated runtime packages, ZIP files, DLLs, manifests, or build
outputs here. Distro subdirectories such as `jazzy/` and `lyrical/` are ignored.

Expected local shape:

```text
r2fu-runtime-artifacts/
  humble/
    windows_x86_64/
      Ros2ForUnity_humble_standalone_windows_x86_64.zip
  jazzy/
    windows_x86_64/
      Ros2ForUnity_jazzy_standalone_windows_x86_64.zip
  lyrical/
    windows_x86_64/
      Ros2ForUnity_lyrical_standalone_windows_x86_64.zip
```

On a developer workstation, the distro subdirectories may be junctions or
symlinks to an external artifact cache. For example:

```text
r2fu-runtime-artifacts/humble -> <external-artifact-cache>/humble
r2fu-runtime-artifacts/jazzy   -> <external-artifact-cache>/jazzy
r2fu-runtime-artifacts/lyrical -> <external-artifact-cache>/lyrical
```

CI should download, restore, or build the required artifacts into this directory
before running validation. Scripts should prefer this path by default:

```text
<repo-root>/r2fu-runtime-artifacts
```

Validation scripts may also expose an explicit artifact-root argument or an
environment variable for machines that keep artifacts elsewhere.
