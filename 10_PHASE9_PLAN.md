---
title: Foxglove Unity SDK Phase 9 执行计划
aliases:
  - Phase 9 Plan
  - FoxgloveSDK Phase 9
tags:
  - plan
  - phase9
  - todo
  - unity
  - foxglove
  - websocket
  - assets
  - playback
status: draft
updated: 2026-05-02
---

# Foxglove Unity SDK Phase 9 执行计划

> [!summary]
> 本文是从 [[00_PLAN]] 拆出的 Phase 9 执行版，承接 [[09_PHASE8_PLAN]] 已完成的 ConnectionGraph + ClientPublish。Phase 9 新增两个相对轻量的协议能力：Assets / `fetchAsset` 和 PlaybackControl。MCAP 不放进本阶段，单独进入后续 Phase 10，避免把 live WebSocket 控制与二进制录制文件 writer 混在一起。

## 对应关系

- 上位计划：[[00_PLAN]]
- 上一阶段：[[09_PHASE8_PLAN]]
- 对应阶段：`Phase 9 - Assets + PlaybackControl`
- 当前包路径：`Packages/dev.unity2foxglove.sdk`
- Unity 验证项目：`Untiy2Foxglove`
- 根命名空间：`Unity.FoxgloveSDK`
- 官方协议参考：<https://github.com/foxglove/ws-protocol/blob/main/docs/spec.md>
- 本地官方 SDK 参考：
  `third-party/foxglove-sdk/rust/foxglove/src/protocol/common/client/fetch_asset.rs`
  `third-party/foxglove-sdk/rust/foxglove/src/protocol/common/server/fetch_asset_response.rs`
  `third-party/foxglove-sdk/rust/foxglove/src/protocol/common/client/playback_control_request.rs`
  `third-party/foxglove-sdk/rust/foxglove/src/protocol/common/server/playback_state.rs`

## 本阶段目标

- 实现 `assets` capability：Foxglove client 发送 `fetchAsset` 后，Unity SDK 能从显式注册的 asset root 中安全读取文件并返回 binary `fetchAssetResponse`。
- 实现 `playbackControl` capability：Foxglove client 可发送 play / pause / seek / speed 请求，Unity SDK 更新 playback clock 并返回 binary `PlaybackState`。
- 将 Unity publisher 的 log time 统一收口到 Manager / Runtime clock，确保 seek 后 `/tf`、`/scene`、`/unity/camera` 和 Time frame 使用同一个时间源。
- 保持 Core / Protocol / Transport / Schemas 层不引用 `UnityEngine`。
- 新增 Phase 9 自动化测试，保持 Phase 0-8 全部回归通过。

## 本阶段不做

- 不实现 MCAP 写入、读取、双写（进入 Phase 10）。
- 不实现 `file://` 任意本机路径读取。
- 不实现 application-level asset chunk protocol；SDK 层单次 response，底层 WebSocket 可自行分帧。
- 不实现历史消息重放；PlaybackControl 只控制 live SDK clock。
- 不实现 Assets 缓存淘汰策略；首版每次请求按需读取文件。
- 不实现 `[FoxgloveLog]` attribute 或 source generation。
- 不切换到 Native Backend。

## 当前约定

> [!important]
> 所有 capability 仍遵守"声明即真实支持"原则。只有注册了至少一个 asset root 才声明 `assets`；只有显式启用 playback control 才声明 `playbackControl`。

- Assets 采用显式 root 策略：
  `uriPrefix` → `localRoot`。
- URI resolve 必须 normalize 后仍位于 `localRoot` 内；拒绝 `..` 越界、未注册 prefix、目录、缺失文件和超限文件。
- 默认 asset 大小限制：16 MiB。Runtime API 和 Inspector 可调。
- PlaybackControl 默认关闭。Unity demo 可通过 Inspector 开启。
- PlaybackControl binary request 不是三个独立 opcode；client→server 只有 `ClientOpcode.PlaybackControlRequest = 3`，command 在 payload 内。
- `ServerInfo` 的 `dataStartTime` / `dataEndTime` 仅在 playback control 启用时发送。

