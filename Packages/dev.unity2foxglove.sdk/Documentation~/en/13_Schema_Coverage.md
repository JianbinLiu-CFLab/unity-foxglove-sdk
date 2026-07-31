## 1. Purpose

Use this page to understand how the core Unity2Foxglove SDK validates official
Foxglove schemas, and where generic schema parity differs from dedicated Unity
publisher UX.

The core package bundles the official Foxglove protobuf snapshot under
`Runtime/Schemas/Proto`. ROS message catalogs, CDR codecs, and ROS transport
adapters belong to opt-in companion packages:

- `dev.unity2foxglove.ros2bridge` for the localhost sidecar Provider;
- `dev.unity2foxglove.ros2forunity` for the native R2FU Provider.

The SDK schema manifest aggregate under `Assets/Generated/Unity2Foxglove/`
records the core protobuf registry, FoxRun evidence, and typed publisher catalog
in one deterministic artifact. It is schema-coverage evidence, not replay
governance: replay mismatch checks continue to use only the FoxRun
`globalManifestHash` recorded in MCAP metadata.

Schema Evidence settings let projects decide how much identity enforcement to
use. `Off` skips identity checks, `Warn` reports mismatches while continuing,
and `Strict` blocks replay on confirmed FoxRun hash mismatch and requires
complete recording evidence.

Recording evidence is stored beside each MCAP file in a sibling directory with
the same base name and a `.schema` suffix. That sidecar contains the
`schema-evidence.json` index plus the available FoxRun and Unity2Foxglove schema
manifests, so replay validation can compare the exact evidence captured for the
recording without depending on an installed ROS transport Provider.

## 2. Core Coverage Definition

In the core package, full official schema coverage means every bundled
`foxglove.*` protobuf message is:

- present in the explicit schema catalog;
- registered with protobuf descriptor bytes;
- sample-constructible in the runtime validation suite;
- publishable through a protobuf Foxglove channel;
- recordable to MCAP with protobuf schema and channel metadata.

Tests derive the expected count from the protobuf registry and descriptor
metadata and require the explicit catalog to match it.

## 3. Generic Parity and Dedicated Components

Generic protobuf support is the parity layer. It proves that bundled official
schemas can travel through the SDK schema, publish, and MCAP paths.

Dedicated Unity components are the UX layer. They provide Inspector fields,
lifecycle integration, and Unity-specific convenience for common workflows.
Current polished paths include:

- `foxglove.FrameTransform`;
- `foxglove.SceneUpdate`;
- `foxglove.CompressedImage`;
- `foxglove.PointCloud`;
- `foxglove.CompressedPointCloud`;
- `foxglove.LaserScan`;
- `foxglove.CameraCalibration`;
- `foxglove.Log`.

Other schemas can still be used through generic protobuf channels and generated
protobuf message classes.

Publisher Encoding defaults to Protobuf for new `FoxgloveManager` components.
Publishers that support several core encodings can select JSON, Protobuf, or
MessagePack through the Manager or a component override. A Provider-only
payload mode, such as packed point-cloud handoff, does not silently fall back
to a WebSocket encoding.

For `foxglove.CompressedImage`, the JSON path stores JPEG data as base64 text
because JSON has no binary field. The protobuf path stores the same JPEG
payload as raw bytes in the official `bytes data` field, so it is the preferred
core path for camera streaming.

## 4. Smoke MCAP

From the repository root:

```bash
python Scripts/smoke/mcap/phase44_all_schemas_mcap.py
```

The script writes:

```text
build/test_mcap/phase44_all_schemas_smoke.mcap
```

Open that file in Foxglove Desktop and check the Problems panel. The fixture
validates protobuf schema parsing and MCAP metadata, not perfect panel
rendering for every schema.

ROS-specific schema and wire fixtures are maintained and validated by their
owning companion package and repository tooling.

## 5. Follow-Up Typed Publisher Candidates

Potential future dedicated core publishers include:

- Odometry;
- LocationFix;
- RawImage;
- RawAudio.

Add them when a real demo, dataset, or user workflow needs a polished Unity
Inspector experience.
