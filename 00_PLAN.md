---
title: Foxglove Unity SDK 实施计划（修订版）
aliases:
  - PLAN
  - Foxglove Unity SDK Plan
tags:
  - plan
  - unity
  - foxglove
  - sdk
status: draft
updated: 2026-04-30
---

# Foxglove Unity SDK 实施计划（修订版）

> [!summary]
> 审核结论：原计划方向是对的，但还缺 4 个关键补丁。
> 1. 先明确是走“纯 C# 实现 ws-protocol”还是“封装官方 `foxglove-sdk` 的 C 接口做 Unity Native Plugin”。
> 2. 协议细节要对齐本地 clone 的 SDK，尤其是 `subscribe` / `unsubscribe` / `MessageData` 的字段语义。
> 3. 当前本地 `FoxgloveSDK` 更像 .NET 验证工程，还不是 Unity Package 形态。
> 4. 计划里缺少明确的非目标、验收矩阵、IL2CPP 约束和升级路径。

## 已确认事实

- 你本地已经有官方仓库 `[[foxglove-sdk/README]]`，它的核心实现是 Rust，并提供 Python / C++ / C 接口，没有现成的 C# 绑定。
- `[[foxglove-sdk/c/README]]` 和 `foxglove-sdk/c/include/foxglove-c/foxglove-c.h` 说明官方已经暴露了 WebSocket Server 的 C FFI，这意味着后续可以走 Unity Native Plugin 路线，而不是永远手写协议。
- `[[foxglove-sdk/schemas/README]]` 和 `foxglove-sdk/schemas/jsonschema/` 已经给出了官方 JSON Schema，可直接作为 Unity 侧第一批 schema 的事实来源。
- 本地 `FoxgloveSDK` 工程已经有初始骨架，但它目前是 `net9.0` 验证项目，不是 Unity 友好的 Runtime/UPM 包结构。

## 现状审计

> [!warning]
> 下面这些点如果不先修正，后面越做越容易偏离官方协议。

- `clientPublish` capability 不是“服务端向 Foxglove 发数据”的必需能力；它表示“允许客户端向服务端发布消息”。如果 MVP 只做 Unity -> Foxglove，可先不宣告这个 capability。
- v1 `subscribe` 不是只传 `channelId`，而是 `subscriptions: [{ id, channelId }]`。服务端后续发 `MessageData` 时，发的是 `subscriptionId`，不是 `channelId`。
- v1 `unsubscribe` 也不是按 `channelId` 退订，而是按 `subscriptionIds` 退订。
- 服务端 `MessageData` 的二进制格式不是 `opcode + channelId + payload`，而是 `opcode + subscriptionId(u32 LE) + logTime(u64 LE) + payload`。
- 当前骨架使用的 `HttpListener` 更适合作为 PC 侧验证手段，不应直接当作 Unity 最终运行时方案。
- `JsonUtility` 不适合作为主序列化方案。协议里有可选字段、字典、camelCase 和更复杂的数据结构，建议统一用 `Newtonsoft.Json`。

## 目标与非目标

### 目标

- 在 Unity 中提供一个可复用的数据发布 SDK，让 Foxglove 可以通过本地 WebSocket 连接到 Unity 并实时可视化数据。
- 第一阶段优先打通本地可视化链路，不追求一开始就覆盖 Foxglove 所有高级能力。
- 让 SDK 最终能以 Unity Package 形式交付，并具备最小可维护测试体系。

### 非目标

- 首版不追求与官方 `foxglove-sdk` 全能力对齐。
- 首版不做 remote access / gateway。
- 首版不做参数系统、服务调用、assets、playback control。
- 首版不支持 WebGL。
- 首版不做跨平台 Native Plugin 全覆盖，除非 Phase 0 的技术决策明确选择这条路。

## 架构决策

> [!tip]
> 建议主线采用“纯 C# MVP + 保留 Native Backend 升级口”的方式推进。

### 方案 A：纯 C# 实现 Foxglove ws-protocol

