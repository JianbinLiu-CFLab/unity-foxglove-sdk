# 1. MCAP 录制与回放

MCAP（Message Capture）是一种用于机器人数据的日志文件格式。SDK 支持将 WebSocket 会话中的实时数据录制为 `.mcap` 文件，并支持在 Unity 中回放这些文件。

## 1.1 目的

这份文档用于说明如何把 Unity/Foxglove 会话录制成 MCAP 文件，并在之后回放。

## 1.2 应用场景

当你需要可重复调试、离线分析、回归证据，或把一次 Unity 运行过程分享给其他人时，使用 MCAP。

## 1.3 录制

### Inspector 配置

在 FoxgloveManager 组件的 **MCAP Recording** 区域：

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enable Recording` | `bool` | `false` | 勾选后启用录制 |
| `Recording Prefix` | `string` | `"foxglove"` | 文件名前缀 |
| `Recording Directory` | `string` | `""` | 留空保存到 `<项目>/Recordings/`；指定则使用该路径 |
| `Recording Chunk Size KB` | `int` | `1024` | 每块大小（KB），数据先写入内存缓冲，满后写盘 |
| `Recording Compression` | enum | `None` | 压缩算法：`None`、`Lz4`、`Zstd` |

### 输出文件

录制文件命名格式：`{prefix}_{yyyyMMdd_HHmmss}.mcap`

示例：`Recordings/foxglove_20260505_143021.mcap`

### 代码配置

```csharp
var manager = FindFirstObjectByType<FoxgloveManager>();
// 在 StartServer 前设置
// manager.EnableRecording 通过 Inspector 序列化字段控制，
// 无需额外代码

// 或通过 FoxgloveRuntime 直接控制：
manager.Runtime.EnableRecording(
    filePath: "recordings/my_session.mcap",
    chunkSizeBytes: 1024 * 1024,    // 1MB
    compression: "lz4",
    coordinateMode: "LeftHand"
);
```

### 压缩

| 模式 | 库 | 说明 |
|------|-----|------|
| `None` | 无 | 不压缩，写入最快 |
| `Lz4` | `IonKiwi.lz4.dll` | LZ4 块压缩，速度快，压缩率中等 |
| `Zstd` | `ZstdSharp.dll` | Zstandard 压缩，速度中等，压缩率高 |

压缩 DLL 位于 `Runtime/Plugins/compression/`。

### 坐标模式

录制时会写入 `coordinate_mode` 元数据（`"LeftHand"` 或 `"RightHand"`），重放时自动恢复。

## 回放

### Inspector 配置

在 FoxgloveManager 组件的 **MCAP Replay** 区域：

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enable Replay` | `bool` | `false` | 勾选后启用回放 |
| `Replay File Path` | `string` | `""` | .mcap 文件的完整路径 |
| `Replay Auto Play` | `bool` | `false` | 启动后自动开始播放 |
| `Disable Live Publishers` | `bool` | `true` | 启动回放时禁用场景中的实时 Publisher |

### 录制与回放互斥

同时启用 `Enable Recording` 和 `Enable Replay` 时，录制优先，回放自动禁用。Console 中会输出：
```
[Foxglove] Recording and Replay cannot both be enabled. Disabling Replay.
```

### 实时 Publisher 禁用

`Disable Live Publishers = true` 时，回放启动会自动禁用场景中所有 `FoxglovePublisherBase` 子类的组件，避免实时数据和回放数据重复发布。Stop 后自动恢复。

### 回放控制

通过 `McapReplayEngine` API 控制：

| 方法 | 说明 |
|------|------|
| `Play()` | 开始/继续播放。已结束时从头开始 |
| `Pause()` | 暂停播放，保持当前位置 |
| `Seek(ulong timeNs)` | 跳转到指定时间点（纳秒） |

状态枚举：`Playing`、`Paused`、`Buffering`、`Ended`

### 回放事件

`FoxgloveManager.OnReplayMessage` 事件在每条回放消息被消费时触发：

```csharp
manager.OnReplayMessage += (topic, data) =>
{
    // 处理回放数据
    var json = Encoding.UTF8.GetString(data);
    Debug.Log($"Replay: {topic} => {json}");
};
```

## 录制内容

MCAP 文件包含以下数据类型：

| 类型 | MCAP 记录 | 说明 |
|------|----------|------|
| 话题消息 | Schema + Channel + Message | 所有通过 `PublishJson` 发布的结构化数据 |
| 参数快照与变更 | Metadata 记录 | 以 `"parameters"` 为 key 的 JSON 快照 |
| 连接图快照 | Metadata 记录 | 以 `"connection_graph"` 为 key 的拓扑快照 |
| 客户端发布 | Channel + Message | 通过 ClientPublish 的数据 |

