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

Bearer-token authentication is intended for Foxglove Desktop and other trusted
local clients. Browser-hosted Foxglove clients send an unauthenticated OPTIONS
preflight before the POST; Unity answers that preflight but still requires the
token on the actual cursor request.

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

Unity should follow the Foxglove playhead. Normal playback advances replay
incrementally; explicit seeks or scrubs refresh the scene from a latest-at
snapshot.

## Cursor Rate

The `Cursor rate (Hz)` field sets how often the panel POSTs a fresh playback
cursor to Unity (default 60). Lower it when Unity runs a heavy scene: fewer
cursors per second means Unity is forced to drain less often, which reduces the
chance it falls behind and has to take the expensive latest-at snapshot path.
The setting persists with the panel.

The forward path now waits for Unity to acknowledge (HTTP 202) each cursor
before sending the next one. At most one cursor POST is in flight at a time, so
Foxglove's send cadence adapts to Unity's processing speed instead of flooding
it. While waiting, only the latest cursor is sent next.

## Follow Unity Replay (Experimental)

When the installed Foxglove build exposes a programmatic `seekPlayback`, the
panel shows an extra `Follow Unity replay` toggle (default off).

**Use this toggle instead of pressing Foxglove's own play button.** The Foxglove
panel API has no play/pause control — only `seekPlayback` — so the panel cannot
take ownership of Foxglove's playback clock. Follow therefore runs its own
**internal clock**: it sends a forward "advance" cursor, waits for Unity's 202
ACK, advances the internal clock by one rate step, sends the next cursor, and so
on. Unity's ACK latency paces the whole loop, so Foxglove can never outrun Unity.

Each step advances by the real wall-clock time elapsed since the previous step
(so playback runs at ~1x regardless of ACK latency), clamped under Unity's 500 ms
seek threshold so Unity stays on its cheap forward-advance path. If Unity cannot
keep up, the loop slows gracefully instead of jumping.

The cursor stream to Unity runs at the full cursor rate, but the best-effort
`seekPlayback` that drags the Foxglove UI along is throttled (~10 Hz, the
`SEEK_UI_INTERVAL_MS` constant). Because `seekPlayback` is a *jump* (Foxglove
reloads the frame at the target time), calling it every cursor strobes the
Foxglove panels — point clouds in particular flicker. Throttling reduces that,
but some flicker is inherent to seek-driving: treat **Unity as the smooth view**
and the Foxglove panels as a coarse follow. The panel's `Replay time` readout
still advances smoothly because it shows the internal clock, not the (throttled)
Foxglove playhead. `SEEK_UI_INTERVAL_MS` is the main tuning knob — lower it for a
more continuous Foxglove UI at the cost of more frame reloads, raise it for fewer
reloads.

Because of this, Follow is mainly worth it when Unity is the bottleneck (heavy
scenes that drop frames chasing Foxglove). For light scenes, leaving Follow
**off** and driving from Foxglove's own play button gives smoother Foxglove
panels.

When follow reaches the end of the replay it parks: the loop stops and the panel
falls back to plain Foxglove-to-Unity sync, so you can scrub the timeline freely
(each scrub syncs Unity once) without playback running away. Re-check `Follow
Unity replay` to resume Unity-paced playback from the current position.

**Do not press Foxglove's play button while Follow is on.** Because there is no
pause API, Foxglove's own free-run and the follow loop would both drive time and
fight each other (the timeline gets pinned near the start). Treat Follow as the
playback driver, not an overlay on Foxglove playback. This is why the feature is
experimental.

`seekPlayback` is an undocumented panel API reached via a type cast; it may
change or disappear on Foxglove upgrades. If it is absent the toggle is hidden
and the panel behaves exactly as the default Foxglove-to-Unity sync.

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
  "mode": "advance",
  "didSeek": false
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
npm run typecheck
npm run build
npm run package
```

The generated extension artifacts are local build outputs and should not be
committed unless the release process explicitly asks for packaged artifacts.
