# MCAP Remote Data Source Boundary

Phase 119 documents a local prototype boundary, not a production hosted feature.

## Modes

| Mode | What Foxglove expects | Unity2Foxglove stance |
|---|---|---|
| Static/direct MCAP URL | A user opens or links one `.mcap` file directly. | Supported externally by Foxglove local data workflows; Unity2Foxglove already writes local `.mcap` files. |
| Remote Data Loader style backend | A data backend exposes a manifest endpoint and one or more data endpoints that return MCAP bytes. The manifest endpoint is the authorization source of truth and must not return data endpoint URLs for unauthorized sources. | Phase 119 models this locally with DTOs, a mapper, bearer-token denial, and exact-byte data responses. |
| Remote Access Gateway | Live device visualization and teleoperation through a device gateway connected to Foxglove. | Out of scope for historical MCAP replay/data-source work. |

## What Phase 119 Proves

- `McapDataLoader.Initialize()` can be mapped into a manifest-style source with deterministic topic and schema ordering.
- A local prototype can deny unauthorized manifest requests without exposing a data URL.
- A local prototype can return exact MCAP bytes for an authorized source id.
- Returned bytes remain readable by `McapReader`, `McapIndexedReader`, and `McapDataLoader`.
- Indexed chunked MCAP and summary-less direct-message MCAP files both map through the same boundary.

## What Phase 119 Does Not Prove

- No production Foxglove Remote Data Loader deployment.
- No Kubernetes, Helm, cache bucket, cloud object storage, or remote range serving.
- No OAuth, device-token provider, organization permission model, or credential storage.
- No Remote Access Gateway implementation.
- No multi-file timeline merge.

## References

- Foxglove Remote Data Loader: https://docs.foxglove.dev/docs/visualization/connecting/cloud-data/remote-data-loader
- Foxglove Local Data: https://docs.foxglove.dev/docs/visualization/connecting/local-data
- Foxglove Remote Access: https://docs.foxglove.dev/docs/visualization/connecting/live/remote-access