### 元数据记录

每条元数据以 `name` 标识，`value` 为 JSON 字符串：

- `coordinate_mode` -- `"LeftHand"` 或 `"RightHand"`
- `parameter_change` -- 参数变更事件
- `connection_graph_snapshot` -- 连接图快照

## 在 Foxglove 中播放 MCAP

1. 打开 Foxglove Desktop
2. File > **Open local file...** （或直接将 .mcap 拖入窗口）
3. 选择录制文件

### 播放控制

| 操作 | 方式 |
|------|------|
| 播放/暂停 | 底部控制栏或空格键 |
| 拖动时间轴 | 鼠标拖动底部时间轴手柄 |
| 加速/减速 | 底部控制栏调整播放速度 |
| 循环 | 底部控制栏切换循环模式 |

### 播放时可用的面板

- **3D 面板**：显示录制的场景实体和 Transform
- **Plot 面板**：查看录制的数值随时间变化
- **Raw Messages 面板**：查看录制消息的原始 JSON
- **Image 面板**：查看录制的相机画面

播放 MCAP 时不需要 Unity 运行，Foxglove 独立读取和渲染。

## FoxRun schema metadata

如果当前项目生成了 FoxRun runtime schema info，MCAP 录制会写入名为 `unity2foxglove.foxrun.schema` 的 metadata。它的 `value` 是紧凑 JSON，包含 `globalManifestHash`、FoxRun section 的 `manifestHash`、manifest/generator 版本、计数和每个 contract 的诊断 hash。

Unity replay 会在 MCAP 文件 load 完成后、正式 playback 前读取这条 metadata。recorded `globalManifestHash` 和当前 runtime `globalManifestHash` 不一致时，replay 会因为 schema mismatch 被阻断，并在日志里显示短 hash。显式 replay 模式下，confirmed mismatch 会 fail closed：Manager 会中止启动，不会恢复 live publishers 作为 fallback。缺少 recorded metadata、缺少当前 schema info、或 recorded metadata malformed 只会 warning，以便旧 MCAP 文件还能回放。

## 注意事项

- **不要中断录制**：`Close()` 方法负责写入 MCAP footer 和 summary 区段。如果 Unity 异常退出，footer 未写入会导致文件无法读取。正常 Stop Play 会触发 `OnDestroy` > `StopServer()` 确保正常关闭。
- **Chunk Size**：较小的 chunk 值提供更细粒度的索引，但增加文件开销。默认 1024KB（1MB）适合大多数场景。
- **压缩**：Lz4 适合实时录制（低 CPU 开销），Zstd 适合存档（更高压缩率但稍慢）。
- **大文件**：MCAP 支持随机访问，不需要一次性加载整个文件到内存。

## Schema Evidence identity modes

FoxgloveManager 的 MCAP Record & Replay 区域包含 **Schema Evidence** 设置。`Off` 会跳过 schema identity 检查，适合演示和早期调试；`Warn` 会报告 mismatch 但继续 replay 或 recording；`Strict` 会在 FoxRun `globalManifestHash` mismatch 时阻断 replay，并要求 recording evidence 完整。

默认 current evidence root 是 `Assets/Generated`，里面有 `FoxRun/` 和 `Unity2Foxglove/` 两组文件。启用 recording 且 identity mode 为 `Warn` 或 `Strict` 时，SDK 会在 `.mcap` 旁边写入配对的 `.schema` 文件夹：

```text
Recordings/session_20260521_135001.mcap
Recordings/session_20260521_135001.schema/
  schema-evidence.json
  FoxRun/
  Unity2Foxglove/
```

## Local MCAP DataLoader v1

`McapDataLoader` 是本地文件 API，用于在不启动 replay 的情况下检查 Unity 生成的 `.mcap`。它复用 indexed MCAP reader，并提供：

- `Initialize()`：返回 channels、schemas、time range、metadata indexes、attachment indexes、message count 和 diagnostics。
- `CreateIterator(query)`：按 topic、channel ID 和 log time 范围迭代原始消息。
- `GetBackfill(query)`：按选中的 channel 返回请求时间点之前的 latest raw message。

Phase 116 的 DataLoader 只暴露 raw serialized payload bytes，不解码 JSON、protobuf、CDR、image、point cloud 或 FoxRun payload。FoxRun schema metadata mismatch 在 DataLoader 中只是 diagnostic；严格 replay 仍然由 Phase 114 的 replay blocker 控制。

这不是 official Foxglove data-loader host ABI，也不包含 WASM bindings、remote data loading、HTTP range serving、Remote Access Gateway、multi-file timeline merge 或 typed payload views。

这样用户可以通过时间戳和文件夹名确认 MCAP 与 Schema Evidence 的配对关系。
