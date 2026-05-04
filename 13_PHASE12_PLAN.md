# Phase 12：MCAP 闭环（录制范围扩展 + 压缩输出 + 自动坐标系）

## Context

**状态：Done。**

Phase 11 已完成 MCAP reader / replay engine / object adapter，实现了 topic message 的录制→回放全链路。但录制范围仅限 topic messages（`Publish` 路径），Parameters/Services/ConnectionGraph/ClientPublish 没有进入 MCAP。此外录制输出为 uncompressed chunks（`compression=""`），文件偏大。坐标系的自动识别也未实现。

Phase 12 补齐这些缺口，让 MCAP 成为完整的会话存档格式。

## Goal

- [ ] MCAP writer 支持 lz4 / zstd 压缩输出。
- [ ] Parameters get/set 事件写入 MCAP（Metadata records）。
- [ ] Services 调用/响应写入 MCAP（Metadata records）。
- [ ] ConnectionGraph 拓扑变迁写入 MCAP（Metadata records）。
- [ ] ClientPublish 消息写入 MCAP（Message records）。
- [ ] 录制时写入 `coordinate_mode` 到 Channel metadata，回放自动识别坐标系。
- [ ] Foxglove Studio 打开 .mcap 后可看到 Parameters / Services / ConnectionGraph 面板数据。
- [ ] Windows IL2CPP Player 验收通过。

## Design Decisions

| 决策 | 选择 |
|------|------|
| Parameters 存储格式 | MCAP Metadata（name=`foxglove.parameters`，value=JSON `{name, type, value}`） |
| Services 存储格式 | MCAP Metadata（name=`foxglove.services`，value=JSON `{name, request, response, timestamp}`） |
| ConnectionGraph 存储格式 | MCAP Metadata（name=`foxglove.connection_graph`，value=JSON `{publishedTopics, subscribedTopics, ...}`） |
| ClientPublish 存储格式 | MCAP Message records（复用 live 路径的 `OnClientBinary` → 写入独立 channel） |
| 压缩策略 | `string _recordingCompression = ""`（默认无压缩），可选 `lz4`/`zstd` |
| 压缩触发 | 在 `FlushChunk` 时根据 `_recordingCompression` 压缩 chunk buffer |
| coordinate_mode | 写入 Channel metadata `coordinate_mode: FoxgloveStandard`，回放时 adapter 自动读取 |
| 参数/服务 snapshot | 录制结束时，录制期内所有变化写入 MCAP |
| Replay 端解析 | `McapReplayEngine` 不解析 Metadata——由 `FoxgloveRuntime` 层直接从 summary 读取并回放 |

## References

- MCAP Metadata record: `opcode=0x0C, name(string), metadata(map<string,string>)`
- MCAP Attachment: 本阶段不用于参数/服务
- `IonKiwi.lz4.managed`（标准 LZ4 frame）+ `ZstdSharp.Port` 已在 Phase 11-12 集成

## Target Files

新增：

- `Runtime/IO/McapMetadataWriter.cs`

修改：

- `Runtime/IO/McapRecorder.cs`
- `Runtime/IO/McapWriter.cs`
- `Runtime/Core/FoxgloveRuntime.cs`
- `Runtime/Core/FoxgloveSession.cs`
- `Runtime/Unity/FoxgloveManager.cs`
- `Runtime/Unity/FoxgloveReplayObjectAdapter.cs`
- `Tests/Runtime/Phase12Validation.cs`
- `13_PHASE12_PLAN.md`
- `00_PLAN.md`
- `Documentation~/README.md`
- `Documentation~/Architecture.md`

## Batch 12A：MCAP Writer Compressed Output

- [ ] `FoxgloveManager.Inspector` 新增 `_recordingCompression` dropdown（`""` / `lz4` / `zstd`），默认 `""`。
- [ ] `McapRecorder` 接收 `compression` 参数，存入字段 `_compression`。
- [ ] `FlushChunk()` 中根据 `_compression` 调用 `McapCompression.Compress(compression, buffer)` 压缩 chunk buffer。
- [ ] `WriteChunk()` 传入压缩后的数据和 `compressedSize`、`uncompressedSize`、`compression` 字符串。
- [ ] `ChunkIdx` 结构体记录 `compression` 和 `compressedSize`。
- [ ] `Close()` 中 `WriteChunkIndex` 传入压缩参数。
- [ ] 无压缩时行为不变（chunk 内仍为 raw MCAP records）。
- [ ] IL2CPP 验证：Windows Player 可录制压缩 mcap 并被 Foxglove / Phase 11 reader 正确读取。

