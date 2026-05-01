---
title: Foxglove Unity SDK Phase 1 执行计划
aliases:
  - Phase 1 Plan
  - FoxgloveSDK Phase 1
tags:
  - plan
  - phase1
  - todo
  - unity
  - foxglove
status: draft
updated: 2026-04-30
---

# Foxglove Unity SDK Phase 1 执行计划

> [!summary]
> 本文是从 [[00_PLAN]] 拆出来的 Phase 1 执行版，承接 [[01_PHASE0_PLAN]] 已完成的 UPM 包骨架。目标不是一次性做完数据发布链路，而是先让 Foxglove 通过 `foxglove.sdk.v1` WebSocket 子协议连上 SDK，并在连接后收到正确的 `serverInfo`。

## 对应关系

- 上位计划：[[00_PLAN]]
- 上一阶段：[[01_PHASE0_PLAN]]
- 对应阶段：`Phase 1 - 握手、会话与最小 ServerInfo`
- 包路径：`Packages/dev.unityfoxglove.sdk`
- package id：`dev.unityfoxglove.sdk`
- 根命名空间：`Unity.FoxgloveSDK`

## 本阶段目标

- 把 `ManagedWsBackend` 从占位实现变成可连接的纯 C# WebSocket Server。
- 正确处理 `Sec-WebSocket-Protocol: foxglove.sdk.v1` 握手。
- 每个新客户端连接后，只向该客户端发送一次 `serverInfo`。
- 建立可自动化验证的 Phase 1 harness，能证明握手、连接生命周期、`serverInfo` 单发行为可用。
- 保持 Phase 2 边界：本阶段不实现 `advertise` / `subscribe` / `unsubscribe` / `MessageData` 完整链路。

## 当前约定

> [!important]
> Phase 1 的协议边界要克制：`serverInfo` 只能宣告真实支持的能力。当前不支持客户端发布、服务、参数、播放控制，所以不要默认声明这些 capability。

- WebSocket 子协议固定为 `foxglove.sdk.v1`，已有常量：`Subprotocol.Id`。
- `IFoxgloveTransport` 已包含 `SendText(uint clientId, string json)`，Phase 1 必须用它单发连接初始化消息。
- `serverInfo.capabilities` 默认使用空数组 `[]`。
- `supportedEncodings` 只在支持 `clientPublish` 或 `services` 时才需要；Phase 1 默认留空，最好空集合不序列化。
- `metadata` 没有内容时也应尽量不序列化，避免制造无意义字段。
- `sessionId` 在一个 `FoxgloveSession` 生命周期内保持稳定；重启 session 后生成新的 id。

## 本阶段不做

- 不实现 channel 广播：`advertise` / `unadvertise`。
- 不解析客户端订阅：`subscribe` / `unsubscribe`。
- 不真正发送业务数据：`MessageData` 路由留到 Phase 2。
- 不声明 `clientPublish`，因为当前不支持 Foxglove 客户端向 Unity 发布数据。
- 不声明 `time`，因为当前不广播 Foxglove time message。
- 不引入 Native Backend；`NativeFoxgloveBackend` 继续只作为 Phase 5 预留入口。

## Todo

### A. WebSocket 依赖落地

- [ ] 选择并 vendor `websocket-sharp` / `websocket-sharp-netstandard` 作为 Phase 1 managed backend。
- [ ] 放置到 `Packages/dev.unityfoxglove.sdk/Plugins/websocket-sharp/` 或等价的包内第三方目录。
- [ ] 保留第三方库 license / notice / source URL，写入 `Documentation~/Architecture.md`。
- [ ] 确认 `Unity.FoxgloveSDK.asmdef` 能引用该 WebSocket 实现，且 WebGL 仍然排除。
- [ ] 如果第三方库要求单独 asmdef，命名不要污染 `Unity.FoxgloveSDK` 根命名空间。

### B. 实现 `ManagedWsBackend`

- [ ] `Start(host, port)` 创建 WebSocket server，并设置 `IsRunning = true`。
- [ ] `Stop()` 关闭 server，断开所有客户端，清空 client 映射，并设置 `IsRunning = false`。
- [ ] 为每个连接分配稳定递增的 `uint clientId`，建议从 `1` 开始。
- [ ] 维护 `clientId -> socket/session` 映射，所有访问必须线程安全。
- [ ] 连接建立后触发 `OnClientConnected(clientId)`。
- [ ] 连接关闭后触发 `OnClientDisconnected(clientId)`，并移除映射。
- [ ] 收到 text frame 时触发 `OnTextReceived(clientId, json)`。
- [ ] 收到 binary frame 时触发 `OnBinaryReceived(clientId, bytes)`。
- [ ] `SendText(clientId, json)` 只发给指定客户端，不允许降级成广播。
- [ ] `BroadcastText` / `BroadcastBinary` 遍历当前连接，跳过已断开的客户端。
- [ ] `SendBinary(clientId, data)` 暂时可实现为指定客户端发送，但 Phase 1 测试只要求接口行为不破坏。

### C. 子协议握手

- [ ] 握手时必须要求客户端请求 `foxglove.sdk.v1`。
- [ ] 如果客户端没有请求该子协议，优先在握手阶段拒绝；如果库不支持握手拒绝，则连接后立即关闭且不要触发业务初始化。
- [ ] 服务端响应必须包含接受的子协议 `foxglove.sdk.v1`。
- [ ] 支持客户端传入多个子协议时的逗号分隔匹配，例如 `foo, foxglove.sdk.v1`。
- [ ] 负向测试覆盖“无 subprotocol”和“错误 subprotocol”。

