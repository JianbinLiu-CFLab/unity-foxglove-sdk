# Phase 88 Draco Evidence Template

Target local note: `Developer/50 Phase88 Compressed PointCloud Draco Evidence Gate.md`

`Developer/*` is ignored. Mirror the final verdict, commit under test, Draco
commit, and probe output summary in the handoff or PR description.

## Branch And Commit

- Branch:
- Commit under test:
- `git status --short --branch`:

## Tool Versions

- Unity version:
- Foxglove Desktop/Web version:
- OS:
- Compiler:
- CMake/build tools:

## Draco Source

- Google Draco tag or commit:
- Source path:
- Build command:
- Build output summary:
- Bundled plugin path: `Packages/dev.unity2foxglove.sdk/Runtime/Plugins/Windows/x86_64/Unity2FoxgloveDracoNative.dll`

## Smoke Input

- Raw and compressed fed by the same generated PointCloudFrame: yes/no
- If no, describe the parameter-equivalent setup:
- Point count:
- XYZ-only:
- Intensity/ring/reflectivity/time disabled:

## Raw Probe Output

```text
paste output from Scripts/smoke/pointcloud_qos_probe.py
```

## Compressed Probe Output

```text
paste output from Scripts/smoke/compressed_pointcloud_draco_probe.py
```

## Foxglove 3D Live Display

- Result:
- Spatial plausibility:
- Warnings/errors:

## MCAP Record And Replay

- Recording path:
- Foxglove replay result:
- Warnings/errors:

## MCAP Byte-Level Inspection

```text
paste output from Scripts/smoke/compressed_pointcloud_mcap_inspect.py
```

## Payload-Size Comparison

- Raw PointCloud.data average bytes:
- Draco CompressedPointCloud.data average bytes:
- Ratio:

## Synchronous Native Plugin Caveat

- `DracoPointCloudNativeEncoder.TryEncode()` is synchronous in the Unity publish path.
- Observed live-session symptoms:
- Productization follow-up needed:

## Final Verdict

Choose one: GREEN / YELLOW / RED / BLOCKED

- Verdict:
- Reason:
- Next recommendation:
