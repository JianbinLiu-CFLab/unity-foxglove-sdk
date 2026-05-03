# Phase 10：MCAP Writer（录制 / 双写）

## Context

Phase 0-9 已完成（276/276 tests）。SDK 实现了完整的 Foxglove WebSocket 协议：实时数据流、参数、服务、ConnectionGraph、ClientPublish、Assets、PlaybackControl。

Phase 10 新增 MCAP 文件录制能力——在 `FoxgloveSession.Publish()` 路径上加双写分支，将发布的数据同时写入 MCAP 文件。MCAP 没有现成 C# 实现，需从零写。

本 plan 由 DeepSeek 执行。输出格式为 Obsidian Markdown plan 文件。

## MCAP 格式要点

- Magic: `0x89 M C A P 0x30 \r \n`（8 字节，文件头尾各一份）
- 每条 record: `opcode(u8) + content_length(u64 LE) + content(N bytes)`
- String: `u32 LE length + UTF-8 bytes`
- Map: `u32 LE total_byte_length + repeated(string key + string value)`
- 最小合法文件: Magic + Header + DataEnd + Footer + Magic = 75 字节

### Record 类型

| Opcode | 名称 | 内容 |
|--------|------|------|
| 0x01 | Header | profile(string) + library(string) |
| 0x03 | Schema | id(u16) + name(string) + encoding(string) + data(u32-prefixed bytes) |
| 0x04 | Channel | id(u16) + schema_id(u16) + topic(string) + message_encoding(string) + metadata(map) |
| 0x05 | Message | channel_id(u16) + sequence(u32) + log_time(u64) + publish_time(u64) + data(bytes) |
| 0x06 | Chunk | start_time(u64) + end_time(u64) + uncompressed_size(u64) + crc(u32) + compression(string) + records(u64-prefixed bytes) |
| 0x07 | MessageIndex | channel_id(u16) + entries(u32-prefixed array of (u64 timestamp, u64 offset)) |
| 0x08 | ChunkIndex | start_time + end_time + chunk_offset + chunk_length + message_index_offsets(map u16→u64) + message_index_length + compression + compressed_size + uncompressed_size |
| 0x0B | Statistics | message_count(u64) + schema_count(u16) + channel_count(u32) + attachment_count(u32) + metadata_count(u32) + chunk_count(u32) + start_time(u64) + end_time(u64) + channel_message_counts(map u16→u64) |
| 0x0E | SummaryOffset | group_opcode(u8) + group_start(u64) + group_length(u64) |
| 0x0F | DataEnd | crc(u32) |
| 0x02 | Footer | summary_start(u64) + summary_offset_start(u64) + summary_crc(u32) |

### 文件结构（chunked）

```
[Magic]
[Header]
[Schema records]           ← 独立于 chunk，RegisterChannel 时写入
[Channel records]          ← 同上
[Chunk 1: 包含 Message records]
  [MessageIndex per channel]
[Chunk 2: ...]
  [MessageIndex per channel]
[DataEnd]
--- Summary Section ---
[Schema copies]
[Channel copies]
[Statistics]
[ChunkIndex records]
[SummaryOffset records]
[Footer]
[Magic]
```

## 设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| Chunked vs unchunked | Chunked | Foxglove Studio 需要 chunk index 做 seeking |
| 压缩 | 不压缩（compression=""） | 首版简单；后续可加 lz4/zstd |
| CRC | 全部 0 | 首版不做校验 |
| Summary section | 包含 | Foxglove Studio 快速加载需要 |
| publish_time | = log_time | MCAP spec 推荐 |
| Chunk size | 默认 1MB | 可配置 |
| Profile | 空字符串 | 非 ROS |
| Library | "unity-foxglove-sdk" | 标识 |
| Schema/Channel 位置 | data section 独立 record（不在 chunk 内） | 简单且保证即使无消息也有 schema/channel |
| ID 映射 | Foxglove uint32 → MCAP uint16 | MCAP spec 限制；内部重新分配紧凑 ID，超过 `ushort.MaxValue` 必须 fail recording + log error，禁止 cast/wrap |

> [!important] 官方 MCAP 参考
> 本阶段以 `third-party/mcap` 为 wire format 参考。关键语义：
> - `MessageIndex.records[].offset` 是相对 **uncompressed chunk data** 起点的偏移。
> - `ChunkIndex.message_index_offsets` 是每个 `MessageIndex` record 从 **文件开头** 算的绝对 offset。
> - `Channel.schema_id = 0` 表示该 channel 没有 schema。
> - `Statistics.channel_count` 和 `Statistics.chunk_count` 都是 `uint32`。