验收：

- [ ] Editor Play Mode 录制 `lz4`/`zstd` mcap → Foxglove 可打开。
- [ ] Phase 11 replay engine 可读取压缩 mcap。
- [ ] dotnet test Phase 10 回归测试不降级。

## Batch 12B：Parameters 文件化

- [ ] `McapMetadataWriter` 工具类：封装 `WriteMetadata(name, jsonValue)` 写入 MCAP Metadata record。
- [ ] `McapRecorder` 持有 `McapMetadataWriter` 引用，传递 `_w`（McapWriter）写入。
- [ ] `FoxgloveRuntime` 在 `ParameterStore.SetParameter()` / `GetParameter()` 路径上加 hooks：
  - set 时调用 `_recorder?.WriteParameter(name, value, type)`
  - get 时调用 `_recorder?.WriteParameterGet(name, value, type)`（可选，仅记录）
- [ ] 录制开始时，将当前所有已知参数写入 snapshot（`foxglove.parameters.snapshot` Metadata）。
- [ ] 录制期间参数变更写入 `foxglove.parameters` Metadata，每条一个 `{name, type, value, timestamp}`。

验收：

- [ ] 录制含参数变更的会话 → mcap 包含 `foxglove.parameters` Metadata。
- [ ] Foxglove Studio 的 Parameters 面板可显示历史值。

## Batch 12C：Services 文件化

- [ ] `FoxgloveRuntime` 在 service 调用/响应路径加 hooks：
  - service call 时调用 `_recorder?.WriteServiceCall(name, request, timestamp)`
  - service response 时调用 `_recorder?.WriteServiceResponse(name, response, timestamp)`
- [ ] 存储为 `foxglove.services` Metadata（每次调用一条记录）。

验收：

- [ ] 录制含 service 调用的会话 → mcap 包含 `foxglove.services` Metadata。
- [ ] Foxglove Studio 的 Services 面板可显示服务调用历史。

## Batch 12D：ConnectionGraph + ClientPublish 文件化

- [ ] ConnectionGraph 拓扑变更写入 `foxglove.connection_graph` Metadata。
  - `publishedTopics` / `subscribedTopics` / `advertisedServices` 完整 JSON。
  - 录制开始时写初始状态，每次变更写增量 snapshot。
- [ ] ClientPublish message 写入 MCAP Message records：
  - `FoxgloveSession.OnClientBinary` → `_recorder?.WriteClientMessage(clientId, channelId, payload, logTimeNs)`
  - 使用预留的高位 channel ID range（如 `0xA0000000 | clientChannelId`）或新建独立 MCAP channel。
- [ ] ConnectionGraph replay 端：暂不重放到 live 连接（因为是无状态 replay），仅存档。

验收：

- [ ] 录制含 ClientPublish 的会话 → mcap 包含对应消息。
- [ ] Foxglove 打开 mcap 可看到 ConnectionGraph 面板历史。

## Batch 12E：coordinate_mode 存档 + 检测提示

- [ ] `McapRecorder.AddChannel()` 中，从 `FoxgloveManager.ActiveCoordinateMode` 获取当前坐标系。
- [ ] 写入 Channel metadata：`"coordinate_mode": "FoxgloveStandard"` 或 `"coordinate_mode": "UnityRaw"`。
- [ ] `McapChannel`（`McapRecords.cs`）新增 `Metadata` 字段。
- [ ] `McapReader.DecodeChannel()` 解析 metadata map。
- [ ] `FoxgloveReplayObjectAdapter` **始终使用** `_manager.ActiveCoordinateMode`（用户手动设置优先）。
- [ ] 若 MCAP channel metadata 中的 `coordinate_mode` 与当前 Manager 设置不一致，log warning 提示用户。
- [ ] MCAP metadata 仅作存档参考，不自动覆盖用户设置。

优先级：**FoxgloveManager manual config > MCAP metadata（仅 warning）**

验收：

