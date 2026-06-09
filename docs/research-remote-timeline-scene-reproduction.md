# Remote Timeline-Controlled Scene Reproduction for Unity-Based Telemetry Replay

**Draft Research Note - Unity2Foxglove Project. First drafted 2026-05-12; updated 2026-06-09.**

## 1 Introduction

Telemetry replay is a standard capability in robotics and simulation tooling. Systems such as ROS 2 `rosbag2_transport`, Foxglove Studio, and Rerun allow developers to record timestamped message streams and play them back for analysis. During ordinary forward playback, a single ordered message stream can serve multiple consumers. During paused scrubbing, however, the consumers diverge in what they need from the same replay timestamp.

This note describes the replay architecture behind Unity2Foxglove's paused-scrub behavior. The central problem is not merely playing an MCAP file. It is allowing a remote Foxglove client to control a replay timeline while an external Unity scene reproduces the corresponding simulation state without mixing stale live telemetry, replay data, and panel reset traffic.

The key observation is that paused scrubbing is not ordinary playback at a different speed. It is a state-reproduction query initiated by a remote client.

Unity2Foxglove addresses this by separating three responsibilities: protocol state broadcast, latest-at scene reproduction, and ordered replay/panel data delivery. Full continuous range history is available when Foxglove opens the MCAP through the Remote files path; bounded WebSocket history remains available for replay sessions that do not use that file-backed path. This separation produces a replay loop where Foxglove is not only a viewer of MCAP data, but also a controller of the Unity replay timeline.

## 2 Problem

A typical telemetry replay system has one timeline and several consumers. Unity and Foxglove panels want different things from the same replay timestamp:

- The Unity scene wants the latest state at time *T* (a latest-at query).
- A time-series panel wants a range of samples around or before time *T* (a range query).
- The WebSocket protocol wants a coherent playback-state transition so panels can reset their local state.
- The transport queue wants stale data removed so old frames do not arrive after the seek.

Using one undifferentiated replay stream for all of these consumers leads to observable failure modes:

- Unity applies scene updates from a non-main thread.
- Old live publisher frames interleave with replay frames.
- Data messages arrive with log times older than the client has already processed.
- Foxglove panels report "data went back in time."
- Paused scrubbing can clear panels or destabilize the client.
- Replay can work while playing forward but fail when the user drags backwards.

## 3 Related Work

### 3.1 Rerun: Latest-At and Range Access

Rerun [1, 2] distinguishes latest-at and range-style access patterns as first-class concepts in its data model. When no visual time range applies, views use latest-at semantics: starting from the time cursor, the viewer queries the latest available data for each component type. Rerun's chunk store documentation exposes separate latest-at and range relevant-chunk query paths. This distinction is highly relevant to Unity2Foxglove: Unity scene reproduction corresponds to latest-at access, and Foxglove Plot reconstruction corresponds to range access. However, Rerun applies this distinction inside its own viewer architecture. It does not drive an external 3D engine such as Unity.

### 3.2 MCAP

MCAP [3, 4] provides the timestamped, multi-channel log container used by Unity2Foxglove replay. MCAP is an open container format for multimodal log data, supporting pre-serialized data across channels, schemas embedded alongside messages, optional chunk indexes for efficient seeking, and LZ4/Zstandard chunk compression. Unity2Foxglove builds on these MCAP properties rather than replacing them. The contribution here is not a new file format; it is the control and consumption architecture around MCAP replay inside Unity.

### 3.3 Foxglove PlaybackControl Protocol

Foxglove's PlaybackControl capability [5, 6, 7] lets the Foxglove UI control an external WebSocket server that owns playback. Foxglove sends play, pause, seek, and speed changes; the application loads data, handles requests, advances time, and returns updated playback state so the UI stays synchronized. The SDK `PlaybackState` frame includes a `did_seek` field [8], and Foxglove panel render state exposes `didSeek` so panels can clear stale state when data may have been skipped [9]. Unity2Foxglove uses this protocol directly. The important difference is that Unity2Foxglove is not only streaming data back to Foxglove — it also uses the same replay control path to reproduce a Unity scene from MCAP state.