## 目标文件结构

- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveAssetRegistry.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Transport/PlaybackClock.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase9Validation.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Protocol/JsonMessages.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Protocol/BinaryEncoding.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveSession.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveManager.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxglovePublisherBase.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxglovePublisher.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveTransformPublisher.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveSceneCubePublisher.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveCameraPublisher.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj`
- 修改：
  `Untiy2Foxglove/Assets/Scripts/FoxgloveDemoSetup.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Documentation~/README.md`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Documentation~/Architecture.md`
- 修改：
  `00_PLAN.md`

---

## Batch 7：Assets / fetchAsset

### A. Protocol DTO + Binary codec

- [ ] `JsonMessages.cs` 新增 `FetchAsset` DTO：
  `op = "fetchAsset"`，字段 `requestId:uint`、`uri:string`。
- [ ] `BinaryEncoding` 新增 `EncodeFetchAssetResponseSuccess(uint requestId, byte[] payload)`。
- [ ] `BinaryEncoding` 新增 `EncodeFetchAssetResponseError(uint requestId, string message)`。
- [ ] `fetchAssetResponse` wire format：
  `opcode(1=4) + requestId(u32 LE) + status(u8: 0 success, 1 error) + errorMessageLen(u32 LE) + errorMessageBytes_or_assetBytes`。
- [ ] success 时 `errorMessageLen = 0`，剩余 bytes 为 asset payload。
- [ ] error 时 `errorMessageLen = UTF8(message).Length`，剩余 bytes 为 error message。
- [ ] 增加 roundtrip / decode-only 测试辅助方法可选；生产代码只必须 encode response。

### B. FoxgloveAssetRegistry

- [ ] 新增 `FoxgloveAssetRegistry`，Core 层，不引用 UnityEngine。
- [ ] 支持 `RegisterRoot(string uriPrefix, string localRoot, long maxBytes = 16 * 1024 * 1024)`。
- [ ] `uriPrefix` 必须非空，建议形如 `package://unity/` 或 `asset://demo/`；实现不强制 schema，但必须做 prefix match。
- [ ] `localRoot` 存为 `Path.GetFullPath(localRoot)`。
- [ ] `TryResolve(uri, out path, out error)`：
  未匹配 prefix 返回 false + error。
- [ ] matched relative path 必须 URI decode 后 normalize。
- [ ] resolved path 必须仍以 root full path 为前缀；否则返回越界 error。
- [ ] 目录、缺失文件、超过 maxBytes 都返回 error。
- [ ] `TryRead(uri, out bytes, out error)` 内部调用 `TryResolve` 后 `File.ReadAllBytes`。
- [ ] 不缓存文件内容，避免首版引入失效和内存策略。

### C. Runtime / Session routing

- [ ] `FoxgloveRuntime` 持有 Runtime-owned `FoxgloveAssetRegistry _assets`。
- [ ] `FoxgloveRuntime.RegisterAssetRoot(uriPrefix, localRoot, maxBytes)` 写入 `_assets`，可在 Start 前调用。
- [ ] `FoxgloveSession` 构造函数接受 asset registry 引用。
- [ ] `FoxgloveSession.OnClientText` 增加 `case "fetchAsset"`。
- [ ] fetchAsset 成功时对请求 client 调用 `_transport.SendBinary(clientId, EncodeFetchAssetResponseSuccess(...))`。
- [ ] fetchAsset 失败时对请求 client 调用 `_transport.SendBinary(clientId, EncodeFetchAssetResponseError(...))`。
- [ ] malformed JSON / missing requestId / missing uri 返回 error response；如果连 requestId 都无法解析，使用 requestId=0 并记录 warning。
- [ ] `OnClientConnected()` 的 capabilities 仅在 `_assets.HasRoots` 时包含 `Capability.Assets`。

