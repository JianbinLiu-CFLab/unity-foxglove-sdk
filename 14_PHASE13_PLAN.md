# Phase 13：Bug 修复 + 重构（Phase 10-12 Code Review 驱动）

## Context

**测试状态：364/364 全通过 (Phase 0-12)。**

Phase 10-12 新增了 MCAP Writer、Reader、ReplayEngine、压缩、录制范围扩展、坐标系自动识别。功能矩阵完整，但 Code Review 发现了 7 个 bug（2 个 P1 功能性 + 4 个 P2 + 1 个 P3）和 6 个设计问题。

Phase 13 专门解决这些问题，不新增功能。优先修复会影响手动验收的功能性 bug（Bug #1, #2），再做架构重构降低 God Class 复杂度。

## Goal

- [ ] 修复 7 个 bug（2 P1 + 4 P2 + 1 P3）← Batch 13A-C 完成
- [ ] 修复 6 个 Codex review bug（4 P1 + 2 P2）← Batch 13I
- [ ] 消除 FoxgloveRuntime ↔ FoxgloveSession 双向依赖
- [ ] 拆分 FoxgloveRuntime God Class（370 → ~250 行）
- [ ] 拆分 FoxgloveSession God Class（582 → 3×200 行）
- [ ] 补齐测试缺口 + 手工验收
- [ ] 396+ 测试全通过，零回归

## Non-Goals

- 不新增协议能力
- 不新增 MCAP 功能（attachment、protobuf 等 → Phase 15）
- 不做 Inspector UX / Attributes / Source Generator（→ Phase 14）

---

## Batch 13A：P1 Bug 修复（功能性）

### Bug #1: McapReplayEngine.PopPending() RemoveAt(0) O(n)

**文件**: `Runtime/IO/McapReplayEngine.cs`

**修改方案**:

- [ ] `_pending` 从 `List<McapMessage>` 改为 `Queue<McapMessage>`
- [ ] `PopPending()` 改为 `_pending.Dequeue()`
- [ ] `Tick()` 中 `_pending[0]` 的 peek 改为 `_pending.Peek()`
- [ ] `Seek()` 中 `_pending.Clear()` 保持不变（Queue 亦有 Clear）
- [ ] 添加消息到 pending 时改用 `_pending.Enqueue(msg)`

**验收**:
- [ ] Phase 11 replay tests 全部通过
- [ ] Perf: 1000 条 pending 消息的 Tick 调用无 O(n*m) 退化

### Bug #2: McapReplayEngine.Tick() 固定步长不受 PlaybackClock speed 控制

**文件**: `Runtime/IO/McapReplayEngine.cs`, `Runtime/Core/FoxgloveRuntime.cs`

**修改方案**:

- [ ] `McapReplayEngine.Tick()` 改为 `Tick(ulong nowNs)`，接受外部当前时间
- [ ] 移除内部 `_elapsedNs` 自增逻辑和 `const ulong tickStepNs`
- [ ] `ElapsedNs` 属性保留但改为从外部注入计算
- [ ] `Seek()` 中不再设置 `_elapsedNs`（由调用方通过 nowNs 传入控制）
- [ ] `FoxgloveRuntime.Tick()` 中调用 `_replayEngine.Tick(_playbackClock.NowNs)` 而不是 `_replayEngine.Tick()`
- [ ] 确保 PlaybackClock 的 speed/pause/seek 状态正确影响 NowNs

**前提条件（已验证）**:
- PlaybackClock pause 时 `NowNs` 返回 `_currentTimeNs` 不再推进（`PlaybackClock.cs:34`），所以 `_replayEngine.Tick(_playbackClock.NowNs)` 在 pause 时传入同一个值 → ReplayEngine 不再 emit 新消息。**无需额外修改。**

**验收**:
- [ ] Phase 11 replay tests 全部通过
- [ ] PlaybackClock speed=2x 时 replay 速度翻倍（可在 Phase 11 tests 中验证消息 emit 时间跨度）
- [ ] PlaybackClock pause 时 replay 暂停
- [ ] PlaybackClock seek 后 replay 从新位置开始