### D. 会话与 `serverInfo`

- [ ] 给 `FoxgloveSession` 增加只读 `SessionId`。
- [ ] `FoxgloveSession` 构造时生成 `SessionId`，建议使用 UTC 毫秒时间戳字符串或 `Guid` 字符串，但同一 session 内不能变化。
- [ ] `OnClientConnected(clientId)` 中构造 `ServerInfo`。
- [ ] `ServerInfo.Name = FoxgloveSession.Name`。
- [ ] `ServerInfo.SessionId = FoxgloveSession.SessionId`。
- [ ] `ServerInfo.Capabilities = []`，不要加入 `Time` 或 `ClientPublish`。
- [ ] `ServerInfo.SupportedEncodings` Phase 1 为空，且推荐空时不出现在 JSON 里。
- [ ] 使用 `JsonConvert.SerializeObject(serverInfo)` 序列化，不使用 `JsonUtility`。
- [ ] 调用 `_transport.SendText(clientId, json)`，不要调用 `BroadcastText`。

### E. JSON DTO 小修

- [ ] 为 `ServerInfo.SupportedEncodings` 添加空集合不序列化逻辑。
- [ ] 为 `ServerInfo.Metadata` 添加空字典不序列化逻辑。
- [ ] 保持 `capabilities` 必定序列化为数组，即使为空也应为 `[]`。
- [ ] 保持 capability 枚举 lowerCamelCase 序列化，现有 `"clientPublish"` 快照测试不能退化。

### F. 验证入口

- [ ] 扩展 `Tests/Runtime/Program.cs`，保留默认 skeleton validation。
- [ ] 增加手动 server 模式，例如：

```powershell
dotnet run --project "Packages\dev.unityfoxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj" -- --serve --port 8765
```

- [ ] 手动模式启动 `FoxgloveRuntime.Start("Unity Foxglove SDK", "127.0.0.1", 8765)`。
- [ ] 控制台打印连接地址：`ws://127.0.0.1:8765`。
- [ ] 按 `Ctrl+C` 或输入退出时调用 `Stop()` / `Dispose()`。
- [ ] 该入口只用于 Phase 1 协议验证，不放 Unity 业务逻辑。

### G. 自动化测试

- [ ] 增加 fake transport，用于模拟 `OnClientConnected(1)`。
- [ ] 断言新连接只产生一次 `SendText(1, json)`。
- [ ] 断言 `serverInfo` 没有通过 `BroadcastText` 发送。
- [ ] 断言 `serverInfo` JSON 包含 `"op":"serverInfo"`。
- [ ] 断言 `serverInfo.name` 等于 runtime/session name。
- [ ] 断言 `serverInfo.sessionId` 非空。
- [ ] 断言 `serverInfo.capabilities` 是空数组。
- [ ] 断言 JSON 不包含 `"clientPublish"`、`"time"`、`"parameters"`。
- [ ] 增加本地 `ClientWebSocket` 集成测试：使用 `foxglove.sdk.v1` 连接，读取第一条 text frame，并解析为 `serverInfo`。
- [ ] 增加负向集成测试：不带 subprotocol 连接时必须被拒绝或立即关闭。
- [ ] 所有测试失败时必须抛异常并让进程返回非 0。

### H. 文档更新

- [ ] 更新 `Documentation~/README.md`，说明 Phase 1 仅支持连接和 `serverInfo`。
- [ ] 更新 `Documentation~/Architecture.md`，记录 managed WebSocket backend、第三方依赖、Phase 2 边界。
- [ ] 记录手动验证步骤：打开 Foxglove，连接 `ws://127.0.0.1:8765`。
- [ ] 明确写出“看不到 topic 是预期行为”，因为 `advertise` 属于 Phase 2。

## 验收矩阵

### 自动化验收

- [ ] `dotnet run --project "Packages\dev.unityfoxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj"` 返回 0。
- [ ] 默认测试输出包含 Phase 0 skeleton checks 和新增 Phase 1 checks。
- [ ] fake transport 测试证明 `serverInfo` 是 per-client `SendText`，不是 broadcast。
- [ ] `ClientWebSocket` 正向连接能读到合法 `serverInfo`。
- [ ] `ClientWebSocket` 负向连接能证明错误 subprotocol 被拒绝。

### 手动验收

- [ ] 启动手动 server 模式后，Foxglove 可连接 `ws://127.0.0.1:8765`。
- [ ] Foxglove 连接时服务端没有异常。
- [ ] 断开、重连、再次断开不会留下脏 client 状态。
- [ ] 当前没有 topic 列表是预期结果，不作为失败。

## 风险检查

- [ ] 不要把 `serverInfo` 广播给所有客户端。
- [ ] 不要为了“看起来功能多”提前声明 capability。
- [ ] 不要把 `supportedEncodings = ["json"]` 当作默认值；官方实现里它主要给 client publish / services 使用。
- [ ] 不要在 WebSocket 后台线程里调用 Unity API。
- [ ] 不要在 Phase 1 修改 `BinaryEncoding` 的 publish 语义，除非测试发现 Phase 0 回归。
- [ ] 不要把 Native Backend 和 managed backend 同时推进。

## 下一阶段入口

> [!success]
> Phase 1 完成后，进入 Phase 2：实现 `advertise`、解析 `subscribe` / `unsubscribe`，并按每个客户端自己的 `subscriptionId` 发送 `MessageData`。