### 3.4 Dexory foxglove_mcap_player

Dexory's `foxglove_mcap_player` [11] is a ROS 2 node that plays MCAP files with dual output: a Foxglove WebSocket server with playback controls, and ROS 2 topic republishing to original topics. This is an important precedent for dual-output replay. The boundary is that Dexory republishes the same ordered message stream to both consumers. It does not separate latest-at scene reconstruction from panel streaming.

### 3.5 ROS Foxglove Bridge

The ROS Foxglove bridge [12, 13] is a high-performance C++ WebSocket bridge for ROS 1/ROS 2. It is a useful contrast for Unity2Foxglove as a whole: the Foxglove bridge is an external ROS bridge process, while Unity2Foxglove is an in-process Unity package where the bridge, playback controller, MCAP reader, scene adapter, and runtime publishers are all inside Unity.

### 3.6 ROS 2 Replay and rosbag2_transport

ROS 2's `rosbag2_transport` Player supports remote-control services including seek-related playback control [14, 15]. Its replay model moves through recorded topic data and republishes messages through the ROS graph. It does not perform latest-at scene reconstruction for an external Unity scene at the seek target. ROS replay testing packages [16, 17] demonstrate the importance of replay-driven development, but their primary target is ROS node execution rather than remote timeline-controlled Unity scene reproduction.

### 3.7 Isaac Sim and USD + ROS Bag Workflows

Isaac Sim's ROS 2 Bridge and simulation-control documentation [18, 19] show a related reproducibility pattern: recorded topic data, ROS bridge state, and saved scene/world descriptions all matter for simulation context. A separate workflow combining ROS 2 bags with USD scenes [20] makes that relationship explicit. The similarity is conceptual — recorded data alone is not enough; scene context matters. The difference is operational: Unity2Foxglove performs seek-time scene reproduction in a live Unity process controlled by Foxglove over WebSocket.

### 3.8 Unity Replay Systems

Unity-specific replay systems such as commercial asset-store tools and open-source replay frameworks demonstrate that scene replay and state reproduction are established Unity needs. They usually record Unity-side component state or deterministic inputs and then replay them inside Unity. Unity2Foxglove differs in both data source and control surface: the source of truth is MCAP telemetry, Foxglove controls the replay timeline remotely, and the same replay session must remain coherent for WebSocket clients.

### 3.9 Comparison

| Feature | Rerun | Dexory | Foxglove native | Foxglove bridge | Unity replay systems | rosbag2 / ROS replay testing | Unity2Foxglove |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MCAP data source | Partial | Yes | Yes | No | No | Yes | Yes |
| External Unity scene reproduction | No | No | No | No | Yes (Unity-local) | No | Yes |
| Latest-at scene state | Yes (viewer) | No | Client-local | No | Snapshot-like | No | Yes |
| Range panel data | Yes | Ordered stream | Client/file | Live stream | Usually no | ROS topic stream / assertions | Full curve through Remote files; bounded settled-scrub history through WebSocket |
| Dual output (3D engine + panels) | No | Yes | No | ROS to Foxglove | Unity-only | ROS nodes | Yes |
| Remote timeline controls | Viewer-local | PlaybackControl | Client controls | No | Unity-local | CLI/test-runner | PlaybackControl |
| Multi-client state broadcast | N/A | Partial | Client-local | No | No | No | Yes |
| Stale live/replay queue separation | N/A | Ordered stream | Client/file | No | No | No | Yes |

## 4 Design Principle

The design principle is:

> Separate scene reproduction from telemetry streaming.

Unity scene reproduction is a latest-at operation. It asks: what should the scene look like at time *T*?

Telemetry streaming is an ordered-message operation. It asks: what messages should subscribers receive as playback time advances?

Foxglove panel history is a range operation. It asks: what data points should an analytical panel have in its local window?

These three operations are related, but they should not be forced through the same code path.