---

## Batch 13B：P2 Bug 修复

### Bug #3: EnableReplay() 读文件两次

**文件**: `Runtime/Core/FoxgloveRuntime.cs`, `Runtime/IO/McapReplayEngine.cs`

**修改方案**:

- [ ] `McapReplayEngine` 新增 `public McapFileSummary Summary => _summary;` 暴露已解析的 summary
- [ ] `FoxgloveRuntime.EnableReplay()` 中删除第二个 `File.OpenRead` + `reader.ReadSummary()` 调用
- [ ] 直接从 `_replayEngine.Summary` 读取 `Schemas`、`Channels` 等
- [ ] coordinate_mode mismatch 检测逻辑也从 `_replayEngine.Summary.Channels` 读取

**验收**:
- [ ] Phase 11 replay tests 全部通过（channel 注册、schema lookup 正常）
- [ ] coordinate_mode mismatch warning 仍正常触发

### Bug #4: ClientPublish channel ID 编码碰撞

**文件**: `Runtime/Core/FoxgloveSession.cs`

**修改方案**:

- [ ] 当前 `FoxgloveSession.cs:408` 用 `HashCode.Combine(clientId, chId) & 0x0FFFFFFF`，**不保证唯一性**
- [ ] 改为 `McapRecorder` 内部 auto-increment counter：`WriteClientMessage()` 首次看到新的 `(clientId, chId)` 时从 `_nextCid` 分配 MCAP channel ID，存进 `_chMap[(clientId, chId)]`
- [ ] `_chMap` key 从 `uint` 改为 `(uint clientId, uint chId)` tuple，value 为 `ChMap`（保持现有结构）
- [ ] 移除 `0xA0000000` hash 逻辑（改用全局 auto-increment，与 `AddChannel` 的 `_nextCid++` 一致）
- [ ] 可选：保留 `0xA0000000` 高位标记作为标识位（`0xA0000000 | _nextCid`），仅用于人类可读区分 client vs server channel，无功能意义

**验收**:
- [ ] `TestClientPublishInMcap` 仍通过
- [ ] 新增：不同 (clientId, chId) 组合产生不同 MCAP channel ID（无碰撞）

### Bug #5: BroadcastGraphUpdate 重复写 Metadata

**文件**: `Runtime/Core/FoxgloveSession.cs`

**修改方案**:

当前代码已实现脏标记 + `BroadcastGraphUpdate()` 内检查（`FoxgloveSession.cs:289-298`），每次 graph 变更仍会 broadcast WebSocket 给订阅者，但 Metadata 只在脏时写一条。这已解决 10 个 RegisterChannel 连续调用产生 10 条全量 snapshot 的问题。

但 `BroadcastGraphUpdate` 在每次网络拓扑变更时立即调用，如果同一帧内有多次拓扑变更（如 HandleSubscribe 中 add subscription → BroadcastGraphUpdate，HandleClientAdvertise → BroadcastGraphUpdate），仍可能产生多次 Metadata 写入。

- [ ] 当前实现的脏标记逻辑保持不变
- [ ] 优化（可选）：Metadata 写入延迟到 Tick() — `BroadcastGraphUpdate()` 只设脏标记 + 发 WebSocket，Tick() 中如果脏则写一条 Metadata 并清标记。这样同一帧内多次拓扑变更只产生 1 条 Metadata。
- [ ] 改为延迟模式后，需在 `Close()` 前也 flush 一次脏标记（保证录制结束时保存最终 snapshot）

**验收**:
- [ ] 录制含 10 个 channel 注册的会话 → `foxglove.connection_graph` Metadata 仅 1 条（或极少条数）
- [ ] ConnectionGraph 功能无回归

### Bug #6: ChannelRec.Meta 引用共享（设计脆弱性）

**文件**: `Runtime/IO/McapRecorder.cs`

**修改方案**:

- [ ] `AddChannel()` 中写入 ChannelRec 时，对 `Meta` 做 shallow copy：`Meta = new Dictionary<string, string>(meta)`
- [ ] 其他引用类型字段（`Name`, `Enc`, `Data`）也做防御性拷贝（`Data` 已经是 new byte[]）

