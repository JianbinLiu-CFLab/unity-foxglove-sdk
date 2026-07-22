# ROS2 smoke and acceptance helpers

These helpers use repository-local ROS2 entrypoints and bounded child-process
ownership. They are operator tools: a green helper result is meaningful only
when it contains the correlated Unity and peer evidence required by that
helper's summary JSON.

## Phase181 Windows-local Editor bring-up

Phase181 proves a generated FoxRun custom DTO envelope, rather than a packaged
ROS2 message. Before using a wrapper:

1. Generate or revise the static interface source only through the Foxglove
   Manager's **Data Transport > ROS 2 Native Runtime (R2FU) — Shared > Custom
   FoxRun ROS 2 Interface** controls. A revision changes the lock; do not edit
   the generated `Ros2Package~` source by hand.
2. Resolve exactly one matching runtime package and exactly one matching add-on
   for the selected distro. The preflight must report the locked source and
   add-on digest as ready before native custom contracts can register.
3. Choose the same runtime/RMW, ROS domain, discovery scope, and compatible
   subscription QoS for Unity and the peer. FastDDS and Zenoh are separate
   communication modes. Lyrical Zenoh needs an explicit topology.
4. Import the **FoxRun Custom ROS2 Interface** sample or open the Phase181
   acceptance scene, then enter Play Mode after the helper reports that its
   correlated String publisher is waiting.

From this directory, each Windows-local Editor row has one no-argument entry
point:

```powershell
python .\phase181_humble_fastrtps_acceptance.py
python .\phase181_jazzy_fastrtps_acceptance.py
python .\phase181_lyrical_fastrtps_acceptance.py
python .\phase181_lyrical_zenoh_acceptance.py
```

Every wrapper waits up to 300 seconds for the custom String subscription. It
uses that String envelope to correlate Unity and the peer before it starts the
nested DTO, sequence, and null/empty probes. The default Lyrical Zenoh wrapper
starts and cleans up only its own router; `--no-zenoh-router` is an advanced
mode that requires an explicit externally-owned topology id. Do not use bare
`ros2`, another distro's Python environment, or an externally running router
as if the wrapper owned it.

The result JSON under `build/phase181/<profile>/windows-local-editor.json` is
redacted and records only bounded evidence. A profile-specific `*_PASS` result
is a Windows-local Editor bring-up proof. It is not Linux peer certification
and it is not Windows Player certification.

## Linux peer and Player certification

The Linux helper is intentionally explicit because the caller owns the Linux
ROS2 workspace and sourced distribution:

```text
phase181_custom_ros2_linux_peer.py --role <publisher|subscriber|bidirectional|orchestrate> \
  --profile-id <row> --surface <editor|player> --distro <humble|jazzy|lyrical> \
  --rmw <rmw_fastrtps_cpp|rmw_zenoh_cpp> --workspace <absolute-caller-workspace> \
  --unity-log <append-only-player-or-editor-log>
```

It stages or verifies the exact locked `Ros2Package~` tree in that caller-owned
workspace, builds only that package, and requires the full interface digest
reported by Unity. It never deletes the caller workspace. Player mode uses the
same correlated protocol, requires `--role orchestrate`, and treats a missing
terminal Player marker or nonzero Player exit code as a failure even if the
peer saw traffic.

Do not promote a Windows-local result into a Linux or Player pass. Record those
matrix cells separately with matching graph/type/QoS evidence and the decoded
generated envelope.

## Direction, origin, and recording semantics

`Publish`, `Subscribe`, and `PublishAndSubscribe` are independent
contracts. Native subscription QoS is selected at the contract/input side;
custom native publisher QoS is not fabricated as a Manager-global setting.

Publish-and-subscribe uses explicit echo-on-apply behavior. A same-origin
envelope returns to Unity and is dropped. A different or empty remote origin
applies, then may re-publish under the member's normal publish policy with a
new Unity origin. This is intentional: a `FixedRate` topology can feed back
between peers, so operators must choose topics and policy deliberately.

Native custom inbound receipts are not individually recorded to MCAP. A value
appears in MCAP only if its normal publish policy later emits it, using the
external-facing output representation. This keeps an MCAP file compatible with
the external reader rather than silently recording an internal Unity-side
conversion.

ROS domain IDs isolate discovery; they are not authentication. The helpers
never persist tokens, credentials, environment dumps, Zenoh configuration, or
full command lines. They do not turn unavailable typesupport, mismatched
digests, graph-only evidence, or Inspector-only evidence into a pass.
