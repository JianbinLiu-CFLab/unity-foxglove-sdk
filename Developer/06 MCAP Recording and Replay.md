# 1. MCAP Recording and Replay

MCAP (Message Capture) is a log file format for robotics data. The SDK supports recording real-time data from a WebSocket session into `.mcap` files and replaying those files in Unity.

## 1.1 Purpose

Use this guide to capture Unity/Foxglove traffic into MCAP files and replay those files later.

## 1.2 Application

Use MCAP when you need repeatable debugging, offline analysis, regression evidence, or a shareable capture of a Unity session.

## 1.3 Recording

### 1.1.1 Inspector configuration

In the FoxgloveManager component's **MCAP Recording** section:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Enable Recording` | `bool` | `false` | Check to enable recording |
| `Recording Prefix` | `string` | `"foxglove"` | Filename prefix |
| `Recording Directory` | `string` | `""` | Leave empty to save to `<project>/Recordings/`; specify a custom path |
| `Recording Chunk Size KB` | `int` | `1024` | Chunk size in KB. Data is written to an in-memory buffer first, then flushed to disk when full. |
| `Recording Compression` | enum | `None` | Compression algorithm: `None`, `Lz4`, `Zstd` |

### 1.1.2 Output files

Recording file naming format: `{prefix}_{yyyyMMdd_HHmmss}.mcap`

Example: `Recordings/foxglove_20260505_143021.mcap`

### 1.1.3 Code configuration

```csharp
var manager = FindFirstObjectByType<FoxgloveManager>();
// Set before calling StartServer
// manager.EnableRecording is controlled via serialized Inspector fields;
// no additional code is needed

// Or control directly via FoxgloveRuntime:
manager.Runtime.EnableRecording(
    filePath: "D:/recordings/my_session.mcap",
    chunkSizeBytes: 1024 * 1024,    // 1 MB
    compression: "lz4",
    coordinateMode: "LeftHand"
);
```

Advanced API-only writer options are also available for tests, tooling, and compatibility fixtures. These do not add Inspector fields and do not change default Unity recording behavior:

```csharp
var options = new McapWriterOptions
{
    UseChunking = false,
    IndexTypes = McapIndexTypes.None,
    RepeatSchemas = false,
    RepeatChannels = false,
    UseStatistics = false,
    UseSummaryOffsets = false,
    EnableCrcs = true,
    EnableDataCrcs = true
};

manager.Runtime.EnableRecording(
    filePath: "D:/recordings/direct_layout.mcap",
    options: options,
    coordinateMode: "LeftHand"
);
```

The advanced options align with official MCAP writer knobs for chunking, index groups, repeated summary records, statistics, summary offsets, chunk compression, and CRC fields. Use them when generating compatibility evidence or specialized MCAP layouts; use Inspector defaults for normal Unity recording.

### 1.1.4 Compression

| Mode | Library | Description |
|------|---------|-------------|
| `None` | none | No compression; fastest writes |
| `Lz4` | `IonKiwi.lz4.dll` | LZ4 block compression, fast, moderate compression ratio |
| `Zstd` | `ZstdSharp.dll` | Zstandard compression, moderate speed, high compression ratio |

Compression DLLs are located in `Runtime/Plugins/compression/`.

### 1.1.5 Coordinate mode

The `coordinate_mode` metadata (`"LeftHand"` or `"RightHand"`) is written at recording time and automatically restored at replay time.

## 1.2 Replay

### 1.2.1 Inspector configuration

In the FoxgloveManager component's **MCAP Replay** section:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Enable Replay` | `bool` | `false` | Check to enable replay |
| `Replay File Path` | `string` | `""` | Full path to the .mcap file |
| `Replay Auto Play` | `bool` | `false` | Auto-start playback after loading |
| `Disable Live Publishers` | `bool` | `true` | Disable scene live publishers when replay starts |

### 1.2.2 Recording and replay mutual exclusion

When both `Enable Recording` and `Enable Replay` are enabled simultaneously, recording takes priority and replay is automatically disabled. The Console will show:
```
[Foxglove] Recording and Replay cannot both be enabled. Disabling Replay.
```

### 1.2.3 Live publisher disabling

When `Disable Live Publishers = true`, replay start automatically disables all `FoxglovePublisherBase` subclass components in the scene to prevent duplicate publishing of live and replay data. They are restored on Stop.

### 1.2.4 Replay controls

Control via the `McapReplayEngine` API:

| Method | Description |
|--------|-------------|
| `Play()` | Start/resume playback. Restarts from the beginning if already ended. |
| `Pause()` | Pause playback, keep current position |
| `Seek(ulong timeNs)` | Jump to a specific time point (nanoseconds) |

State enum: `Playing`, `Paused`, `Buffering`, `Ended`

### 1.2.5 Replay events

The `FoxgloveManager.OnReplayMessage` event fires when each replay message is consumed:

```csharp
manager.OnReplayMessage += (topic, data) =>
{
    // Process replay data
    var json = Encoding.UTF8.GetString(data);
    Debug.Log($"Replay: {topic} => {json}");
};
```

## 1.3 Recording contents

The MCAP file contains the following data types:

| Type | MCAP record | Description |
|------|-------------|-------------|
| Topic messages | Schema + Channel + Message | All structured data published via `PublishJson` |
| Parameter snapshots and changes | Metadata record | JSON snapshot with `"parameters"` key |
| Connection graph snapshots | Metadata record | Topology snapshot with `"connection_graph"` key |
| Client publish | Channel + Message | Data published via ClientPublish |

### 1.3.1 Metadata records

Each metadata entry is identified by `name`, with `value` as a JSON string:

- `coordinate_mode` -- `"LeftHand"` or `"RightHand"`
- `parameter_change` -- parameter change events
- `connection_graph_snapshot` -- connection graph snapshot

## 1.4 Playing MCAP in Foxglove

1. Open Foxglove Desktop
2. File > **Open local file...** (or drag the .mcap file directly into the window)
3. Select the recording file

### 1.4.1 Playback controls

| Action | Method |
|--------|--------|
| Play / Pause | Bottom control bar or spacebar |
| Drag timeline | Drag the bottom timeline handle with the mouse |
| Speed up / slow down | Adjust playback speed in the bottom control bar |
| Loop | Toggle loop mode in the bottom control bar |

### 1.4.2 Available panels during playback

- **3D panel**: displays recorded scene entities and Transforms
- **Plot panel**: view recorded numerical values over time
- **Raw Messages panel**: view raw JSON of recorded messages
- **Image panel**: view recorded camera frames

Playing MCAP files does not require Unity to be running; Foxglove reads and renders them independently.

## 1.5 Important notes

- **Do not interrupt recording**: The `Close()` method is responsible for writing the MCAP footer and summary section. If Unity exits abnormally, the footer is not written and the file becomes unreadable. Stopping Play normally triggers `OnDestroy` > `StopServer()` to ensure proper shutdown.
- **Chunk Size**: Smaller chunk values provide finer-grained indexing but increase file overhead. The default 1024 KB (1 MB) is suitable for most scenarios.
- **Compression**: Lz4 is suitable for real-time recording (low CPU overhead). Zstd is better for archiving (higher compression ratio but slightly slower).
- **Large files**: MCAP supports random access; the entire file does not need to be loaded into memory at once.