**验收**:
- [ ] Phase 10-12 tests 全部通过
- [ ] 修改 McapRecorder 内部 Meta dict 不影响已存储的 ChannelRec

---

## Batch 13C：P3 Bug 修复

### Bug #7: McapBinaryReader 无边界检查

**文件**: `Runtime/IO/McapBinaryReader.cs`

**修改方案**:

- [ ] 每个 Read 方法开头加边界检查：`if (off + size > buf.Length) throw new InvalidDataException(...)`
- [ ] `ReadU16LE`: 检查 `off + 2 > buf.Length`
- [ ] `ReadU32LE`: 检查 `off + 4 > buf.Length`
- [ ] `ReadU64LE`: 检查 `off + 8 > buf.Length`
- [ ] `ReadString`: 先检查 `off + 4`，再检查 `off + 4 + len`
- [ ] `ReadPrefixed`: 同 ReadString
- [ ] `ReadMap`: 先检查 `off + 4`，再在循环中逐对检查

**验收**:
- [ ] 损坏 MCAP 文件抛 `InvalidDataException` 而非 `IndexOutOfRangeException`
- [ ] 新增：`TestTruncatedRecordFailsGracefully` 和 `TestTruncatedStringFailsGracefully`

---

## Batch 13D：消除双向依赖

### FoxgloveRuntime ↔ FoxgloveSession 循环依赖

**当前状态**:

```
Runtime → Session (直接持有引用)
Session → Runtime (通过 _runtime 反向引用，访问 PlaybackControl, Replay, Assets)
```

**修改方案**:

- [ ] 新建 `IRuntimeContext` 接口（`Runtime/Core/IRuntimeContext.cs`）：
  ```csharp
  public interface IRuntimeContext
  {
      bool PlaybackEnabled { get; }
      ulong GetPlaybackStartNs();
      ulong GetPlaybackEndNs();
      void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs);
      PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId);
      void ReplaySeek(ulong timeNs);
      void ReplayPlay();
      void ReplayPause();
      FoxgloveAssetRegistry Assets { get; }
  }
  ```
- [ ] `FoxgloveRuntime` 实现 `IRuntimeContext`
- [ ] `FoxgloveSession` 的 `_runtime` 字段类型从 `FoxgloveRuntime` 改为 `IRuntimeContext`
- [ ] `FoxgloveSession.SetRuntime(FoxgloveRuntime)` 改为 `SetRuntimeContext(IRuntimeContext)`
- [ ] 移除 Session 中对 Runtime 具体类型的依赖

**验收**:
- [ ] Phase 0-12 全部 364 tests 通过
- [ ] Session 不再有 `using Unity.FoxgloveSDK.IO` 等 Runtime 层引用（除必要外）
- [ ] 无循环依赖残留

---

## Batch 13E：God Class 拆分 #1 — FoxgloveRuntime

### 提取 RecordingController

**当前 FoxgloveRuntime 录制相关**: 7 个字段 + 3 个方法 + 1 个事件处理

**修改方案**:

- [ ] 新建 `Runtime/Core/RecordingController.cs`：
  - 持有 `McapRecorder`、录制配置字段
  - `EnableRecording()` / `DisableRecording()` / `SetCoordinateMode()`
  - `AttachToSession(FoxgloveSession)` — 注入 recorder 到 session
  - `DetachFromSession()` — 关闭 recorder + 解绑事件
  - `OnParameterChangedForRecording()` — 从 Runtime 移入
- [ ] `FoxgloveRuntime` 持有 `RecordingController _recording`
- [ ] `FoxgloveRuntime.Start()` 中录制初始化委托给 `_recording.AttachToSession()`
- [ ] `FoxgloveRuntime.Stop()` 中录制清理委托给 `_recording.DetachFromSession()`
- [ ] Runtime 行数目标：370 → ~260

### 提取 ReplayController

**当前 FoxgloveRuntime 回放相关**: 4 个字段 + 5 个方法 + 1 个事件