- [ ] Editor Play Mode 录制 `FoxgloveStandard` mcap → Manager 设为 `UnityRaw` → adapter 用 `UnityRaw`，log warning 提示不一致。
- [ ] Manager 设为 `FoxgloveStandard` 与 MCAP 一致 → 无 warning。

## Batch 12F：Tests + Docs

- [ ] `TestCompressedChunkOutput`：lz4 / zstd 录制 + reader 解压 roundtrip。
- [ ] `TestParametersRoundtrip`：录制参数变更 → reader 解析 Metadata。
- [ ] `TestServiceRoundtrip`：录制 service call/response → reader 解析 Metadata。
- [ ] `TestConnectionGraphInMcap`：录制 ConnectionGraph → Metadata 解析。
- [ ] `TestClientPublishInMcap`：录制 ClientPublish → Message records 解析。
- [ ] `TestCoordinateModeInChannelMetadata`：录制 → 回放自动识别坐标系。
- [ ] `TestRecordingCompressionRegressionPhase10`：compression 参数不影响默认 uncompressed 行为。

## Unity / Manual Tests

- [ ] Editor Play Mode 录制 lz4 压缩 mcap → Foxglove 可打开。
- [x] Editor Play Mode 录制 zstd 压缩 mcap → Foxglove 可打开。
- [x] Editor Play Mode 录制参数变更 → mcap 包含 `foxglove.parameters` Metadata。
- [x] Editor Play Mode 录制 service 调用 → mcap 包含 `foxglove.services` Metadata。
- [x] Editor Play Mode 录制含 clientPublish 的会话 → mcap 包含对应消息。
- [x] Editor Play Mode 录制 `FoxgloveStandard` 坐标系 → mcap channel metadata 有 `coordinate_mode`。
- [x] Editor Play Mode replay 时，coordinate_mode 与 Manager 设置不一致 → Console 出现 warning。
- [x] Editor Play Mode 关闭压缩（None）→ 文件大小与 Phase 10 一致，无回归。
- [ ] Windows IL2CPP Player：无压缩录制 + LZ4 压缩回放验证（仅核心路径覆盖，非全部场景）。
- [ ] Phase 11 replay engine 可正常回放 Phase 12 录制的压缩 mcap。

> IL2CPP 验收只覆盖两个核心路径（无压缩录制、LZ4 压缩回放），未逐个验证所有压缩模式 × 录制/回放组合。原因是 IL2CPP build 耗时长、case 组合多且各路径等效性高。若将来 IL2CPP 特定场景出问题，优先从这两条路径的变体排查。

## Acceptance Matrix

| 项 | 标准 |
|----|------|
| dotnet tests | Phase 0-12 全通过 |
| Editor compile | 无 C# compile error |
| IL2CPP build | Windows Player build 成功 |
| Compressed output | `lz4`/`zstd` chunk 可被 Foxglove 和 Phase 11 reader 正确读取 |
| Parameters in mcap | Foxglove Studio Parameters 面板可见历史值 |
| Services in mcap | Foxglove Studio 可看到服务调用历史 |
| ConnectionGraph in mcap | Foxglove Studio 可看到拓扑历史 |
| ClientPublish in mcap | mcap 包含客户端发布的消息 |
| coordinate_mode auto | 回放自动识别录制时的坐标系 |
| Regression | Phase 10/11 全部通过 |

## Risks

- **Metadata 大小增长**: parameters/service 变更频繁时 Metadata records 数量可能较大。`foxglove.parameters` 采用 snapshot + delta 策略，每个 delta 一条 Metadata。
- **Compressed chunk 写入性能**: `lz4` 块级压缩对实时性影响很小；`zstd` 压缩比高但 CPU 开销更大，默认用 `""` 即可。
- **Metadata 格式兼容性**: 当前 Foxglove Studio 不自动解析自定义 Metadata，需验证实际面板行为。若不可用，需考虑改用 Attachment 或放弃面板支持，仅保留存档。
- ~~**LZ4 frame format 不兼容**: `K4os.Compression.LZ4.LZ4Pickler` 使用私有 pickle 格式，Foxglove 无法解压。~~ **已修复**。替换为 `IonKiwi.lz4.managed`（`LZ4Utility.Compress/Decompress`），标准 LZ4 frame magic `0x184D2204` 确认，Foxglove 可正常打开。