- 优点：启动快，调试直接，最适合先把 Unity 侧发布链路跑通。
- 缺点：协议维护成本由自己承担，和官方 SDK 的行为容易发生漂移。
- 适用：目标是尽快做出 Unity 本地实时可视化 MVP。

### 方案 B：封装官方 `foxglove-sdk` C FFI 为 Unity Native Plugin

- 优点：更贴近官方实现，未来扩展参数、服务、状态、播放控制更顺。
- 缺点：需要处理原生库编译、P/Invoke、平台二进制分发、IL2CPP/打包问题。
- 适用：目标是长期维护、追求协议一致性、并且接受更高的前期集成成本。

### 推荐决策

- Phase 0 先做一个 0.5 到 1 天的 spike。
- 如果纯 C# 路线能在 Unity Editor 内稳定握手、订阅、发消息，就先走纯 C# MVP。
- 代码结构从第一天起保留 backend 抽象，让 Phase 5 可以切换到官方 C FFI。

## 修订后的阶段计划

### Phase 0 - 技术决策与 Unity 包落地

目标：把“验证工程”变成“可进 Unity 的 SDK 工程骨架”，并完成纯 C# / Native Plugin 的决策。

- 拆分执行笔记：[[01_PHASE0_PLAN]]
- 当前命名约定：
  - package id：`dev.unity2foxglove.sdk`
  - 根命名空间：`Unity.FoxgloveSDK`

工作项：
- 明确首版支持范围：Unity 2022 LTS+，Editor + Standalone Player，Windows 优先。
- 将当前工程从验证用 `net9.0` 思路迁移到 Unity 友好的包结构。
- 建立统一抽象层：`IFoxgloveTransport`、`IFoxgloveClock`、`ISchemaRegistry`。
- 设计两个 backend 入口：`ManagedWsBackend` 和预留的 `NativeFoxgloveBackend`。
- 建立 UPM 目录结构、asmdef、`package.json`、`Samples~`、`Tests~`、`Documentation~`。
- 选定序列化方案为 `Newtonsoft.Json`，并准备 IL2CPP/AOT 兼容策略。

验收：
- Unity 能成功导入包，不报程序集错误。
- 一个空场景里能挂载最小启动组件。
- 明确记录“本期选择纯 C# 还是 Native Plugin”以及原因。

### Phase 1 - 握手、会话与最小 ServerInfo

目标：Foxglove 能稳定连接 Unity，完成 `foxglove.sdk.v1` 子协议握手，并收到正确的 `serverInfo`。

工作项：
- 实现 WebSocket Server 生命周期：start / stop / client connect / client disconnect。
- 正确处理 `Sec-WebSocket-Protocol: foxglove.sdk.v1`。
- 实现 `serverInfo` JSON 消息。
- 首版 capability 只宣告真正支持的能力，建议从空 capability 或 `time` 开始，不要默认宣告 `clientPublish`。
- 为 session lifecycle 预留 `clearSession` 能力，至少在接口层面支持重置会话。
- 建立 Console Harness 或 Editor Harness，作为非 Unity 业务逻辑的协议验证入口。

验收：
- Foxglove Desktop 或 Web 可以连接 `ws://127.0.0.1:8765`。
- 连接后能看到合法的 `serverInfo`。
- 重复连接、断开、重新连接不会留下脏状态。

### Phase 2 - Channel 广播、订阅状态与正确的 MessageData

目标：让“频道注册 -> 客户端订阅 -> 服务端按订阅发送消息”的链路完整闭环。

工作项：
- 实现 `advertise` / `unadvertise` JSON 消息。
- 维护 `channelId -> ChannelDescriptor` 注册表。
- 正确解析 v1 `subscribe`：
  `subscriptions: [{ id, channelId }]`
- 正确解析 v1 `unsubscribe`：
  `subscriptionIds: [...]`