**修改方案**:

- [ ] 新建 `Runtime/Core/ReplayController.cs`：
  - 持有 `McapReplayEngine`、`_summarySchemas`、`_channelTopicMap`
  - `EnableReplay()` / `DisableReplay()` — 从 Runtime 移入
  - `ReplaySeek()` / `ReplayPlay()` / `ReplayPause()` — 代理到 engine
  - `RegisterReplayChannels(FoxgloveSession)` — channel 注册逻辑
  - `Tick(FoxgloveSession, ulong nowNs)` — 回放消息发布逻辑
  - `OnReplayMessage` 事件
- [ ] `FoxgloveRuntime` 持有 `ReplayController _replay`
- [ ] Runtime 行数目标：~260 → ~200

**验收**:
- [ ] Phase 0-12 全部 364 tests 通过
- [ ] FoxgloveRuntime 行数 ≤ 220
- [ ] RecordingController 和 ReplayController 各自 ≤ 150 行
- [ ] 无 IRuntimeContext 接口实现冲突

---

## Batch 13F：God Class 拆分 #2 — FoxgloveSession

### 按协议域拆分为 partial class 或独立 handler

**当前 FoxgloveSession (582 lines)**:

| 职责域 | 方法 | 行数估算 |
|--------|------|----------|
| Channel 管理 + Publish | 7 | ~100 |
| Subscribe/unsubscribe + ConnectionGraph | 5 | ~80 |
| Parameters (get/set/sub/unsub) | 6 | ~100 |
| Services (drain + handlers) | 2 | ~60 |
| ClientPublish + Assets | 4 | ~60 |
| PlaybackControl | 2 | ~30 |
| 生命周期 + Transport 事件 | 6 | ~80 |
| 构造函数 + 字段 | - | ~70 |

**修改方案**:

- [ ] 将 `FoxgloveSession.cs` 拆分为 4 个 partial class 文件：
  - `FoxgloveSession.cs` — 保留构造函数、字段、生命周期（Start/Stop/Dispose/ClearSession）、Channel API、Publish
  - `FoxgloveSession.Parameters.cs` — 所有 Parameter handler（get/set/sub/unsub）+ `_paramSubs`
  - `FoxgloveSession.Services.cs` — DrainServiceCalls + service handler dispatch
  - `FoxgloveSession.Connection.cs` — Subscribe/unsubscribe + ConnectionGraph + ClientPublish + Assets + PlaybackControl
- [ ] 所有 partial 文件共享同一个 class 声明和字段
- [ ] 目标：每个文件 ≤ 200 行

**验收**:
- [ ] Phase 0-12 全部 364 tests 通过
- [ ] 每个 partial 文件 ≤ 200 行
- [ ] 编译通过，无缺失引用

---

## Batch 13G：代码重复消除 + 低优先级重构

### 1. FoxgloveReplayObjectAdapter 合并重复的 Primitive 方法

**文件**: `Runtime/Unity/FoxgloveReplayObjectAdapter.cs`

- [ ] `ApplyCubePrimitive` 和 `ApplyModelPrimitive` 的 pose/color 处理逻辑完全相同，仅 size/scale key 不同
- [ ] 提取 `ApplyPoseColor(JObject primitive, Transform target, string sizeKey)` 共享方法
- [ ] `ApplyCubePrimitive` 和 `ApplyModelPrimitive` 调用共享方法，各自处理 size/scale key 差异

**验收**:
- [ ] Adapter 功能无回归（Phase 11 相关测试通过）

### 2. 坐标转换提取为静态工具类

**文件**: `Runtime/Unity/FoxgloveManager.cs`, `Runtime/Unity/FoxgloveReplayObjectAdapter.cs`

- [ ] 新建 `Runtime/Unity/CoordinateConverter.cs` 静态类
- [ ] 移入 4 个转换方法：`UnityToFoxglovePosition/Rotation`、`FoxgloveToUnityPosition/Rotation`
- [ ] 接受 `CoordinateMode` 参数而非依赖 `FoxgloveManager` 实例
- [ ] `FoxgloveManager` 保留 4 个方法但委托给 `CoordinateConverter`
- [ ] `FoxgloveReplayObjectAdapter` 改用 `CoordinateConverter`，减少 `_manager != null ? _manager.FoxgloveToUnity*() : fallback` 重复模式