```mermaid
flowchart TD
  Foxglove["Foxglove timeline"] --> Control["Playback control request"]
  Control --> Runtime["Unity runtime tick"]
  Runtime --> Clock["Playback clock"]
  Runtime --> Replay["MCAP replay controller"]
  Replay --> SceneSnapshot["Latest-at scene snapshot"]
  Replay --> Stream["Ordered replay stream"]
  SceneSnapshot --> UnityScene["Unity scene reproduction"]
  Stream --> FoxglovePanels["Foxglove panels"]
  Runtime --> State["PlaybackState broadcast"]
  State --> FoxglovePanels
```

Phase139D adds a separate control path for Foxglove-owned Remote files replay:

```mermaid
flowchart TD
  FoxgloveCursor["Foxglove cursor (up to 60 Hz)"] --> CursorBridge["Unity Replay Sync panel"]
  CursorBridge --> ExternalCursor["ExternalReplayCursorController"]
  ExternalCursor --> ShouldSeek{"Explicit seek, backwards motion,\nor large jump?"}
  ShouldSeek -- Yes --> SeekPath["Latest-at scene snapshot"]
  ShouldSeek -- No --> AdvancePath["Uncapped scene advance to cursor"]
  SeekPath --> UnityScene["Unity scene"]
  AdvancePath --> UnityScene
```

The contrast with a single-stream replay loop is the important architectural boundary:

```mermaid
flowchart LR
  subgraph SingleStream["Single-stream replay"]
    SSeek["Seek / scrub request"] --> SReplay["Ordered replay stream"]
    SReplay --> SUnity["Unity scene"]
    SReplay --> SPanels["Foxglove panels"]
    SOld["Stale queued frames"] -.-> SUnity
    SOld -.-> SPanels
    SPanels --> SWarn["Backwards-time warnings / unstable panels"]
  end

  subgraph SplitPath["Split-path replay"]
    PSeek["Seek / scrub request"] --> PTick["Unity runtime tick"]
    PTick --> PState["PlaybackState + didSeek broadcast"]
    PTick --> PSnapshot["Latest-at scene snapshot"]
    PTick --> PQueue["Clear stale data queue"]
    PTick --> PHistory["Settled bounded history"]
    PSnapshot --> PUnity["Unity scene"]
    PHistory --> PPanels["Foxglove panels"]
    PState --> PPanels
  end
```

## 5 Architecture

### 5.1 Playback Control Serialization

Playback-control requests arrive from the WebSocket receive path. They are not applied directly on that thread. Instead, requests are queued and drained by the Unity runtime tick. This ensures that seek, pause, play, replay cursor mutation, and scene snapshot application are serialized with Unity's main update loop. This matters because Unity scene objects cannot be safely mutated from transport threads.

### 5.2 Broadcast Playback State

When a client seeks, the resulting playback state is broadcast to all connected clients rather than only returned to the initiating client. This keeps multiple Foxglove panels or clients aligned on the same `currentTime`, `status`, and `didSeek` transition. The request identifier is primarily meaningful to the initiating client, but the state transition itself is global for the replay session.

### 5.3 Queue Reset Before Replay Resume

Seek changes invalidate stale queued data frames. Before replay data resumes, data-priority queues are cleared. This prevents old MessageData frames from arriving after Foxglove has already reset its playback state. Reliable control frames and droppable data frames therefore have different roles: control frames preserve protocol state; data frames may be discarded during seek reset.

### 5.4 Scene Snapshot Path

Paused seek applies a latest-at MCAP snapshot to Unity scene listeners. This path is scene-only: it does not publish the snapshot as Foxglove MessageData. This is the key reproduction behavior. The Unity scene follows the Foxglove timeline even while playback is paused.

### 5.5 Active Scrub and Settled State

Paused scrubbing has two phases:

- **Active drag phase:** every seek command updates playback state and Unity scene state. Panel history is suppressed. Plot may remain empty or stale during this phase.
- **Settled phase:** after a debounce window expires with no newer seek, the SDK sends coherent bounded panel history for the settled time and then parks Foxglove time at the requested seek time. In the Phase139C/D Remote files workflow, Foxglove reads the complete MCAP history directly, so Plot can show a continuous curve without relying on this WebSocket history path.

