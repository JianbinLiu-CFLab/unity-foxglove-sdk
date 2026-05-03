# Phase 11：MCAP Reader + Replay Engine

## Context

Phase 10 已完成 MCAP writer / recording / dual-write，当前录制范围是 topic message data：Schema、Channel、Message、Chunk、MessageIndex、ChunkIndex、Statistics、Summary。Phase 11 反向实现 reader 与 replay，把 `.mcap` 文件加载回 Unity，并用 PlaybackControl 驱动时间轴。

> [!important] 范围边界
> Phase 10 没有把 Parameters、Services、ConnectionGraph、ClientPublish 写入 MCAP。Phase 11 只回放 topic messages。Parameters/Services 等状态文件化仍属于 Phase 12。

## Goal

- [ ] Unity 内可加载 Phase 10 录制的 `.mcap` 文件。
- [ ] ReplayEngine 支持 Load / Seek / Play / Pause。
- [ ] Foxglove PlaybackControl 能控制 replay 时间轴。
- [ ] 多 channel 按 `log_time` 同步回放 `/tf`、`/scene`、`/unity/camera`。
- [ ] Demo cube 可被回放数据驱动：pose、scale、color 随时间轴变化。
- [ ] LZ4 和 Zstd compressed chunks 可读取。
- [ ] Windows IL2CPP Player 验收通过。

## Design Decisions

| 决策 | 选择 |
|------|------|
| Reader 类型 | Seekable indexed reader + sequential fallback |
| Replay 输出 | topic 重发 + Unity demo object adapter |
| 时间源 | 复用 `PlaybackClock` |
| 调度时间 | MCAP `Message.log_time` |
| publish_time | 解析保存，不参与调度 |
| 压缩 | `""`、`lz4`、`zstd` |
| 压缩依赖 | `K4os.Compression.LZ4` + `ZstdSharp.Port` (纯托管 NuGet) |
| 参数/服务回放 | 不做，Phase 12 |
| 大文件策略 | 不整文件加载，按 chunk/index 分块读取 |
| 无 summary/index 文件 | 可 sequential scan；`CanSeek=false`，不声明可控时间轴 |

## References

- 本地 MCAP spec：`third-party/mcap/website/docs/spec/index.md`
- 本地 MCAP reader 参考：`third-party/mcap/python/mcap/mcap/reader.py`
- LZ4：`K4os.Compression.LZ4`（纯托管，NuGet PackageReference）
- Zstd：`ZstdSharp.Port`（纯 C# port，NuGet PackageReference）

## Target Files

新增：

- `Runtime/IO/McapReader.cs`
- `Runtime/IO/McapBinaryReader.cs`
- `Runtime/IO/McapRecords.cs`
- `Runtime/IO/McapCompression.cs`
- `Runtime/IO/McapReplayEngine.cs`
- `Runtime/Unity/FoxgloveReplayObjectAdapter.cs`
- `Tests/Runtime/Phase11Validation.cs`

修改：

- `Runtime/Core/FoxgloveRuntime.cs`
- `Runtime/Core/FoxgloveSession.cs`
- `Runtime/Unity/FoxgloveManager.cs`
- `Runtime/Unity.FoxgloveSDK.asmdef`
- `Tests/Runtime/FoxgloveSdk.Tests.csproj`
- `Tests/Runtime/Program.cs`
- `00_PLAN.md`
- `Documentation~/README.md`
- `Documentation~/Architecture.md`

## Batch 11A：Dependencies

- [ ] 测试项目增加 `K4os.Compression.LZ4` + `ZstdSharp.Port` NuGet PackageReference。
- [ ] `Unity.FoxgloveSDK.asmdef` 引用 LZ4/Zstd assemblies（通过 `com.unity.nuget.newtonsoft-json` 同级方式或 UPM git 包集成）。
- [ ] 若 IL2CPP stripping 需要，补 `Assets/link.xml` preserve rules。
- [ ] 新增 smoke test：LZ4/Zstd compress → decompress → bytes roundtrip。

验收：

- [ ] Editor 编译通过。
- [ ] dotnet test 编译通过。
- [ ] Windows IL2CPP build 压缩库可正常加载调用。

## Batch 11B：MCAP Record Reader

实现 `McapRecords`：

- [ ] 从 `McapRecordReader` 抽取 LE read helpers（`ReadU16LE`、`ReadU32LE`、`ReadU64LE`、`ReadString`）为 Runtime 层 `McapBinaryReader` 静态工具类，不重复实现。Tests 的 `McapRecordReader` 改为复用 `McapBinaryReader`。
- [ ] `McapHeader`
- [ ] `McapSchema`
- [ ] `McapChannel`
- [ ] `McapMessage`
- [ ] `McapChunk`
- [ ] `McapMessageIndex`
- [ ] `McapChunkIndex`
- [ ] `McapStatistics`
- [ ] `McapSummaryOffset`
- [ ] `McapFooter`
- [ ] `McapFileSummary`