**验收**:
- [ ] 坐标转换行为不变
- [ ] Adapter 中坐标转换调用从 6 处 `_manager?` 判断简化为单一调用

### 3. ReplayEngine 不接受外部时钟问题已在 Batch 13A Bug #2 修复

### 4. FoxgloveManager.StartServer() 提取方法

**文件**: `Runtime/Unity/FoxgloveManager.cs`

- [ ] `StartServer()` 当前 ~70 行，包含 WebSocket transport 配置、Start 调用、参数/service 注册、event wiring
- [ ] 提取 `SetupTransport()` — host/port 解析与 WebSocket listener 创建
- [ ] 提取 `RegisterDefaultParameters()` — Inspector 参数批量注册
- [ ] 提取 `RegisterDefaultServices()` — Inspector service 批量注册
- [ ] 提取 `WireTransportEvents()` — OnClientConnected/Disconnected 等事件绑定
- [ ] 目标：`StartServer()` 缩减到 ≤ 20 行，作为 orchestrator

### 5. McapReplayEngine.Seek() off-by-one 测试覆盖

**文件**: `Runtime/IO/McapReplayEngine.cs`

- [ ] `Seek()` 中 `_currentChunkIdx = i - 1` 在第一个 chunk（i=0）时产生 -1，LoadNextChunk 会 advance 到 0，行为正确
- [ ] timeNs 在所有 chunk 之前时循环不命中，`_currentChunkIdx` 保持 -1，`if (_currentChunkIdx < -1)` 永远为 false，实际上 Seek 到 start 之前会被后续 Tick 从 start 开始处理——行为一致但未被测试覆盖
- [ ] 新增测试 `TestSeekBeforeStartTime` 和 `TestSeekAtFirstChunkBoundary`

---

## Batch 13I：Codex Review 修复（P1×4 + P2×2）

### Context

Batch 13A-13H 已通过 396 测试，但 Codex 第二轮 review 发现 6 个新问题：4 个 P1（ClientPublish 索引缺失、Replay 坐标转换无条件、三套不一致转换公式、UPM 缺 DLL）和 2 个 P2（事件累积泄漏、warning false positive）。**这些是"测试绿但语义不对"的问题**，需补齐。

### Bug #1: ClientPublish messages 未被 MessageIndex 和 Statistics 覆盖

**文件**: `Runtime/IO/McapRecorder.cs`

**问题**: `FlushChunk()` 只遍历 `_chMap.Values` 写 MessageIndex，`Close()` 的 Statistics 只含 `_chMap`。`_clientChMap` 中的 client channel 被遗漏，导致索引 reader 和 topic 过滤 reader 找不到 client message。

**修改方案**:
- [ ] `FlushChunk()` 合并 `_chMap.Values` 和 `_clientChMap.Values` 写入 MessageIndex
- [ ] `Close()` Statistics 的 `channel_message_counts` 合并 `_clientChMap` entries
- [ ] `_channels` 列表已包含 client channel（`WriteClientMessage` 中添加），channel group 无需额外处理

**验收**:
- [ ] `TestClientPublishMessageIndex`: 验证 client message 出现在 MessageIndex
- [ ] `TestClientPublishStatistics`: 验证 Statistics 包含 client channel 计数

### Bug #2: Replay Adapter 对 UnityRaw 文件也做坐标转换

**文件**: `Runtime/Unity/FoxgloveReplayObjectAdapter.cs`

**问题**: `CoordinateConverter.FoxgloveToUnityPosition/Rotation` 永远做转换，不管 MCAP 的 `coordinate_mode` 和 Manager 的 `ActiveCoordinateMode`。UnityRaw 录制的数据回放时会错误转换。