This avoids flooding the WebSocket client while the user is still dragging and prevents transient backwards-time warnings.

### 5.6 Playback Resumption

When playback resumes, panel-history delta state is invalidated. The next paused seek should not assume that the previous paused history window is still a valid basis for a delta update. Unity-local Play, Pause, Seek, and replay-disable operations also clear external cursor state so an older Foxglove cursor cannot interfere after Unity resumes local control.

## 6 Current Semantics

The current implementation supports the following user-facing behavior:

- Forward playback drives Unity and Foxglove normally.
- Paused seek updates the Unity scene to the requested time.
- Backward paused seek does not produce Foxglove "data went back in time" warnings.
- Foxglove remains connected and stable during repeated paused scrubbing.
- In the Phase139C/D Foxglove Timeline Replay workflow, Plot shows a continuous curve because Foxglove reads the full MCAP file directly.
- In WebSocket-only replay, Plot receives bounded settled-scrub history rather than the complete file-backed curve.
- Pressing Play after paused seek resumes replay successfully.

Scene reproduction applies recorded telemetry state to Unity objects. It does not re-simulate physics, user input, random state, gameplay logic, or other nondeterministic systems from the original run.

This is a deliberate semantic boundary. Unity scene reproduction remains a latest-at operation even when Foxglove independently has access to the full file-backed history.

## Phase139C Remote Data Loader Workflow

Phase139C validates the file-backed analysis path separately from Unity live
replay. The Phase139B HTTP backend still exposes the Remote Data Loader contract
through `/v1/manifest` and `/v1/data`, but Foxglove's stock **Remote files**
dialog expects a URL that ends with a filename and extension. For manual
Foxglove acceptance, use the backend's direct `.mcap` file URL. This is the path
for inspecting continuous Plot curves and 3D/image panels from recorded file
data.

In Unity, the product entry point is the Manager Inspector:

1. Select `FoxgloveManager`.
2. Expand `MCAP Record & Replay`.
3. Set `Replay File Path` to the recording.
4. Expand `Foxglove Timeline Replay`.
5. Enable `Foxglove as Replay Timeline` and use `Copy Foxglove URL` or
   `Open in Foxglove`.

The copied URL is the direct file route:

```text
http://127.0.0.1:8891/v1/files/local-mcap.mcap
```

`Open in Foxglove` opens a Foxglove Desktop shareable link with
`ds=remote-file` and `ds.url=<direct-mcap-url>`, so the Remote files data
source is selected without manually pasting the URL.

Foxglove Timeline Replay opens recorded data in Foxglove and makes Foxglove the
owner of replay time for this workflow. Unity remains a scene reproduction
follower: it serves the selected MCAP file through a local URL, starts the
cursor endpoint, and applies Foxglove cursor updates to Unity replay.

The Inspector first tries a `foxglove` executable on `PATH`, then the installed
Foxglove Desktop executable, and finally falls back to copying/opening the URL.
The separate command-line server remains useful for script debugging or when
Unity is not running:

```powershell
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase139b-remote-data-loader-server --mcap "Unity2Foxglove/Recordings/foxglove_20260605_144901_2666478Z.mcap" --port 8891
```

When connecting manually, use **Open connection -> Remote files** and paste the
same direct `.mcap` URL.

Do not paste `/v1/manifest` into the stock Remote files dialog. That dialog
validates that the URL must end with a filename and extension, so the manifest
URL is intentionally kept as a backend contract and script probe endpoint rather
than the manual UI entry point.

The expected manual evidence is:

- Foxglove accepts the direct `.mcap` URL and lists topics from the recording,
  such as `/imu/data`, `/tf`, and
  point cloud or camera topics that exist in the selected recording.
- A Plot panel shows a continuous curve from the file-backed history after the
  recording is loaded.
- Dragging or scrubbing the Foxglove timeline localizes the Plot cursor within
  the loaded range.
- 3D and image panels render from file data without requiring Unity Play Mode.

The helper script verifies the backend endpoints and writes machine-readable
evidence for the manual Foxglove pass:

