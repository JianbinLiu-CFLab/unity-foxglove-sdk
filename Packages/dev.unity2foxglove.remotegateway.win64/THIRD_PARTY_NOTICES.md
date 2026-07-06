# Third-Party Notices

This optional package is a Phase 171 preview package. It uses the official
`foxglove-sdk` C ABI built with the `remote-access` feature.

Native build inventory captured during Task 0:

- `foxglove.dll` SHA256:
  `53BAA160A0BCB0D77132B75A74154A318CE5ABA279FAE0413ACE7F9CD9677814`
- Cargo metadata package count: 636
- License inventory source:
  `build/remotegateway/cargo-license-inventory.tsv`

Key upstream components observed in the cargo dependency closure:

- foxglove-sdk / MCAP: MIT
- LiveKit Rust SDK, LiveKit WebRTC bindings, and libwebrtc package metadata:
  Apache-2.0
- AWS-LC Rust crates: ISC plus Apache-2.0/ISC and bundled notices
- Rustls / Reqwest / Tokio family crates: MIT, Apache-2.0, or compatible
  dual-license expressions as reported by cargo metadata

Before publishing this package, regenerate the inventory from the exact native
artifact build, resolve packages with missing cargo license metadata, and copy
the required upstream notice text for Foxglove SDK, LiveKit/WebRTC, AWS-LC, and
transitive crates into this file.