## 双写拦截点

```
FoxgloveSession.Publish(uint channelId, byte[] payload, ulong logTimeNs)
  ├─ _recorder?.WriteMessage(channelId, logTimeNs, payload)   ← NEW
  └─ foreach subscriber → transport.SendBinary(...)           ← existing

FoxgloveSession.RegisterChannel(AdvertiseChannel ch)
  ├─ _recorder?.AddChannel(ch.Id, ch.Topic, ch.Encoding,     ← NEW
  │      ch.SchemaName, ch.SchemaEncoding, ch.Schema)
  └─ _channels.Register + _transport.BroadcastText(...)       ← existing
```

## 目标文件结构

新增：
- `Runtime/Core/McapWriter.cs` — 低级 MCAP binary record writer
- `Runtime/Core/McapRecorder.cs` — Foxglove→MCAP 适配器（ID 映射、schema 去重、chunk 管理）
- `Tests/Runtime/McapRecordReader.cs` — 仅测试用的 MCAP 顺序 parser
- `Tests/Runtime/Phase10Validation.cs`

修改：
- `Runtime/Core/FoxgloveSession.cs` — 双写钩子
- `Runtime/Core/FoxgloveRuntime.cs` — 持有 McapRecorder 生命周期
- `Runtime/Unity/FoxgloveManager.cs` — Inspector 录制配置
- `Tests/Runtime/FoxgloveSdk.Tests.csproj` — 新增编译项
- `Tests/Runtime/Program.cs` — 调用 Phase10Validation

---

## Batch 9：McapWriter（低级格式层）

### A. 基础设施

- [ ] `McapWriter` 类，接受 `Stream`，不持有 stream 所有权
- [ ] `Position` 属性跟踪已写字节数（用于 offset 计算）
- [ ] Magic 常量: `{ 0x89, 0x4D, 0x43, 0x41, 0x50, 0x30, 0x0D, 0x0A }`
- [ ] `WriteMagic()` 写 8 字节
- [ ] 私有 LE helpers: `WriteU16LE`, `WriteU32LE`, `WriteU64LE`（写入 MemoryStream）
- [ ] `WriteString(MemoryStream, string)`: u32 length + UTF-8 bytes
- [ ] `WriteMap(MemoryStream, Dictionary<string,string>)`: u32 total_byte_len + entries
- [ ] `WritePrefixedBytes(MemoryStream, byte[])`: u32 length + raw bytes
- [ ] `WriteRecord(byte opcode, byte[] content)`: opcode + u64 content_length + content

### B. Record writers

- [ ] `WriteHeader(string profile, string library)`
- [ ] `WriteSchema(ushort id, string name, string encoding, byte[] data)`
- [ ] `WriteChannel(ushort id, ushort schemaId, string topic, string messageEncoding, Dictionary<string,string> metadata)`
- [ ] `WriteMessage(ushort channelId, uint sequence, ulong logTime, ulong publishTime, byte[] data)`
- [ ] `WriteChunk(ulong startTime, ulong endTime, ulong uncompressedSize, string compression, byte[] records)`
- [ ] `WriteMessageIndex(ushort channelId, List<(ulong timestamp, ulong offset)> entries)`
- [ ] `WriteChunkIndex(ulong startTime, ulong endTime, ulong chunkOffset, ulong chunkLength, Dictionary<ushort,ulong> messageIndexOffsets, ulong messageIndexLength, string compression, ulong compressedSize, ulong uncompressedSize)`
  - 注意：`messageIndexOffsets` value 是每个 `MessageIndex` record 的文件绝对 offset，不是 chunk 内偏移。
- [ ] `WriteStatistics(ulong messageCount, ushort schemaCount, uint channelCount, uint attachmentCount, uint metadataCount, uint chunkCount, ulong messageStartTime, ulong messageEndTime, Dictionary<ushort,ulong> channelMessageCounts)`
  - 注意：Statistics 的 `channel_count` 是 u32，`chunk_count` 是 u32，`schema_count` 是 u16。
- [ ] `WriteDataEnd()` — crc=0
- [ ] `WriteFooter(ulong summaryStart, ulong summaryOffsetStart, uint summaryCrc)`
- [ ] `WriteSummaryOffset(byte groupOpcode, ulong groupStart, ulong groupLength)`
- [ ] `Dispose()` — flush stream

### C. 测试辅助：McapRecordReader（仅测试用）