- 每个客户端维护 `subscriptionId -> channelId` 的映射，而不是只维护“某 channel 是否被订阅”。
- 发送二进制 `MessageData` 时，按每个客户端的 `subscriptionId` 独立编码：
  `opcode(1) + subscriptionId(u32 LE) + logTime(u64 LE) + payload`
- 重新设计发布 API，建议至少包含时间戳：
  `Publish(channelId, payload, logTimeNs)`

验收：
- Foxglove 能收到 advertise 后的 topic 列表。
- 订阅某个 channel 后，Foxglove 能持续收到消息。
- 取消订阅后，消息立即停止。
- 同一个 channel 被多个客户端订阅时，每个客户端都按自己的 `subscriptionId` 收到正确数据。

### Phase 3 - Schema 对齐与第一批可视化消息

目标：不要自己“发明 schema”，而是直接对齐本地 clone 的官方 schema，并完成第一批真正有可视化价值的消息类型。

工作项：
- 以 `foxglove-sdk/schemas/jsonschema/` 作为 schema 真值源。
- 首批支持的 schema 建议限定为：
  `foxglove.FrameTransform`
- 首批支持的 schema 建议再补一个：
  `foxglove.SceneUpdate`
- 可选第三个 schema：
  `foxglove.PoseInFrame`
- 对 schemaless JSON 和 typed JSON 做清晰区分：
  schemaless JSON 只用于调试或简单日志，不作为核心可视化路径。
- 建立最小 DTO 层，把 Unity `Vector3` / `Quaternion` / `Transform` 映射到 Foxglove schema。
- 为 schema 加入基本格式校验或至少提供开发期断言。

验收：
- Foxglove 3D 面板能显示 Unity 物体的位姿或场景实体。
- 至少一个 topic 使用官方 JSON Schema 成功渲染。
- schema 文件来源、版本和导入方式有文档记录。

### Phase 4 - Unity 集成与易用 API

目标：让 Unity 开发者不需要理解 Foxglove 协议细节，也能顺手接入。

工作项：
- 提供 `FoxgloveRuntime` 或 `FoxgloveManager` 入口组件。
- 提供 `FoxglovePublisher<T>` 或同类封装，统一管理 channel 生命周期。
- 提供常用发布器：
  `TransformPublisher`
- 后续扩展发布器：
  `SceneUpdatePublisher`
- 明确主线程与后台线程边界，用队列或 dispatcher 解决 Unity API 线程限制。
- 提供 `TimeProvider`，统一生成 `logTimeNs`。
- 提供 Demo Scene 与最小使用文档。

验收：
- 一个 Unity 示例场景中，不写底层协议代码也能把 Transform 发布到 Foxglove。
- 停止 Play Mode、重新运行、切换场景都不会导致异常连接残留。
- 开发者只需要少量脚本和 Inspector 配置即可完成接入。

### Phase 5 - 加固、IL2CPP、Native Backend 预留

目标：把 MVP 从“能演示”推进到“能维护、能发布、能扩展”。

工作项：
- 做一次 IL2CPP 构建验证，确认序列化、反射和第三方库不会被裁剪坏。
- 对 WebSocket 层进行异常路径测试：重连、断线、未订阅发送、非法 JSON、非法二进制消息。
- 明确 `FoxgloveRuntime` 与 transport backend 的生命周期所有权：
  `Stop()` 是否只停止 session 而不 dispose transport，或每次 `Start()` 是否创建新的 transport。
- 如果纯 C# 方案在 IL2CPP 或稳定性上出现明显问题，启动 Native Backend 分支：
  基于 `foxglove-c.h` 做最小 P/Invoke 封装。
- 只有当前 5 个阶段稳定后，再评估是否增加 MCAP 录制。

**状态：Done。已通过 IL2CPP Windows Standalone 验证，link.xml 生效，serverInfo 序列化无裁剪。**

验收：
- Editor 与目标 Standalone Player 都能稳定工作。
- 有一套最小回归测试，能覆盖握手、advertise、subscribe、publish、unsubscribe。
- 对”继续纯 C#”还是”切到官方 C FFI”有明确结论：继续纯 C#，Native Backend 仅作预留。

