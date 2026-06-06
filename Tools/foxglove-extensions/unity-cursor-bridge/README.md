# Unity Replay Sync

Unity Replay Sync is an optional Foxglove Desktop panel for synchronizing a
Foxglove MCAP playback timeline with Unity replay.

It does not load MCAP files and it does not stream sensor data. The MCAP data
path stays in Foxglove Remote files. This panel only exchanges replay cursor
metadata with a trusted local Unity process through:

```text
http://127.0.0.1:8892/v1/replay-cursor
```

Use this panel when you want to open the same MCAP in Foxglove and Unity, then
use Foxglove's timeline as the single playback clock for Unity scene
reproduction. The panel sync switch is enabled by default because this panel has
only one product direction: Foxglove timeline to Unity replay.

## Requirements

- Foxglove Desktop with local extension support.
- Node.js and npm.
- Unity2Foxglove running in Play Mode with MCAP Replay enabled.
- Unity **Foxglove Timeline Replay** enabled when Foxglove should open the
  MCAP through a local URL.
- The Unity Console should show:

```text
[Foxglove] Replay cursor endpoint ready: http://127.0.0.1:8892/v1/replay-cursor
```

Hosted Foxglove Web is not the primary target for this panel. Browser security
features such as mixed-content blocking, Private Network Access, and CORS may
block direct localhost access.

## Install The Panel

From a repository checkout:

```powershell
cd Tools\foxglove-extensions\unity-cursor-bridge
npm install
npm run build
npm run local-install
```

Then restart Foxglove Desktop, or reload local extensions if your Foxglove
version exposes that action.

After installation, add a panel named `Unity Replay Sync` from Foxglove's
panel picker.

If `npm run local-install` cannot find `foxglove-extension`, use npm's local
binary runner from this folder:

```powershell
npx foxglove-extension install
```

Do not hard-code machine-local paths in shared instructions. The commands above
work from any clone of this repository.

## Open The MCAP In Foxglove

In Unity, enable `Foxglove as Replay Timeline` under
`Foxglove Timeline Replay` on the `FoxgloveManager`. The Inspector shows a
direct MCAP URL similar to:

```text
http://127.0.0.1:8891/v1/files/local-mcap.mcap
```

Use that direct `.mcap` URL in Foxglove Desktop's `Remote files` connection.
Do not use `/v1/manifest` in the Foxglove Remote files dialog; that endpoint is
for backend diagnostics and integration tests.

## Sync Foxglove Timeline To Unity

Use this when Foxglove is the playback UI.

1. In Unity, enable MCAP Replay and select the same replay file.
2. Enable `Foxglove as Replay Timeline` and copy/open the direct MCAP URL.
3. In Foxglove, open that URL through `Remote files`.
4. Add the `Unity Replay Sync` panel.
5. Keep `Sync Foxglove timeline to Unity` checked.
6. Play, pause, or scrub the Foxglove timeline.

Expected Unity evidence:

```text
[Foxglove] Replay cursor bridge received cursor from foxglove-unity-cursor-bridge ...
```

Unity should seek its replay scene toward the Foxglove playhead.

## Endpoint And Security

The default endpoint is loopback-only:

```text
http://127.0.0.1:8892/v1/replay-cursor
```

Only use this with a trusted local Unity instance. If you set a bearer token in
Unity, enter the same token in the panel. The panel sends replay cursor metadata
only:

```json
{
  "source": "foxglove-unity-cursor-bridge",
  "sequence": 1,
  "time": { "sec": 1780671016, "nsec": 434472071 },
  "mode": "seek"
}
```

It does not send MCAP bytes, scene data, or sensor payloads.

## Troubleshooting

- Panel does not appear: rerun `npm run build` and `npm run local-install`,
  then restart Foxglove Desktop.
- Foxglove reports `Failed to fetch`: confirm Unity is in Play Mode,
  `Foxglove as Replay Timeline` is enabled, and the direct `.mcap` URL is
  used.
- Unity does not receive cursors: confirm the endpoint-ready Console log,
  confirm the panel endpoint matches `http://127.0.0.1:8892/v1/replay-cursor`,
  and check Foxglove Desktop DevTools for CORS, CSP, or localhost fetch errors.
- Unity and Foxglove fight each other: Foxglove should be the timeline owner in
  this workflow. Unity disables Replay Auto Play while `Foxglove as Replay
  Timeline` is enabled.
- Do not treat Remote File `/v1/data` range requests as a playhead signal. They
  are cache, prefetch, and byte-range traffic for MCAP data.
- Python or curl POST success is not proof that the Foxglove panel works. The
  real evidence is a Unity Console log whose source is
  `foxglove-unity-cursor-bridge`.

## Development

```powershell
cd Tools\foxglove-extensions\unity-cursor-bridge
npm install
npm run build
npm run package
```

The generated extension artifacts are local build outputs and should not be
committed unless the release process explicitly asks for packaged artifacts.