### D. Unity Manager + Demo

- [ ] `FoxgloveManager` 新增 `[Serializable] AssetRootDefinition`：`string uriPrefix`、`string localRoot`、`long maxBytes`。
- [ ] `FoxgloveManager` Inspector 新增 `List<AssetRootDefinition> _assetRoots`。
- [ ] `StartServer()` 前将 Inspector roots 注册到 Runtime。
- [ ] 路径支持相对 Unity project root：相对路径按 `Application.dataPath/..` 解析。
- [ ] Demo 中注册一个最小 root，例如 `asset://demo/` → `Untiy2Foxglove/Assets` 或专用 demo asset folder。
- [ ] README 写明安全边界：默认不支持任意 `file://` 读取。

### Batch 7 测试

- [ ] 测试：未注册 asset root 时 serverInfo 不包含 `assets`。
- [ ] 测试：注册 asset root 后 serverInfo 包含 `assets`。
- [ ] 测试：`FetchAsset` DTO 字段名为 `requestId` 和 `uri`。
- [ ] 测试：success response opcode=4，requestId 保持，status=0，payload bytes 正确。
- [ ] 测试：error response opcode=4，requestId 保持，status=1，errorMessageLen 和 UTF8 message 正确。
- [ ] 测试：未注册 URI 返回 error response。
- [ ] 测试：`..` 越界路径返回 error response。
- [ ] 测试：缺失文件、目录、超过 maxBytes 返回 error response。
- [ ] 测试：合法文件只发送给请求 client，不 broadcast 给其他 client。

---

## Batch 8：PlaybackControl

### E. Protocol types + Binary codec

- [ ] 新增 `PlaybackCommand` enum：`Play = 0`、`Pause = 1`。
- [ ] 新增 `PlaybackStatus` enum：`Playing = 0`、`Paused = 1`、`Buffering = 2`、`Ended = 3`。
- [ ] 新增 `PlaybackControlRequest` class/struct：`Command`、`PlaybackSpeed`、`SeekTimeNs?`、`RequestId`。
- [ ] 新增 `PlaybackState` class/struct：`Status`、`CurrentTimeNs`、`PlaybackSpeed`、`DidSeek`、`RequestId?`。
- [ ] `BinaryEncoding.TryDecodePlaybackControlRequest(byte[] data, out PlaybackControlRequest req)`：
  `opcode(1=3) + command(u8) + speed(f32 LE) + hadSeek(u8) + seekTime(u64 LE) + requestIdLen(u32 LE) + requestIdBytes`。
- [ ] `BinaryEncoding.EncodePlaybackState(PlaybackState state)`：
  `opcode(1=5) + status(u8) + currentTime(u64 LE) + speed(f32 LE) + didSeek(u8) + requestIdLen(u32 LE) + requestIdBytes?`。
- [ ] `BinaryEncoding` 增加 `ReadF32LE` / `WriteF32LE` helper，使用 `BitConverter` 时显式处理 little endian。
- [ ] invalid command、buffer too short、invalid UTF8、requestIdLen 超出剩余长度时 decode 返回 false，不抛异常。

### F. ServerInfo playback fields

- [ ] `ServerInfo` 新增可选 `dataStartTime` / `dataEndTime`。
- [ ] 字段类型可复用 `FoxgloveTime` 或新增 Protocol-local `Timestamp` DTO，wire shape 必须是 `{ "sec": uint/ulong, "nsec": uint }`。
- [ ] 仅 playback control 启用时写入 `dataStartTime` / `dataEndTime`。
- [ ] `OnClientConnected()` 的 capabilities 仅在 playback control 启用时包含 `Capability.PlaybackControl`。

### G. PlaybackClock