- [ ] `Parse(byte[] data)` → `(hasLeadingMagic, List<McapRecord>, hasTrailingMagic)`
- [ ] `McapRecord` struct: `Opcode`, `ContentLength`, `Content`
- [ ] Decode helpers: `DecodeHeader`, `DecodeSchema`, `DecodeChannel`, `DecodeMessage`, `DecodeChunk`, `DecodeMessageIndex`, `DecodeFooter`, `DecodeDataEnd`
- [ ] `ReadString(byte[] buf, ref int offset)`
- [ ] `ReadU16LE`, `ReadU32LE`, `ReadU64LE`

### Batch 9 测试

- [ ] `TestMagicBytes`: 写 magic，验证 8 字节值
- [ ] `TestMinimalValidFile`: Magic + Header + DataEnd + Footer + Magic，验证总长 ≥ 75 字节，解析回 record opcodes
- [ ] `TestHeaderRecord`: opcode=0x01，profile/library 往返
- [ ] `TestSchemaRecord`: opcode=0x03，id/name/encoding/data 往返
- [ ] `TestChannelRecord`: opcode=0x04，id/schemaId/topic/encoding 往返
- [ ] `TestMessageRecord`: opcode=0x05，channelId/sequence/logTime/publishTime/data 往返
- [ ] `TestChunkContainsInnerRecords`: opcode=0x06，读取 chunk data 后能解析内部 record
- [ ] `TestMessageIndexRecord`: opcode=0x07，entries 往返
- [ ] `TestChunkIndexRecord`: opcode=0x08，验证 `message_index_offsets` 用 u16+u64 entry，offset 字段按 u64 解码。
- [ ] `TestStatisticsWireWidths`: 验证 `channel_count`/`chunk_count` 都按 u32 写入，后续时间字段不发生错位。
- [ ] `TestFooterRecord`: opcode=0x02，summaryStart/summaryOffsetStart 往返
- [ ] `TestStringEncoding`: 空串、ASCII、Unicode 正确编码
- [ ] `TestMapEncoding`: 空 map、多 entry 正确编码

---

## Batch 10：McapRecorder（适配层 + 集成）

### D. McapRecorder

- [ ] 构造函数接受 `Stream` + 可选 `IFoxgloveLogger` + `int chunkSizeBytes = 1MB`
- [ ] 构造时立即写 Magic + Header
- [ ] Schema 去重: `Dictionary<SchemaKey, ushort> _schemaKeyToMcapId`
  - `SchemaKey = (schemaName, schemaEncoding, schemaContentHash)`。
  - 相同 name/encoding/content 复用 ID；同名但 encoding/content 不同必须分配不同 MCAP schema ID。
  - 空 schema：当 `schemaName`、`schemaEncoding`、`schemaContent` 都为空时，不写 Schema record，Channel 使用 `schema_id=0`。
- [ ] Channel ID 映射: `Dictionary<uint, ushort> _foxgloveToMcapChannel`
  - 分配前检查 schema/channel 数量；若下一个 ID 会超过 `ushort.MaxValue`，记录 error，关闭/禁用 recording，禁止生成损坏文件。
- [ ] `AddChannel(uint foxgloveChannelId, string topic, string encoding, string schemaName, string schemaEncoding, string schemaContent)`：
  - 若为 no-schema channel → `schemaId = 0`，不写 Schema record
  - 若 schema key 未见过 → 分配新 MCAP schema ID（从 1 起），写 Schema record 到文件
  - 分配新 MCAP channel ID（从 1 起），写 Channel record 到文件
  - 存储 schema/channel copies 用于 summary
- [ ] `WriteMessage(uint foxgloveChannelId, ulong logTimeNs, byte[] payload)`：
  - 查 mapping，未映射则 log warning + skip
  - 递增 per-channel sequence counter
  - 写 Message record 到 chunk buffer（内部 McapWriter 写入 MemoryStream）
  - 记录 MessageIndex entry: (logTimeNs, offset in chunk buffer)
  - 更新 chunk start/end times
  - 更新全局 statistics
  - 若 chunk buffer ≥ chunkSizeBytes → `FlushChunk()`
- [ ] `FlushChunk()` 私有方法：
  - 取 chunk buffer bytes
  - 记录 `chunk_start_offset = _fileWriter.Position`（文件绝对 offset）
  - 用 `_fileWriter.WriteChunk(...)` 写 Chunk record
  - 记录 `message_index_start_offset = _fileWriter.Position`
  - 每个有消息的 channel 写一个 MessageIndex record；写之前记录 `messageIndexOffset = _fileWriter.Position`，保存到 `ChunkIndex.message_index_offsets[channelId]`
  - 保持 `MessageIndex.records[].offset` 为 chunk buffer 内偏移；不要和 `ChunkIndex.message_index_offsets` 混用
  - 记录 ChunkIndex entry
  - 重置 chunk buffer