**修改方案**:
- [ ] Adapter 新增 `private CoordinateMode _replayCoordinateMode = CoordinateMode.FoxgloveStandard;` 字段
- [ ] 从 MCAP channel metadata 或 Manager 读取实际模式
- [ ] `HandleFrameTransform()` 和 `ApplyPrimitive()` 中只在 `_replayCoordinateMode == CoordinateMode.FoxgloveStandard` 时调用 CoordinateConverter，否则直接赋值
- [ ] 如果 Manager 可用，以 `Manager.ActiveCoordinateMode` 为准

**验收**:
- [ ] `TestReplayUnityRawNoConversion`: UnityRaw 录制回放坐标不变
- [ ] `TestReplayFoxgloveStandardWithConversion`: FoxgloveStandard 录制正确转换

### Bug #3: 三套坐标转换公式不一致

**文件**: `Runtime/Unity/CoordinateConverter.cs`, `Runtime/Unity/FoxgloveTransformPublisher.cs`, `Runtime/Unity/FoxgloveManager.cs`

**当前三套公式**:

| 位置 | Unity→Foxglove rotation |
|------|------------------------|
| `FoxgloveManager` | `(y, z, x, w)` |
| `FoxgloveTransformPublisher` | `(-z, x, -y, w)` |
| `CoordinateConverter` | `(-y, z, -x, w)` |

**确认正确公式**: ROS/foxglove 标准坐标系: Unity (`x,y,z`) → Foxglove (`z,-x,y`) 对应 position。Rotation: Unity `(x,y,z,w)` → Foxglove `(y,z,x,w)` → Unity `(y,-z,-x,w)`。**Manager 的实现是正确的。**

**修改方案**:
- [ ] `CoordinateConverter` 对齐 `FoxgloveManager` 的公式（position 和 rotation 都改）
- [ ] `FoxgloveTransformPublisher` 改用 `CoordinateConverter.UnityToFoxglovePosition/Rotation`
- [ ] 新增 roundtrip 测试：Unity→Foxglove→Unity 还原

**验收**:
- [ ] `TestCoordinateRoundtripPosition`: 转换两次回到原位
- [ ] `TestCoordinateRoundtripRotation`: 转换两次回到原旋转
- [ ] `TestConverterMatchesManager`: CoordinateConverter 公式与 Manager 一致

### Bug #4: UPM package 不含 compression DLL

**文件**: `package.json`, `Plugins/compression/`

**问题**: asmdef 引用了 `IonKiwi.lz4` 和 `ZstdSharp`，但 package 内没有对应 DLL。当前靠 Assets/Plugins/ 的 demo 项目 DLL 临时绕过。

**修改方案**:
- [ ] 将 `IonKiwi.lz4.managed.dll` 和 `ZstdSharp.Port.dll` 复制到 `Runtime/Plugins/compression/`
- [ ] 更新 `package.json` description 说明 DLL 已内置

**验收**:
- [ ] `dotnet test` 无回归
- [ ] 检查 `Runtime/Plugins/compression/` 下两个 DLL 文件存在

### Bug #5: Replay 转发 handler 在 Stop/Start 循环中累积

**文件**: `Runtime/Core/FoxgloveRuntime.cs`

**问题**: `Start()` 每次 `_replay.OnReplayMessage += (...)`，`Stop()` 未 unsubscribe。多次 Stop/Start 后回放消息被多次转发。

**修改方案**:
- [ ] 存储 delegate 引用：`private Action<string, byte[]> _replayForwarder;`
- [ ] `Start()` 中 `_replayForwarder = ...; _replay.OnReplayMessage += _replayForwarder;`
- [ ] `Stop()` 中 `if (_replayForwarder != null) _replay.OnReplayMessage -= _replayForwarder;`

**验收**:
- [ ] `TestReplayHandlerNoAccumulate`: Stop/Start 3 次后 OnReplayMessage 仅触发 1 次

### Bug #6: 坐标不匹配 warning 对所有 coordinate_mode 文件触发

**文件**: `Runtime/Core/ReplayController.cs`

**问题**: 只要有 `coordinate_mode` metadata 就发 warning，不比较当前值。匹配文件也 false positive。