- [ ] 新增 `PlaybackClock : IFoxgloveClock`，Core/Transport 层可测试，不引用 UnityEngine。
- [ ] 默认 live mode：`NowNs` 委托 inner `SystemClock`。
- [ ] `EnableRange(ulong startNs, ulong endNs)` 记录可播放范围，初始状态 `Paused`，当前时间 `startNs`。
- [ ] `Apply(PlaybackControlRequest req)`：
  command=Play → status Playing；
  command=Pause → status Paused；
  `SeekTimeNs` 有值 → clamp 到 `[startNs,endNs]`，设置 current time，`DidSeek=true`；
  speed <= 0 或 NaN → fallback 1.0。
- [ ] Playing 状态下 `NowNs` 根据 wall-clock elapsed * speed 前进，到达 endNs 后 status 变 `Ended` 且 current time 固定 endNs。
- [ ] Pause 状态下 `NowNs` 固定当前 playback time。
- [ ] `ToState(requestId, didSeek)` 返回 `PlaybackState`。

### H. Runtime / Session playback routing

- [ ] `FoxgloveRuntime` 构造时改为持有 `PlaybackClock`，其 inner clock 为当前 `SystemClock` 或注入 clock。
- [ ] `FoxgloveRuntime.NowNs` 暴露统一 log time。
- [ ] `FoxgloveRuntime.EnablePlaybackControl(ulong startNs, ulong endNs)` 显式启用 playback。
- [ ] `FoxgloveRuntime.DisablePlaybackControl()` 可选，恢复不声明 capability。
- [ ] `FoxgloveSession` 使用 Runtime 的 playback clock，而不是直接持有不可控 clock。
- [ ] `OnClientBinary` 在 service/client publish 之前或之后均可，但必须明确分支：先尝试 `TryDecodePlaybackControlRequest`，成功则 enqueue playback request 并 return。
- [ ] `FoxgloveRuntime.Tick()` 主线程 drain playback requests：apply request → send `PlaybackState` response 给请求 client。
- [ ] 状态变化也可 broadcast playback state；首版只要求 request response，避免额外状态噪声。
- [ ] malformed playback frame 只 warning，不断开 client。

### I. Unity publisher time unification

- [ ] `FoxgloveManager` 新增 `public ulong NowNs => _runtime?.NowNs ?? FoxgloveTimeUtil.NowUnixTimeNs()`。
- [ ] `FoxglovePublisherBase` 新增 `protected ulong CurrentLogTimeNs => _manager?.NowNs ?? FoxgloveTimeUtil.NowUnixTimeNs()`。
- [ ] `FoxglovePublisher<TMessage>.Update()` 使用 `CurrentLogTimeNs` publish。
- [ ] `FoxgloveTransformPublisher.CreateMessage()` 使用 `CurrentLogTimeNs` 构造 timestamp。
- [ ] `FoxgloveSceneCubePublisher.CreateMessage()` 使用 `CurrentLogTimeNs` 构造 timestamp。
- [ ] `FoxgloveCameraPublisher.OnReadbackComplete()` 使用 Manager/Runtime clock 构造 timestamp 和 logTime。
- [ ] 避免在 publisher 中继续直接调用 `FoxgloveTimeUtil.NowUnixTimeNs()`，除 fallback 外。

### J. Unity Manager + Demo

- [ ] `FoxgloveManager` 新增 Inspector 字段：
  `_enablePlaybackControl = false`、
  `_playbackStartOffsetSeconds = 0`、
  `_playbackDurationSeconds = 60`。
- [ ] `StartServer()` 前，如果 `_enablePlaybackControl` 为 true，根据当前 Runtime clock 计算 start/end 并调用 `EnablePlaybackControl`。
- [ ] Demo 可默认关闭 playback；README 写手动开启方式。
- [ ] Demo 验收重点是 seek 后 timestamp 变化，不要求历史 transform 重放。

### Batch 8 测试