实现 `McapReader`：

- [ ] 校验 leading/trailing magic。
- [ ] 支持 LE helpers：u16/u32/u64/string/map/prefixed bytes。
- [ ] 支持 record size limit，默认 256 MiB。
- [ ] 读取 Footer，定位 summary section。
- [ ] 解析 summary 中的 Schema、Channel、Statistics、ChunkIndex。
- [ ] 无 summary 时 sequential scan 构建基础 channel/message 信息。
- [ ] 遇到未知 opcode：跳过 record content，不 crash。
- [ ] 遇到损坏 length / 越界 offset：返回 load failure，不部分回放。

## Batch 11C：Chunk + Compression

实现 `McapCompression`：

- [ ] `Decode("", data, uncompressedSize)` 直接返回 data。
- [ ] `Decode("lz4", data, uncompressedSize)` 用 `K4os.Compression.LZ4`。
- [ ] `Decode("zstd", data, uncompressedSize)` 用 `ZstdSharp.Port`。
- [ ] unknown compression 抛 `UnsupportedCompressionException`。
- [ ] CRC 非 0 时验证 uncompressed CRC32。
- [ ] 解压后长度必须等于 `uncompressed_size`。

实现 chunk message 读取：

- [ ] 按 `chunk_start_offset` seek 到 Chunk record。
- [ ] 解压 `records`。
- [ ] 在 uncompressed chunk data 内解析 Message records。
- [ ] `MessageIndex.records[].offset` 解释为 chunk data 内偏移。
- [ ] `ChunkIndex.message_index_offsets` 解释为文件绝对 offset。
- [ ] 如果 MessageIndex 缺失，允许 full chunk scan。

## Batch 11D：Replay Engine

实现 `McapReplayEngine`：

- [ ] `Load(Stream/FilePath)` 建立 summary/index。
- [ ] 暴露 `StartTimeNs`、`EndTimeNs`、`CanSeek`、`Channels`。
- [ ] `Seek(ulong timeNs)` 清空 pending queue，定位到包含 seek time 的 chunk。
- [ ] `Play()` 设置 playing。
- [ ] `Pause()` 设置 paused。
- [ ] `Tick(ulong nowNs)` 发布 `(lastTime, nowNs]` 的 due messages。
- [ ] 多 channel 按 `(log_time, chunk_order, record_order)` 稳定排序。
- [ ] 同 timestamp 保持文件顺序。
- [ ] 到达 EndTime 后进入 Ended，不再重复发布；外部调用 Seek 可从 Ended 恢复。
- [ ] Seek 向后不重放旧消息；Seek 向前允许重新读取历史消息。
- [ ] payload 保持原始 bytes，不在 replay engine 内做 schema decode。

Channel ID 策略：

- [ ] MCAP `ushort channel_id` 映射为 runtime replay channel id：`0x80000000 | mcapChannelId`。
- [ ] replay channel ID 保留高位 range `[0x80000000, 0xFFFFFFFF]`，live channel 注册时必须拒绝落入该范围。
- [ ] replay register 使用 MCAP Channel 的 topic / encoding / schemaName / schemaEncoding / schemaContent。
- [ ] no-schema channel 使用空 schema fields。

## Batch 11E：Runtime + PlaybackControl

修改 `FoxgloveRuntime`：

- [ ] 新增 `LoadMcapReplay(string path)`。
- [ ] 新增 `UnloadMcapReplay()`。
- [ ] 新增 `_replayEngine` 字段。
- [ ] Load 成功后调用 `EnablePlaybackControl(startNs, endNs)`。
- [ ] Runtime Start 后注册 replay channels。
- [ ] Runtime Stop 时释放 replay stream，但不清除 replay path，方便 restart。
- [ ] `Tick()` 顺序改为：drain service calls → replay due messages → broadcast Time。

修改 `FoxgloveSession`：

- [ ] PlaybackControl seek command 后调用 runtime replay seek。
- [ ] playbackState response 使用 seek 后 `CurrentTimeNs`。
- [ ] replay loaded 时 `serverInfo` 声明 `playbackControl`。
- [ ] replay loaded 时 `serverInfo.dataStartTime/dataEndTime` 来自 MCAP。

冲突规则：

- [ ] Recording 和 Replay 同时启用时 fail fast，记录 warning，不启动 replay。
- [ ] Replay topic 与 live publisher topic 相同允许存在，但 sample 场景默认关闭 live publishers，避免验收混淆。

## Batch 11F：Unity Manager + Demo Adapter

修改 `FoxgloveManager` Inspector：

- [ ] 新增 `[Header("MCAP Replay")]`。
- [ ] `_enableReplay = false`
- [ ] `_replayFilePath = ""`
- [ ] `_replayAutoPlay = false`
- [ ] `_driveUnityObjects = true`
- [ ] StartServer 中若 enable replay，先 LoadMcapReplay，再 Start runtime。

新增 `FoxgloveReplayObjectAdapter`：

