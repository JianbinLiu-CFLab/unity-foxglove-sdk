# Official Schema Coverage

Unity2Foxglove includes the bundled official Foxglove protobuf schema snapshot generated under `Runtime/Schemas/Proto`.

## What "Full Schema Coverage" Means

In this package, full official schema coverage means every bundled `foxglove.*` protobuf message is:

- present in the explicit schema catalog;
- registered with protobuf descriptor bytes;
- sample-constructible in the runtime validation suite;
- publishable through a protobuf Foxglove channel;
- recordable to MCAP with protobuf schema and channel metadata.

The current bundled snapshot contains 46 official `foxglove.*` messages. Tests derive the expected count from the protobuf registry/descriptor metadata and require the explicit catalog to match it.

## Generic Parity vs Dedicated Components

Generic protobuf support is the parity layer. It proves that all bundled official schemas can travel through the SDK's schema, publish, and MCAP paths.

Dedicated Unity components are the UX layer. They provide Inspector fields, lifecycle integration, and Unity-specific convenience for common workflows. Phase 44 does not add one custom `MonoBehaviour` for every schema.

Current dedicated or polished Unity paths include:

- `foxglove.FrameTransform`
- `foxglove.SceneUpdate`
- `foxglove.CompressedImage` through the JSON camera publisher
- `foxglove.Log`

Other schemas can still be used through generic protobuf channels and generated protobuf message classes.

## Smoke MCAP

From the repository root:

```bash
python Scripts/smoke/generate_phase44_all_schemas_mcap.py
```

The script writes:

```text
build/test_mcap/phase44_all_schemas_smoke.mcap
```

Open that file in Foxglove Desktop and check the Problems panel. The smoke file is intended to validate protobuf schema parsing and MCAP metadata, not perfect panel rendering for every schema.

## Follow-Up Typed Publisher Candidates

Potential future dedicated publishers include:

- PointCloud
- LaserScan
- CameraCalibration
- Odometry
- LocationFix

These should be added when a real demo, dataset, or user workflow needs a polished Unity Inspector experience.