### Phase 6 - Parameters / Services

**状态：Done。Parameters get/set、JSON Services advertise/call/response/failure/timeout 已实现，210/210 测试通过，IL2CPP Player 构建成功。ParametersSubscribe capability 与参数变更 push 明确顺延到 Phase 7。**

**已完成验收：**
- [x] Editor Play Mode 连接 Foxglove
- [x] 3D / Camera / Plot / Parameters / Services 面板均可见
- [x] `/cube/color` 和 `/cube/scale` 参数可修改，cube 可视化颜色/尺寸变化
- [x] `/cube/reset_pose` service 调用后 cube pose 重置
- [x] IL2CPP Player 中重复验收全部通过

目标：在 MVP 与 IL2CPP 加固稳定后，扩展 Foxglove WebSocket 的交互能力，而不是过早摊大到独立文件格式生态。

执行计划：
- [[07_PHASE6_PLAN]]

范围：
- Parameters get/set（ParametersSubscribe capability 与参数变更 push 在 Phase 7 补齐）
- Services（仅 JSON encoding）

明确不做：
- MCAP 双写或录制
- Assets / fetchAsset
- Playback control
- 更完整的官方 schema 覆盖
- 泛型发布器：
  提供 `FoxglovePublisher<T>` / `FoxgloveTopic<T>` 之类的类型安全封装，把 channel 注册、schema 绑定、JSON 编码和 publish 调用收口到一个 API。
- 泛型发布器只能缩小与 Rerun log API 的使用差距：
  Foxglove 协议仍然要求 channel advertise、subscription 路由、schema 声明和 `subscriptionId` 二进制转发，SDK 需要隐藏这些细节，但不能假装协议不存在。
- Rerun-like 开发体验糖层：
  设计 `[FoxgloveLog]` attribute + 反射自动发布器，把普通 C# 字段/属性映射到 topic/schema，减少手写 publisher。
- `[FoxgloveLog]` 方向需要单独处理 IL2CPP/AOT：
  反射扫描结果要么生成 link.xml / preserve 规则，要么走 Editor-time source generation，避免 Player 下 metadata 被裁剪。

### Phase 7 - Bug 修复 + DX 提升 + Time

目标：修复 Phase 6 遗留的 10 个 bug 和 plan 偏差，提升开发者体验，新增 Time capability。

执行计划：
- [[08_PHASE7_PLAN]]

范围：
- Phase 6 bug 修复（ParametersSubscribe 声明、参数变更广播、生命周期语义）
- IFoxgloveLogger 全链路连通（替换 Console.Error.WriteLine）
- 参数/service 定义从 Session 提升到 Runtime（Stop 后保留）
- Service handler delegate 注册机制
- 泛型 `FoxglovePublisher<TMessage>` 基类
- Inspector 参数注册组件
- FoxgloveManager 连接状态事件
- Time frame 周期广播

明确不做：
- ConnectionGraph（Phase 8）
- ClientPublish（Phase 8）
- Assets / fetchAsset（Phase 9）
- PlaybackControl（Phase 9）
- MCAP（Phase 10）
- `[FoxgloveLog]` attribute + source generation

### Phase 8 - ConnectionGraph + ClientPublish

目标：新增 ConnectionGraph 和 ClientPublish 两个协议能力，实现连接拓扑可视化和 Foxglove→Unity 双向通信。

执行计划：
- [[09_PHASE8_PLAN]]

范围：
- ConnectionGraph subscribe/unsubscribe/update
- ClientPublish advertise/unadvertise/MessageData
- Unity 主线程安全的 client message 回调
- ConnectionGraph 与 ClientPublish 联动

### Phase 9 - Assets + PlaybackControl

目标：补齐 `assets` 和 `playbackControl` 两个 WebSocket 协议能力。Assets 采用显式 root 映射，PlaybackControl 控制 SDK live clock，不实现历史消息重放。

