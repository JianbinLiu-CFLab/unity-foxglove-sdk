# Unity Cursor Bridge

This optional Foxglove extension is the Phase139D control-channel spike for
Unity replay cursor synchronization. It does not load MCAP data. Phase139B and
Phase139C keep the file-backed data path on Remote Data Loader / Remote files;
this panel only exchanges cursor metadata with a local Unity endpoint when the
user enables it.

The bridge is disabled by default. Use it only with a trusted local Unity
instance because it sends timeline cursor values to a loopback endpoint.

## Contract

- Watch `currentTime`, `startTime`, `endTime`, and `didSeek` through the
  Foxglove panel extension API.
- Send `{ sec, nsec }` as separate integer fields; do not collapse epoch
  nanoseconds into a JavaScript number.
- Send only cursor metadata, never MCAP bytes.
- Keep the send cadence bounded and ignore duplicate cursor values.
- `Forward Foxglove currentTime to Unity` sends Foxglove playhead changes to
  Unity's replay endpoint.
- `Follow Unity replay in Foxglove` polls Unity replay state and uses
  `seekPlayback` so Foxglove panels follow Unity's replay cursor while Unity is
  the current source of playback truth.
- Treat `/v1/data` range requests as Remote Data Loader cache traffic, not as a
  Unity playhead signal.

## Development

```powershell
npm install
npm run build
npm run local-install
```

The default endpoint is `http://127.0.0.1:8892/v1/replay-cursor`. Change it in
the panel UI only after the matching Unity endpoint is explicitly enabled.
