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

The supported environment overrides are:

| Variable | Default | Applies to |
| --- | --- | --- |
| `R2FU_ARTIFACT_ROOT` | `<repo-root>/r2fu-runtime-artifacts` | Humble, Jazzy, and Lyrical sync scripts |
| `R2FU_EVIDENCE_DIR` | `<repo-root>/build/r2fu-sync-evidence` | Humble, Jazzy, and Lyrical sync scripts |
| `R2FU_JAZZY_ROS2_BIN` | `<repo-root>/ros2-windows/ros2_jazzy/bin` | Jazzy sync script only |

These variables select local input/output paths; they do not change the
runtime's ROS2 environment contract. Command-line artifact, evidence, and
Unity paths take precedence when the selected sync script exposes those
options. The artifact root must contain the distro-specific Windows x64 ZIP
under `<distro>/windows_x86_64/`.

## Runtime Environment Contract

The first active runtime context acquires a process-wide lease for the ROS
variables it changes, snapshots unset and present values, and conditionally
restores values still equal to its own writes when the last safe shutdown
completes. Caller changes are preserved. A failed startup attempts the same
rollback; an incomplete restore keeps the lease pending and blocks a new
context until cleanup succeeds. The runtime may update `ROS_DISTRO`,
`AMENT_PREFIX_PATH`, `RMW_IMPLEMENTATION`, runtime-specific `RCUTILS_*` and
`ROS2CS_*`, and the native plugin `PATH`; domain, discovery, firewall, and
Zenoh configuration remain caller-owned. Native libraries cannot be unloaded or
safely mixed, so restart Unity after changing distro, runtime package, or
communication mode.