执行计划：
- [[10_PHASE9_PLAN]]

范围：
- Assets / `fetchAsset` request + binary `fetchAssetResponse`
- 显式 `uriPrefix → localRoot` asset root 注册
- PlaybackControl binary request + `PlaybackState` response
- Runtime / Manager / publishers 统一 clock，确保 seek 后 topic timestamp 跟随 playback clock

明确不做：
- MCAP 双写或录制
- 任意 `file://` 本机路径读取
- asset application-level chunk protocol
- 历史消息重放

### Phase 10 - MCAP 录制 / 双写

目标：单独评估并实现 MCAP 写入能力，不把文件格式、索引、summary、CRC 与 live WebSocket 交互能力混在一起。

候选项：
- JSON / JSON Schema channel 写入 MCAP。
- 与 live WebSocket publish 双写。
- 可被 Foxglove 打开的最小 MCAP 文件。
- 是否实现 indexed writer、summary offsets、chunking、compression 另行在 Phase 10 plan 中决策。

**状态：Done。MCAP 双写实现，307 测试通过，Foxglove Studio 可正常打开录制的 .mcap 文件。**

### Phase 11 - MCAP Reader + Replay

目标：实现 MCAP 文件读取、压缩解压（LZ4/Zstd）、ReplayEngine 时间轴回放、FoxgloveReplayObjectAdapter 驱动 Unity 场景。

**状态：Done。MCAP 读取/解压/回放/adapter 驱动已实现。**

执行计划：
- [[12_PHASE11_PLAN]]

范围：
- `McapReader` + `McapBinaryReader` + `McapRecords` + `McapCompression`
- `McapReplayEngine`（Load / Play / Pause / Seek / Tick）
- `FoxgloveReplayObjectAdapter`（/tf + /scene → Unity GameObject 驱动）
- `CoordinateMode` 全局坐标系转换（UnityRaw / FoxgloveStandard）
- `FoxgloveManager` live/replay 互斥切换（`_disableLivePublishers`）

Deferred：
- Parameters / Services / ConnectionGraph / ClientPublish 文件化回放
- CompressedImage 反向渲染到 Unity texture
- MCAP writer compression 输出
- 录制时写入 `coordinate_mode` 到 Channel metadata，回放自动识别坐标系

### Phase 12 - MCAP 闭环（录制范围扩展 + 压缩输出 + 自动坐标系）

目标：补齐 MCAP 录制范围，加入压缩输出，实现坐标系自动识别。

**状态：Done。**

执行计划：
- [[13_PHASE12_PLAN]]

范围：
- MCAP writer 支持 lz4 / zstd 压缩输出（LZ4 frame format）
- Parameters / Services / ConnectionGraph / ClientPublish 纳入 MCAP 录制
- Channel metadata 写入 `coordinate_mode`，回放时不一致 warning
- 38 个 Phase 12 自动化测试，全链路回归通过

### Phase 13 - Inspector UX + Attributes + Source Generation

**状态：Planned**

候选项：
- `FoxgloveManagerEditor.cs` — 路径字段 Browse 按钮（`_replayFilePath`、`_recordingDirectory`、`_assetRoots.localRoot`）
- `[FoxgloveLog]` attribute + Editor-time source generation
- 更完整 Assets 缓存 / MIME / 大文件策略

### Phase 14 - 开源发布准备

**状态：Planned**

范围：
- README / 使用文档
- Samples~ demo 场景
- LICENSE
- GitHub Actions CI（dotnet test）
- 清理仓库垃圾

## 建议目录结构