- [ ] `Close()`：
  - 幂等（`_closed` guard）
  - FlushChunk()
  - WriteDataEnd
  - 记录 summaryStart offset
  - 写 Summary: Schema copies → Channel copies → Statistics → ChunkIndex records
  - 记录 summaryOffsetStart offset
  - 写 SummaryOffset records（每种 group 一个）
  - WriteFooter
  - WriteMagic（尾部）
  - Flush stream
- [ ] `Dispose()` → Close() + dispose stream
- [ ] 所有 I/O 操作 try/catch，失败只 log warning 不 crash

### E. Session 双写钩子

- [ ] `FoxgloveSession` 新增 `private McapRecorder _recorder` + `internal void SetRecorder(McapRecorder r)`
- [ ] `RegisterChannel()` 末尾加: `_recorder?.AddChannel(channel.Id, channel.Topic, channel.Encoding, channel.SchemaName, channel.SchemaEncoding ?? "", channel.Schema)`
- [ ] `Publish(channelId, payload, logTimeNs)` 在 foreach 之前加: `_recorder?.WriteMessage(channelId, logTimeNs, payload)`

### F. Runtime 生命周期

- [ ] `FoxgloveRuntime` 新增字段: `_recorder`, `_recordingStream`, `_recordingEnabled`, `_recordingPath`, `_recordingChunkSize`
- [ ] `EnableRecording(string filePath, int chunkSizeBytes = McapRecorder.DefaultChunkSizeBytes)` — Start 前调用
- [ ] `DisableRecording()` — 清除配置
- [ ] `Start()` 中：若 _recordingEnabled，创建 FileStream + McapRecorder，调 `_session.SetRecorder(_recorder)`；创建失败 log warning 不 crash
- [ ] `Stop()` 中：先 `_recorder?.Close()` + dispose，再 dispose session。确保录制数据在 session 销毁前完整写入

### G. Unity Manager Inspector

- [ ] 新增 Inspector 字段（`[Header("MCAP Recording")]`）：
  - `_enableRecording = false`
  - `_recordingPrefix = "foxglove"`（文件名前缀）
  - `_recordingDirectory = ""`（空=Application.persistentDataPath）
  - `_recordingChunkSizeKB = 1024`（默认 1MB）
- [ ] `StartServer()` 中：若 `_enableRecording`，拼接路径 `{dir}/{prefix}_{yyyyMMdd_HHmmss}.mcap`，调 `_runtime.EnableRecording(fullPath, _recordingChunkSizeKB * 1024)`

### Batch 10 测试

- [ ] `TestRecorderMinimalFile`: 创建 recorder 立即 Close，验证合法空 MCAP（Magic + Header + DataEnd + Footer + Magic）
- [ ] `TestRecorderSingleChannel`: AddChannel + Close，验证 Schema + Channel record 出现在 data section 和 summary
- [ ] `TestRecorderSingleMessage`: AddChannel + WriteMessage + Close，验证 Chunk 包含 Message，有 MessageIndex，summary 有 ChunkIndex，Statistics messageCount=1
- [ ] `TestRecorderMultipleMessages`: 3 条消息，验证 chunk 内 3 个 Message，MessageIndex 3 entries，Statistics 正确
- [ ] `TestRecorderSchemaDedup`: 两个 channel 共享同一 schemaName/schemaEncoding/schemaContent，验证只写一个 Schema record
- [ ] `TestRecorderSchemaSameNameDifferentContent`: 两个 channel schemaName 相同但 schemaContent 不同，验证写两个 Schema record，Channel 分别引用正确 schemaId
- [ ] `TestRecorderNoSchemaChannel`: schemaName/schemaEncoding/schemaContent 全空时，验证 Channel.schema_id=0 且不写空 Schema record
- [ ] `TestRecorderIdMapping`: Foxglove channelId=1000,2000，验证 MCAP channelId=1,2（紧凑）
- [ ] `TestRecorderIdOverflow`: 模拟 schema/channel 超过 `ushort.MaxValue`，验证 recording fail/disabled + log error，不发生 wrap/truncate
- [ ] `TestRecorderChunkFlush`: 小 chunk size（256B），写足够消息触发多 chunk，验证多个 Chunk + ChunkIndex
- [ ] `TestChunkIndexMessageIndexOffsetsAreSeekable`: 解析 ChunkIndex 后 seek 到每个 `message_index_offsets`，验证该 offset 处 opcode=0x07，且 MessageIndex.channel_id 正确
- [ ] `TestRecorderSequenceCounters`: per-channel sequence 独立递增
- [ ] `TestRecorderStatistics`: 验证 messageCount, schemaCount, channelCount, chunkCount, time range, per-channel counts
- [ ] `TestDualWritePublish`: FakeTransport + MemoryStream recorder，RegisterChannel + Publish，验证 WS transport 收到 AND MCAP 有效
- [ ] `TestDualWriteNoRecorder`: 无 recorder 时 Publish 不 crash
- [ ] `TestRecorderLifecycle`: Runtime.EnableRecording + Start + Stop，验证文件有效
- [ ] `TestEmptyRecording`: 启用录制但不发消息，Stop 产出合法空 MCAP
- [ ] `TestCloseIdempotent`: Close 两次不 crash
- [ ] `TestZeroLengthPayload`: 空 payload 写合法 Message record
- [ ] `TestPublishWithoutChannel`: 未注册 channel 的消息 skip 不 crash
- [ ] `TestTimestampEdgeCases`: logTimeNs=0 和 ulong.MaxValue 不 overflow
- [ ] `TestOfficialMcapReaderCompatibility`: 用 `third-party/mcap/python` 官方 reader 打开生成文件，读取 topics/messages/summary/index，验证至少 `/tf`、`/scene`、`/unity/camera` 可被官方 reader 正常遍历

