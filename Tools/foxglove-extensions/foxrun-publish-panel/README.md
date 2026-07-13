# FoxRun Publish

FoxRun Publish is a catalog-driven Foxglove panel for sending data to Unity
`SubscribeOnly` and `PublishAndSubscribe` FoxRun contracts.

The panel loads `/foxrun/subscription-contracts` from the active Unity
connection. The first request contains only deterministic contract summaries;
after a topic is selected, the panel asks Unity for that one contract's writable
fields and, for Protobuf, its descriptor. Unity remains authoritative for
subscription enablement, shared-token policy, payload size, and the per-topic
subscription rate limit.

## Wire Behavior

- JSON contracts use the Foxglove panel publishing API.
- A JSON topic is advertised once per panel session, then reused for Send once
  and Repeat; changing topics or closing the panel releases the prior
  advertisement so repeat sends cannot consume client-published channel budget.
- Protobuf contracts use a direct Foxglove WebSocket connection. The panel
  requires the selected contract's descriptor, explicitly advertises the client
  channel as `protobuf`, then sends a raw `MessageData` binary frame. It never
  falls back to JSON.
- The shared token field is memory-only. It is omitted from persisted panel
  state and never printed by the panel.
- Send confirmation is fire-and-forget. Unity diagnostics and its observed
  state remain the acceptance authority.
- Each topic retains its requested repeat rate in persisted panel state. Repeat
  sends are clamped to Unity's advertised per-topic limit. A timer tick while
  another send is in flight is skipped and counted; it is not queued.
- The direct Protobuf connection times out after 10 seconds and reports a local
  failure; it never falls back to JSON.

## Local Development

```powershell
npm install
npm test
npm run typecheck
npm run build
npm run package
```

Install the resulting `.foxe` package in Foxglove Desktop, add the **FoxRun
Publish** panel, and run Unity in Play Mode with FoxRun subscriptions enabled.
For a Protobuf contract, enter the Unity WebSocket endpoint and, when remote
access requires it, the current shared token.

For the independent binary control path used during Protobuf acceptance, run:

```powershell
python Scripts/smoke/websocket/phase176_foxrun_publish_panel_probe.py --port 8765 --value 10
```