```powershell
python Scripts/smoke/phase139c_dataloader_cursor_acceptance.py --mode curve-only --mcap "Unity2Foxglove/Recordings/foxglove_20260605_144901_2666478Z.mcap" --json-out build/phase139c/manual.json
```

The script checks both the contract endpoints (`/v1/manifest` and `/v1/data`)
and the direct Remote files compatibility endpoint
`/v1/files/local-mcap.mcap` with a byte-range MCAP magic read.

`/v1/data` accepts time-range requests and uses `RemoteMcapRangeWriter` to
produce an MCAP slice within the configured in-memory response cap. The direct
`.mcap` route instead supports byte-range streaming for Foxglove Remote files
and large-file access.

Remote Data Loader `/v1/data` range requests are cache and prefetch requests,
not a reliable signal for the current Foxglove playhead. Unity scene replay
should continue to use the local replay controls and the live WebSocket
playback-control path. A future playback-sync feature must provide a dedicated
control transport rather than inferring cursor state from Remote files traffic.

## Phase139D Unity Replay Sync Boundary

Phase139D records a separate control channel for Foxglove-owned replay. The
product workflow is named **Foxglove Timeline Replay**: Foxglove controls replay
time from a Remote File source, and Unity follows the Foxglove timeline by
applying cursor updates to scene replay. The data path remains Phase139B/139C:

```text
MCAP -> Phase139B HTTP backend -> Foxglove Remote files
```

The control path is intentionally separate:

```text
Foxglove extension currentTime -> bounded loopback cursor message -> Unity replay advance or seek
```

Do not infer Unity cursor state from `/v1/data`. Those requests are Remote Data
Loader cache, range, and prefetch traffic. They can appear ahead of, behind, or
independent from the visible playhead.

The Phase139D extension scaffold follows the Foxglove panel extension contract:
call `context.watch("currentTime")` and read `renderState.currentTime` from
`context.onRender`. It also watches `startTime`, `endTime`, and `didSeek` so a
Unity endpoint can distinguish timeline bounds, smooth playback advances, and
explicit seek events. Cursor time is sent as separate `{ sec, nsec }` fields to
avoid JavaScript integer precision loss. The cursor payload also carries
optional `startTime` and `endTime` values so Unity can validate the timeline
bounds reported by Foxglove.

The prototype bridge originally explored both directions. The product path now
keeps only the Foxglove -> Unity direction because it gives users one visible
timeline owner. The reverse Unity -> Foxglove follow path is not retained as a
product feature; it makes Unity and Foxglove compete to explain playback state.

Unity exposes the cursor endpoint only when Foxglove Timeline Replay is enabled.
The Foxglove panel sync switch is enabled by default because the panel has a
single product direction: Foxglove timeline -> Unity replay. Its first target is
a trusted local loopback endpoint, with origin/token restrictions before broader
browser access. It must send only cursor metadata, never MCAP data, and it must
coalesce rapid updates so Unity handles cursor work on the main runtime tick
rather than on an endpoint thread. During smooth playback, Unity advances replay
incrementally through due MCAP messages; only explicit seeks, backwards motion,
or large timeline jumps use the latest-at scene snapshot path.

The panel targets a maximum cursor cadence of 60 Hz. This is a clock-sync
cadence, not a telemetry sampling rate: Unity still processes every replay
message in `(lastCursor, currentCursor]`, including messages from 100 Hz and
higher-rate topics. The earlier 20 Hz cadence made Unity scene motion visibly
choppy while Foxglove playback remained smooth.

The current cursor sender is fire-and-forget and replaces an older in-flight
request when a newer cursor is ready. This bounds stale work differently from a
concurrent request backlog, but it does not provide response-aware
backpressure. A stronger follow-up design is single-flight, latest-wins
delivery: keep at most one request in flight, retain only the newest pending
cursor, and send that cursor when Unity responds. A simple busy-time drop is
insufficient because it can lose the final cursor when Foxglove pauses.

Foxglove remains the single timeline owner. The current panel extension API
observes playback state but does not expose a supported playback-rate control,
so Unity cannot ask Foxglove to slow its visible timeline without reintroducing
an unsupported reverse-control path.