---

## 建议执行顺序

1. 先写 McapWriter（纯格式层）+ McapRecordReader（测试工具）
2. 写 Batch 9 格式层测试，全通过
3. 写 McapRecorder（适配层）
4. 写 Batch 10 适配层测试，全通过
5. 加 Session/Runtime/Manager 集成钩子
6. 写集成测试 + 边界测试
7. 跑完整 dotnet 验证（276 + ~35 新 = ~311）
8. Unity Editor 手动验收
9. IL2CPP Player 验收

## 验收矩阵

### 自动化

- [ ] Phase 0-10 dotnet validation 全部通过
- [ ] McapWriter 每种 record type 有 roundtrip 测试
- [ ] McapRecorder schema 去重、ID 映射、chunk flush、statistics 全覆盖
- [ ] 使用 `third-party/mcap` 官方 reader/CLI 交叉验证生成文件，避免自写 writer + 自写 reader 同错假绿
- [ ] 双写：transport 和 MCAP 同时收到数据
- [ ] 空录制、幂等 Close、无 channel 发消息等边界覆盖
- [ ] Phase 0-9 无回归

### 手动验收

- [ ] Editor Play Mode 连接 Foxglove，同时录制 MCAP
- [ ] 录制的 .mcap 文件可在 Foxglove Studio 中打开
- [ ] Foxglove Studio 显示录制的 /tf、/scene、/unity/camera 数据
- [ ] Foxglove Studio 时间轴可拖动（chunk index 有效）
- [ ] 3D / Camera / Parameters / Services / Time / ConnectionGraph 不回归
- [ ] IL2CPP Player 构建通过 + 录制功能验收

## 风险与注意事项

- **MCAP 格式实现 bug**：每种 record type 都有 roundtrip 测试 + McapRecordReader 字节级验证
- **Chunk offset 计算错误**：McapWriter.Position 精确跟踪字节数；多 chunk 测试验证 offset
- **录制 I/O 影响 publish 延迟**：chunk buffer 是 MemoryStream，WriteMessage 只写内存；FlushChunk 才真正 I/O
- **崩溃导致文件不完整**：Stop/Dispose 都会 Close recorder；MonoBehaviour.OnDestroy 调 StopServer。不完整文件缺 summary/footer 但 data section 仍可被健壮的 reader 部分读取
- **Schema 内容大**（SceneUpdate JSON Schema ~51KB）：每个 schema name 只写一次，summary 复制一次
- **IL2CPP 风险**：McapWriter/McapRecorder 是纯 C#，无反射、无泛型虚调用，IL2CPP 安全

## 已知限制

MCAP 录制范围仅限于 topic 消息数据（Message records）。双写钩子仅在 `FoxgloveSession.Publish()` 中，以下 WebSocket 协议功能的数据**不会**出现在 .mcap 文件中：

- **Parameters** — 走 JSON text 协议，不经 `Publish` 路径
- **Services** — 走 JSON text + binary response，不经 `Publish` 路径
- **ConnectionGraph** — 动态拓扑，仅 WebSocket 连接期间维护
- **ClientPublish** — 走 `OnClientBinary`，不在当前双写范围内

因此在 Foxglove Studio 中打开 .mcap 文件后，Parameters、Services、ConnectionGraph 面板不可用属于预期行为。扩展录制范围到这些路径已列入 Phase 12 计划。