**修改方案**:
- [ ] `ReplayController.Enable()` 接受 `string currentCoordinateMode` 参数
- [ ] 只在 `mcapMode != currentMode` 时 warning
- [ ] `FoxgloveRuntime.EnableReplay()` 传入当前 `_recording.CoordinateMode`

**验收**:
- [ ] matching coordinate_mode 不触发 warning
- [ ] mismatching coordinate_mode 仍触发 warning

---

## Unity / Manual Tests

- [ ] **Foxglove Studio 连接**: `ws://127.0.0.1:8765`，3D/Topics/Parameters/Services 面板正常
- [ ] **MCAP 录制**: 录制 60s 会话 → 在 Foxglove Studio 中打开 `.mcap`，所有 topic/parameter/service message 可查看
- [ ] **MCAP 回放**: 将 `.mcap` 设为 replay file → Play Mode 中 Adapter 正确驱动 Transform
- [ ] **坐标转换 — FoxgloveStandard**: Manager 设为 FoxgloveStandard → 录制 → 回放时 cube 位置与原始一致
- [ ] **坐标转换 — UnityRaw**: Manager 设为 UnityRaw → 录制 → 回放时 cube 位置与原始一致（不做转换）
- [ ] **ClientPublish**: Foxglove Studio 发送 client message → MCAP 中包含该消息 → 回放可读取
- [ ] **Stop/Start 循环**: Manager.StopServer() → StartServer() 重复 3 次 → OnReplayMessage 仅触发 1 次
- [ ] **Regression**: Phase 10-12 全部手动验收项通过

## Acceptance Matrix

| 项 | 标准 |
|----|------|
| dotnet tests | Phase 0-13 全通过，无回归 |
| P1 #1 ClientPublish 索引 | MessageIndex + Statistics 含 client channel |
| P1 #2 UnityRaw 回放 | 不做坐标转换 |
| P1 #3 转换公式一致 | CoordinateConverter = Manager = Publisher |
| P1 #4 UPM DLL | `Plugins/compression/` 含两个 DLL |
| P2 #5 Handler 无累积 | Stop/Start 3 次后单次触发 |
| P2 #6 Warning 正确 | 匹配时不 warning，不匹配时 warning |
| IL2CPP build | Windows Player build 成功 |

---

## Batch 13H：测试补齐

- [ ] `TestPendingQueuePerformance`: 验证 pending 使用 Queue 后 Tick 性能（可测量 10000 条 pending 的 Tick 耗时）
- [ ] `TestReplaySpeedControl`: 验证 ReplayEngine.Tick(nowNs) 正确响应不同速度（speed=2x 时 emit 两倍时间范围的消息）
- [ ] `TestReplayPauseResume`: 验证 pause 期间 Tick 不 emit 新消息，resume 后从正确位置继续
- [ ] `TestClientPublishChannelIdNoCollision`: 验证不同 (clientId, chId) 组合产生不同 MCAP channel ID
- [ ] `TestTruncatedRecordFailsGracefully`: 损坏 MCAP 文件抛 InvalidDataException
- [ ] `TestTruncatedStringFailsGracefully`: 截断字符串抛 InvalidDataException
- [ ] `TestSeekBeforeStartTime` 和 `TestSeekAtFirstChunkBoundary`: 验证 Seek() 边界行为

## Modified Files