- [ ] Inspector 映射 frame/entity id 到 Transform。
- [ ] 默认 demo 映射：`Cube` frame/entity → scene 中 `Cube` GameObject。
- [ ] 订阅 replay message event。
- [ ] `foxglove.FrameTransform`：解析 JSON，按 `child_frame_id` 更新 position/rotation。
- [ ] `foxglove.SceneUpdate`：解析第一层 cube primitive，更新 scale/color/pose。
- [ ] `foxglove.CompressedImage`：本阶段只 topic replay，不反向渲染到 Unity texture。
- [ ] JSON parse 失败只 warning-once per topic，不中断 replay。

## Batch 11G：Tests

Reader tests：

- [ ] `TestMcapMagicAndFooter`
- [ ] `TestReadSummarySchemasChannels`
- [ ] `TestReadChunkIndexAbsoluteOffsets`
- [ ] `TestReadMessageIndexChunkRelativeOffsets`
- [ ] `TestReadSchemalessChannel`
- [ ] `TestSequentialFallbackNoSummary`
- [ ] `TestRecordSizeLimit`
- [ ] `TestMalformedOffsetFailsLoad`

Compression tests：

- [ ] `TestUncompressedChunkDecode`
- [ ] `TestLz4ChunkDecode`
- [ ] `TestZstdChunkDecode`
- [ ] `TestUnknownCompressionFails`
- [ ] `TestChunkCrcValidation`

Replay tests：

- [ ] `TestReplayLoadRegistersChannels`
- [ ] `TestReplayPlayPublishesDueMessages`
- [ ] `TestReplayPausePublishesNothing`
- [ ] `TestReplaySeekForwardSkipsOldMessages`
- [ ] `TestReplaySeekBackwardCanReplayHistory`
- [ ] `TestReplayMultiChannelTimeOrdering`
- [ ] `TestReplaySameTimestampFileOrder`
- [ ] `TestReplayEndedState`

Runtime tests：

- [ ] `TestRuntimeLoadMcapEnablesPlaybackRange`
- [ ] `TestPlaybackControlSeekMovesReplayCursor`
- [ ] `TestReplayChannelIdsDoNotCollideWithLiveIds`
- [ ] `TestRecordingAndReplayConflictWarns`
- [ ] `TestReplayUnloadCleansChannels`

Unity/manual tests：

- [ ] Editor Play Mode 加载 Phase 10 录制文件。
- [ ] Foxglove 连接后看到 `/tf`、`/scene`、`/unity/camera`。
- [ ] Foxglove 时间轴 Play/Pause/Seek 可控制 Unity replay。
- [ ] Demo cube pose/scale/color 随 replay 时间变化。
- [ ] Windows IL2CPP Player 重复上述验收。
- [ ] LZ4/Zstd fixture 在 IL2CPP Player 中可读取。

## Acceptance Matrix

| 项 | 标准 |
|----|------|
| dotnet tests | Phase 0-11 全通过 |
| Editor compile | 无 C# compile error |
| IL2CPP build | Windows Player build 成功 |
| MCAP load | Phase 10 录制文件可加载 |
| Topic replay | Foxglove 可看到回放 topics |
| Time control | Play/Pause/Seek 生效 |
| Multi-channel sync | /tf 与 /scene 时间同步，无明显乱序 |
| Unity object replay | cube pose/scale/color 可随时间轴回放 |
| Compression | `""`、`lz4`、`zstd` chunk 均可读 |
| Regression | Phase 10 recording 不回归 |

## Deferred To Phase 12

- [ ] Parameters 文件化录制与回放。
- [ ] Services 文件化录制与回放。
- [ ] ConnectionGraph 文件化录制与回放。
- [ ] ClientPublish 文件化录制与回放。
- [ ] CompressedImage 反向渲染到 Unity texture panel。
- [ ] MCAP writer compression 输出。
- [ ] 非 Windows 平台 native compression 分发矩阵。
- [ ] 录制时写入 `coordinate_mode` 到 Channel metadata，回放自动识别坐标系。

## Risks

- **纯托管压缩 + IL2CPP:** `K4os.Compression.LZ4` 和 `ZstdSharp.Port` 均为纯 C# 实现，无 native lib，IL2CPP 风险低。但必须早期 smoke test 确认 AOT 下编译和调用正常。
- **Seek 假绿:** 必须测试 `ChunkIndex.message_index_offsets` 文件绝对 offset 和 `MessageIndex.records[].offset` chunk 相对 offset，两个概念不能混。
- **Live/replay 双源混乱:** sample replay 场景默认禁用 live publishers。
- **大文件卡顿:** `Tick()` 每帧限制 replay message batch 数量，通过 `_replayMaxMessagesPerTick` 控制（默认 1000），超出则下帧继续并进入 Buffering 状态。
- **JSON adapter 脆弱:** Unity object replay 只做 demo adapter，不把所有 Foxglove schemas 都承诺为通用 scene importer。