The same loopback endpoint supports `GET /v1/replay-cursor` for diagnostics and
smoke tests. Its state includes replay availability, play/end state, speed,
current time, and timeline bounds. The GET route is observational only; it is
not a Unity-to-Foxglove synchronization path.

### Phase139D Implemented Replay Components

| Component | Responsibility |
| --- | --- |
| `ReplaySnapshotStateMachine` | Separates pending scene snapshots from panel snapshots |
| `ReplayOrchestrator` | Coordinates replay lifecycle and subsystem operations |
| `ReplayPoseOwnershipArbiter` | Resolves replay pose ownership between recorded channels |
| `ReplayCoordinateModeGuard` | Detects coordinate-mode incompatibility |
| `ReplaySchemaGuard` | Detects recorded/current FoxRun schema mismatch |
| `ReplayChannelBehavior` | Classifies replay channel behavior |
| `FoxgloveReplayObjectAdapter` | Applies replay messages to Unity scene objects |
| `RemoteMcapRangeWriter` | Produces time-range MCAP slices for `/v1/data` |
| `RemoteMcapHttpOptions` | Configures the local Remote files HTTP service |

## 7 Validation Evidence

The implementation is covered by runtime validation and manual Foxglove acceptance.

Automated checks include:

- Playback-control requests are queued and drained on runtime tick.
- Runtime drains playback controls before advancing replay time.
- Playback seek broadcasts `didSeek` state to all connected clients.
- Paused seek applies a scene-only latest-at snapshot.
- Active paused scrub suppresses panel history before the settled debounce.
- Superseded paused scrub requests cancel the older pending history operation.
- Play invalidates paused history delta state.
- Replay mode suppresses live publisher frames and live channel advertisements.
- Replay MessageData uses the clearable data-priority path.

Recent validation results:

- Replay validation passed.
- Full runtime validation passed.
- Release package validation passed.
- Manual Foxglove testing confirmed no warnings during paused backward scrub.

**Implementation status note:** The checks listed above reflect currently implemented behavior. Full continuous Plot reconstruction is available through the Phase139C/D Remote files path. Large-MCAP scrub latency optimization and response-aware cursor backpressure remain follow-up evidence items.

## 8 Contribution

Unity2Foxglove introduces a WebSocket-controlled replay architecture for Unity scenes backed by MCAP data. The contribution is a **compositional systems contribution**: it does not invent MCAP playback, Foxglove playback controls, Unity scene updates, or latest-at queries, but combines them into a Unity-native replay path where:

- a remote Foxglove timeline can seek the Unity replay state,
- playback-control requests are serialized onto Unity's runtime tick,
- scene reproduction is driven from MCAP latest-at snapshots,
- replay output is separated from live publisher output,
- WebSocket `didSeek` state is broadcast to connected clients,
- queued stale data is cleared before replay data resumes,
- paused scrub no longer causes "data went back in time" warnings.

The main contribution boundary is not "Unity2Foxglove is the first replay system." It is narrower and more specific: Unity2Foxglove applies remote WebSocket playback control to an in-process Unity MCAP replay server and separates latest-at scene reproduction from replay streaming sufficiently to support stable paused backward scrubbing.

The current completed claims are **Unity scene reproduction** and **full historical panel reconstruction through Foxglove Remote files**. WebSocket-only replay intentionally provides bounded settled-scrub history rather than reimplementing Foxglove's native file playback. Unity2Foxglove does not claim that all large-MCAP performance cases are solved or that it is the first system to replay a 3D scene from telemetry.

## 9 Future Work

The current implementation includes bounded server-push history for settled
paused scrubs and full continuous Plot history through the Remote files path.
Remaining research and engineering work is narrower:

- Implement and benchmark response-aware, single-flight, latest-wins cursor
  delivery so slow Unity frames cannot cause repeated request cancellation.
- Measure and optimize large-MCAP seek and scrub latency.
- Decide whether WebSocket-only replay needs richer history beyond its current
  bounded settled-scrub window.