- [ ] 测试：默认 serverInfo 不包含 `playbackControl`。
- [ ] 测试：启用 playback 后 serverInfo 包含 `playbackControl`。
- [ ] 测试：启用 playback 后 serverInfo 包含 `dataStartTime` / `dataEndTime`。
- [ ] 测试：PlaybackControlRequest decode 覆盖 play、pause、seek、speed、requestId。
- [ ] 测试：malformed playback request 返回 false，不抛异常。
- [ ] 测试：PlaybackState encode opcode=5，字段顺序和 little-endian 正确。
- [ ] 测试：PlaybackClock pause 固定时间，play 推进时间，seek clamp 到范围内。
- [ ] 测试：session 收到 playback request 后向同一 client 发送 PlaybackState binary response。
- [ ] 测试：publisher-facing `NowNs` 在 seek 后变化到 playback time。

---

## 建议执行顺序

1. 先做 Batch 7 的 Protocol codec 测试和 `FoxgloveAssetRegistry`。
2. 接入 Session fetchAsset routing，跑 Phase 9 assets 测试。
3. 做 Batch 8 的 Playback codec 和 `PlaybackClock`。
4. 接入 Runtime/Session playback routing，跑 Phase 9 playback 测试。
5. 统一 Unity publisher timestamp 来源。
6. 跑完整 dotnet 验证。
7. 跑 Unity Editor 手动验收。
8. 跑 Windows IL2CPP Player 验收。
9. 更新 README / Architecture / 00_PLAN.md。

## 验收矩阵

### 自动化验收

- [ ] Phase 0-9 dotnet validation 全部通过。
- [ ] `serverInfo.capabilities` 默认不包含 `assets` / `playbackControl`。
- [ ] 注册 asset root 后 `assets` capability 出现。
- [ ] 启用 playback 后 `playbackControl` capability 和 data range 出现。
- [ ] `fetchAsset` success/error binary response 符合官方格式。
- [ ] asset root path traversal 和 size limit 测试覆盖。
- [ ] PlaybackControl request/state binary codec 符合官方格式。
- [ ] PlaybackClock play/pause/seek/speed 测试覆盖。
- [ ] Phase 0-8 无回归。

### 手动验收

- [ ] Editor Play Mode 连接 Foxglove 成功。
- [ ] 3D / Camera / Plot / Parameters / Services / Time / ConnectionGraph / ClientPublish 不回归。
- [ ] 注册 demo asset root 后，用最小 WebSocket 测试脚本发送 `fetchAsset` 能取回 demo 文件。
- [ ] 未注册或越界 asset URI 返回 error，不断开连接。
- [ ] 开启 PlaybackControl 后，Foxglove play/pause/seek 请求得到 PlaybackState response。
- [ ] seek 后 `/tf`、`/scene`、Time frame timestamp 跟随 playback clock。
- [ ] IL2CPP Player 构建通过。
- [ ] IL2CPP Player 下重复上述验收成功。

## 风险与注意事项

- Asset 安全风险：
  不允许任意 `file://` 读取；所有路径必须落在显式注册 root 内。
- Asset 大文件风险：
  首版单 response，必须保留 maxBytes 防护。大文件分片不是本阶段目标。
- Playback 语义风险：
  本阶段控制的是 live timestamp clock，不提供历史消息重放；文档必须避免让用户误以为 seek 会自动恢复过去的 Unity 场景状态。
- Time source 分裂风险：
  如果 publisher 继续直接调用 `FoxgloveTimeUtil.NowUnixTimeNs()`，PlaybackControl 会只影响 Time frame，不影响 topic log time。必须统一到 Runtime/Manager clock。
- IL2CPP 风险：
  新 DTO 继续使用 `MemberSerialization.OptIn`，`Assets/link.xml` preserve 规则必须保持。

## 后续阶段预留

- Phase 10：MCAP 录制 / 双写。
- Phase 11 候选：`[FoxgloveLog]` attribute + Editor-time source generation。
- Phase 11 候选：更完整 Assets 缓存 / MIME / 大文件策略。