```text
Packages/
└── dev.unity2foxglove.sdk/
    ├── package.json
    ├── Runtime/
    │   ├── Core/
    │   │   ├── FoxgloveRuntime.cs
    │   │   ├── FoxgloveSession.cs
    │   │   ├── ChannelRegistry.cs
    │   │   ├── SubscriptionRegistry.cs
    │   │   ├── FoxgloveParameterStore.cs
    │   │   ├── ParameterSubscriptionRegistry.cs
    │   │   ├── FoxgloveServiceRegistry.cs
    │   │   ├── FoxgloveServiceCall.cs
    │   │   ├── IFoxgloveLogger.cs
    │   │   └── FoxgloveLogger.cs
    │   ├── Protocol/
    │   │   ├── JsonMessages.cs
    │   │   ├── BinaryEncoding.cs
    │   │   └── Opcodes.cs
    │   ├── Transport/
    │   │   ├── IFoxgloveTransport.cs
    │   │   ├── ManagedWsBackend.cs
    │   │   └── NativeFoxgloveBackend.cs
    │   ├── Schemas/
    │   │   ├── Json/
    │   │   └── Dto/
    │   ├── Unity/
    │   │   ├── FoxgloveManager.cs
    │   │   └── Publishers/
    │   └── Unity.FoxgloveSDK.asmdef
    ├── Tests/
    │   ├── Editor/
    │   └── Runtime/
    ├── Samples~/
    │   └── BasicVisualization/
    ├── Plugins/
    │   ├── websocket-sharp/
    │   └── native/
    └── Documentation~/
        ├── README.md
        └── Architecture.md
```

## 验收矩阵

### 自动化验证

- JSON 消息快照测试：
  `serverInfo`
- JSON 消息快照测试：
  `advertise`
- JSON 消息快照测试：
  `subscribe`
- JSON 消息快照测试：
  `unsubscribe`
- 二进制消息测试：
  `MessageData` 编码与解析
- 生命周期测试：
  connect / disconnect / reconnect
- Parameters 测试：
  `getParameters` / `setParameters` / subscribe / unsubscribe
- Services 测试：
  binary request/response codec, failure cases, timeout, payload boundary
- `serverInfo.capabilities` 测试：
  只包含真实支持的能力
- Stop/Start lifecycle 测试：
  subscriptions 和 pending calls 清理

### 手工验证

- Foxglove 可连接本地 Unity 服务端。
- topic 列表正确显示。
- 3D 面板可显示 `FrameTransform` 或 `SceneUpdate`。
- Plot panel 可见 `/tf.translation.*` 曲线。
- Parameters panel 可读写 `/cube/color`、`/cube/scale`。
- Service Call panel 可调用 `/cube/reset_pose`。
- 停止发布、取消订阅、断开连接时行为符合预期。
- Unity Editor 与 Standalone Player 表现一致。

## 风险与对策

- 协议漂移风险：始终以本地 `foxglove-sdk` clone 作为协议和 schema 参考，而不是手写记忆。
- Unity 线程模型风险：所有 Unity API 访问必须留在主线程，后台线程只做网络收发和缓冲。
- IL2CPP/AOT 风险：尽早做 build 验证，不要等 API 都铺开后再测。
- 第三方 WebSocket 库风险：先隔离在 transport 层，避免把具体实现渗透到业务 API。
- Native Plugin 复杂度风险：只在纯 C# 明显不稳时切换，不在 MVP 初期同时推进两条重实现路线。
- Rerun-like API 预期风险：`FoxglovePublisher<T>` 和 `[FoxgloveLog]` 可以改善体验，但 Foxglove 的 channel/schema/subscription 模型比 Rerun 的 `log()` API 更显式，文档和 API 命名必须避免承诺“一行 log 就自动解决所有协议状态”。

## 结论

> [!success]
> 这份计划的正确主线不是“继续往下堆功能”，而是先把协议和产品边界对齐，再用一个能迁移的架构做 MVP。
> 最稳的推进方式是：
> Phase 0 先做架构决策和 Unity 包骨架，
> Phase 1-4 做纯 C# 可视化 MVP，
> Phase 5 再决定是否切到官方 `foxglove-sdk` 的 C FFI。

## 参考

- `[[foxglove-sdk/README]]`
- `[[foxglove-sdk/c/README]]`
- `[[foxglove-sdk/schemas/README]]`