**修改**:
- `Runtime/IO/McapReplayEngine.cs` — Bug #1, #2
- `Runtime/IO/McapBinaryReader.cs` — Bug #7
- `Runtime/IO/McapRecorder.cs` — Bug #6
- `Runtime/Core/FoxgloveRuntime.cs` — Bug #2, #3, Batch 13D, 13E
- `Runtime/Core/FoxgloveSession.cs` — Bug #4, #5, Batch 13D, 13F
- `Runtime/Unity/FoxgloveManager.cs` — Batch 13D (SetRuntimeContext), Batch 13G (CoordinateConverter), Batch 13G (extract methods)
- `Runtime/Unity/FoxgloveReplayObjectAdapter.cs` — Batch 13G, 13I
- `Runtime/Unity/FoxgloveTransformPublisher.cs` — Batch 13I
- `Runtime/Core/ReplayController.cs` — Batch 13I (Bug #6)

**新增**:
- `Runtime/Core/IRuntimeContext.cs` — Batch 13D
- `Runtime/Core/RecordingController.cs` — Batch 13E
- `Runtime/Core/ReplayController.cs` — Batch 13E
- `Runtime/Core/FoxgloveSession.Parameters.cs` — Batch 13F (partial)
- `Runtime/Core/FoxgloveSession.Services.cs` — Batch 13F (partial)
- `Runtime/Core/FoxgloveSession.Connection.cs` — Batch 13F (partial)
- `Runtime/Unity/CoordinateConverter.cs` — Batch 13G
- `Tests/Runtime/Phase13Validation.cs` — Batch 13H

**更新**:
- `Tests/Runtime/FoxgloveSdk.Tests.csproj` — 新增编译项
- `Tests/Runtime/Program.cs` — 调用 Phase13Validation
- `00_PLAN.md` — Phase 13 状态更新
- `Documentation~/README.md`
- `Documentation~/Architecture.md`

## Acceptance Matrix

| 项 | 标准 |
|----|------|
| dotnet tests | Phase 0-13 全通过，无回归 |
| Bug #1 | pending 使用 Queue，无 O(n) 退化 |
| Bug #2 | ReplayEngine 与 PlaybackClock 联动，speed/pause/seek 生效 |
| Bug #3 | EnableReplay 只读一次文件 |
| Bug #4 | ClientPublish channel ID 无碰撞 |
| Bug #5 | ConnectionGraph Metadata 不重复 |
| Bug #6 | ChannelRec Meta 独立拷贝 |
| Bug #7 | 损坏 MCAP 文件抛出有意义的异常 |
| 双向依赖 | Session 只依赖 IRuntimeContext，无 Runtime 具体类型引用 |
| Runtime 行数 | ≤ 220 |
| Session 行数 | 每个 partial ≤ 200 |
| **Codex P1 #1** | ClientPublish 消息进入 MessageIndex + Statistics |
| **Codex P1 #2** | UnityRaw 录制回放不做坐标转换 |
| **Codex P1 #3** | 三套转换公式对齐为同一套 |
| **Codex P1 #4** | Plugin DLL 内置 |
| **Codex P2 #5** | Handler 无累积 |
| **Codex P2 #6** | Warning 仅在 mismatch 时触发 |
| 手工验收 | Foxglove Studio 录制/回放/坐标转换/ClientPublish 全部通过 |
| IL2CPP build | Windows Player build 成功 |

## Suggested Execution Order

1. Batch 13A: P1 bugs (#1, #2) — 功能性，影响验收 ✅
2. Batch 13D: 双向依赖消除 — 架构基础，在重构前先做 ✅
3. Batch 13B: P2 bugs (#3, #4, #5, #6) ✅
4. Batch 13C: P3 bug (#7) ✅
5. Batch 13E: Runtime 拆分 (RecordingController, ReplayController) ✅
6. Batch 13F: Session 拆分 (partial classes) ✅
7. Batch 13G: 代码重复消除 ✅
8. Batch 13H: 测试补齐 ✅
9. **Batch 13I: Codex review 修复 (P1×4 + P2×2)** ← 当前
10. 全量回归测试 + 手工验收

## Risks

- **重构影响面大**: Batch 13D/13E/13F 涉及核心类拆分，364 个测试是安全网。每完成一个 batch 立即跑全量测试。
- **partial class 可能引起困惑**: 如果团队不习惯 partial，可考虑改为独立 handler 类 + 组合模式。当前选择 partial 是因为改动最小，Session 字段共享不需要重新布线。
- **IRuntimeContext 接口范围**: 需要确认接口包含的方法不遗漏。从 Session 中对 `_runtime` 的所有引用点出发反推。
- **IL2CPP 兼容性**: 新增的接口和 controller 类不做反射、不做泛型虚调用，IL2CPP 安全。