- Evaluate whether future Foxglove panel APIs can expose timeline-rate control
  without reintroducing a second timeline owner.

## 10 Conclusion

This note describes a replay architecture that separates remote timeline control, Unity scene reproduction, and Foxglove panel data delivery. The architecture uses the latest-at/range distinction, established in systems such as Rerun, as an architectural boundary between scene state and analytical panel history, applied to a remote WebSocket-controlled Unity replay loop.

The current system demonstrates stable paused scrubbing without backwards-time warnings, bounded server-push history for settled panel updates, and full continuous Plot reconstruction through Remote files. Large-MCAP scrub latency optimization and response-aware cursor backpressure remain future work. The contribution is best understood as a compositional systems contribution: mature pieces exist in neighboring systems, but the reviewed public material did not identify this combination as a documented Unity-based telemetry replay architecture.

## References

[1] Rerun Contributors. "VisibleTimeRanges." Rerun documentation. https://ref.rerun.io/docs/python/0.26.1/common/blueprint_archetypes/

[2] Rerun Contributors. "re_chunk_store." Rerun Rust API documentation. https://docs.rs/rerun/latest/rerun/external/re_chunk_store/index.html

[3] MCAP Contributors. "MCAP." https://mcap.dev/

[4] MCAP Contributors. "MCAP Format Specification." https://mcap.dev/spec

[5] Foxglove Technologies. "Connect Foxglove to your local player with PlaybackControl." Foxglove Blog, 2026. https://foxglove.dev/blog/connect-foxglove-to-your-local-player-with-playback-control

[6] Foxglove Technologies. "WebSocket Server: Playback control." Foxglove Documentation. https://docs.foxglove.dev/docs/sdk/websocket-server

[7] Foxglove Technologies. "Playback." Foxglove Documentation. https://docs.foxglove.dev/docs/visualization/playback

[8] Foxglove Technologies. "PlaybackState source." Foxglove Rust SDK documentation. https://docs.rs/foxglove/latest/src/foxglove/websocket/ws_protocol/server/playback_state.rs.html

[9] Foxglove Technologies. "RenderState." Foxglove Extension API documentation. https://docs.foxglove.dev/docs/extensions/extension-api/type-aliases/RenderState

[10] Foxglove Technologies. "foxglove/ws-protocol." Archived GitHub repository. https://github.com/foxglove/ws-protocol

[11] Dexory / BotsAndUs. "foxglove_mcap_player." GitHub repository. https://github.com/botsandus/foxglove_mcap_player

[12] Foxglove Technologies. "ROS Foxglove bridge." Foxglove Documentation. https://docs.foxglove.dev/docs/connecting-to-data/ros-foxglove-bridge

[13] Foxglove Technologies. "ros-foxglove-bridge." GitHub repository. https://github.com/foxglove/ros-foxglove-bridge

[14] ROS 2 Contributors. "rosbag2." GitHub repository. https://github.com/ros2/rosbag2

[15] ROS Index. "rosbag2_transport package." https://index.ros.org/p/rosbag2_transport/

[16] ROS Index. "replay_testing package." https://index.ros.org/p/replay_testing/

[17] Polymath Robotics contributors. "replay_testing." GitHub repository. https://github.com/polymathrobotics/replay_testing

[18] NVIDIA. "Isaac Sim ROS 2 Bridge." https://docs.isaacsim.omniverse.nvidia.com/5.0.0/py/source/extensions/isaacsim.ros2.bridge/docs/index.html

[19] NVIDIA. "Isaac Sim ROS2 Simulation Control." https://docs.isaacsim.omniverse.nvidia.com/5.1.0/ros2_tutorials/tutorial_ros2_simulation_control.html

[20] Champion3D. "Combining ROS 2 Bag Files with USD Scenes." 2025. https://www.champion3d.io/ros-2/combining-ros-2-bag-files-with-usd-scenes

## Evidence Scope

This document records the replay design represented by the current Unity2Foxglove repository documentation. Future versions may add a more complete related-work review, precise citations, benchmark data, screenshots or videos from the Foxglove/Unity replay workflow, and a clearly versioned implementation artifact.
