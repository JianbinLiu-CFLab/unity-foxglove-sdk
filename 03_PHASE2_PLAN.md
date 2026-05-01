---
title: Foxglove Unity SDK Phase 2 执行计划
aliases:
  - Phase 2 Plan
  - FoxgloveSDK Phase 2
tags:
  - plan
  - phase2
  - todo
  - unity
  - foxglove
status: draft
updated: 2026-05-01
---

# Foxglove Unity SDK Phase 2 执行计划

> [!summary]
> 本文是从 [[00_PLAN]] 拆出来的 Phase 2 执行版，承接 [[02_PHASE1_PLAN]] 已完成的 WebSocket 握手与 `serverInfo`。目标是打通 `channel 注册 -> advertise -> subscribe/unsubscribe -> Publish -> MessageData` 的协议闭环，并提供一个 debug JSON heartbeat 方便手动验证。

## 对应关系

- 上位计划：[[00_PLAN]]
- 上一阶段：[[02_PHASE1_PLAN]]
- 对应阶段：`Phase 2 - Channel 广播、订阅状态与正确的 MessageData`
- 包路径：`Packages/dev.unityfoxglove.sdk`
- package id：`dev.unityfoxglove.sdk`
- 根命名空间：`Unity.FoxgloveSDK`

## 本阶段目标

- 提供显式 channel API，让调用方能注册、取消注册、发布 payload。
- 在 channel 注册后向已连接客户端发送 `advertise`。
- 新客户端连接后先收到 `serverInfo`，再收到当前 channel 列表的 `advertise` snapshot。
- 正确解析 Foxglove v1 `subscribe` / `unsubscribe` JSON。
- 使用每个客户端自己的 `subscriptionId` 编码 server -> client `MessageData`。
- 增加 debug JSON heartbeat，用 Foxglove 手动看到 topic 并接收数据。

## 当前约定

> [!important]
> Phase 2 只做协议闭环，不做官方 schema 可视化。能看到 topic、能订阅、能收到 `MessageData` 就算本阶段成功；3D 面板真正渲染 Unity 物体留到 Phase 3。

- 公开 API 采用显式方法，不提前做泛型 Publisher：
  - `RegisterChannel(AdvertiseChannel channel)`
  - `UnregisterChannel(uint channelId)`
  - `Publish(uint channelId, byte[] payload)`
  - `Publish(uint channelId, byte[] payload, ulong logTimeNs)`
- `subscribe` 使用官方 v1 格式：
  `subscriptions: [{ id, channelId }]`
- `unsubscribe` 使用官方 v1 格式：
  `subscriptionIds: [...]`
- 服务端发送 `MessageData` 使用：
  `opcode(1) + subscriptionId(u32 LE) + logTime(u64 LE) + payload`
- `serverInfo.capabilities` 继续保持 `[]`，不要为了本阶段提前声明 `clientPublish` 或 `time`。
- debug heartbeat channel 使用：
  - topic：`/debug/heartbeat`
  - encoding：`json`
  - schemaName：`""`
  - schema：`""`
- `schemaName: ""` 和 `schema: ""` 是本地官方 SDK v1 快照里的无 schema channel 表达方式，不要改成省略空字符串字段。
- Phase 2 不新增“获取已连接客户端列表”的公开接口；连接后的 snapshot 在 `OnClientConnected(clientId)` 内用 `SendText`，channel 变更用 `BroadcastText`。

## 本阶段不做

- 不做 `FrameTransform` / `SceneUpdate` / `PoseInFrame`。
- 不导入官方 JSON schema。
- 不做 Unity `MonoBehaviour` 发布器。
- 不做 `FoxglovePublisher<T>` 泛型封装。
- 不做客户端发布：不声明也不实现 `clientPublish`。
- 不做 Parameters / Services / Assets / PlaybackControl。
- 不做 MCAP。

## Todo

### A. 协议字段与 DTO 收口

- [ ] 确认 `Advertise` JSON 快照符合官方 v1：
  `op`、`channels`、`id`、`topic`、`encoding`、`schemaName`、`schemaEncoding`、`schema`。
- [ ] 无 schema 的 JSON channel 仍序列化 `schemaName: ""` 和 `schema: ""`，对齐 `foxglove-sdk/rust/foxglove/src/protocol/common/server/snapshots/...advertise...snap`。
- [ ] 不要添加 `ShouldSerializeSchema()` 来省略空字符串；相反，应确保 `AdvertiseChannel.SchemaName` 和 `AdvertiseChannel.Schema` 在注册或构造时从 `null` 规范化为 `""`。
- [ ] `schemaEncoding` 仅在非空时序列化。
- [ ] 增加快照断言：`Encoding = "json"` 且未提供 schema 时，输出包含 `"schemaName":""` 和 `"schema":""`，不输出 `null`。
- [ ] `metadata` 如果本阶段不使用，先不加入公开 API；如保留字段，空时必须省略。
- [ ] 确认 `Unadvertise` JSON 为：
  `{"op":"unadvertise","channelIds":[...]}`
- [ ] 确认 `SubscribeMessage` JSON 为：
  `{"op":"subscribe","subscriptions":[{"id":100,"channelId":1}]}`
- [ ] 确认 `UnsubscribeMessage` JSON 为：
  `{"op":"unsubscribe","subscriptionIds":[100]}`

### B. 公开 API 与 Registry

- [ ] 在 `FoxgloveSession` 增加 `RegisterChannel(AdvertiseChannel channel)`。
- [ ] `RegisterChannel` 写入 `ChannelRegistry` 后，向所有已连接客户端广播 `advertise`。
- [ ] 重复注册同一个 `channel.Id` 时，覆盖 registry 中的 descriptor，并重新广播该 channel 的 `advertise`。
- [ ] 重复注册只更新 channel descriptor，不清理已有订阅；已有 `subscriptionId` 继续有效。
- [ ] 在 `FoxgloveSession` 增加 `UnregisterChannel(uint channelId)`。
- [ ] `UnregisterChannel` 仅在 channel 存在时广播 `unadvertise`。
- [ ] `UnregisterChannel` 同时清理所有指向该 channel 的订阅。
- [ ] 在 `SubscriptionRegistry` 增加 `RemoveChannel(uint channelId)`。
- [ ] 将 `SubscriptionRegistry.GetSubscribersForChannel` 改成返回 snapshot，避免发送网络消息时持有 lock。
- [ ] 保留 `Channels` 只读入口用于调试，但文档推荐使用 `RegisterChannel` / `UnregisterChannel`。
- [ ] 在 `FoxgloveRuntime` 增加同名快捷方法：`RegisterChannel`、`UnregisterChannel`、`Publish`，内部代理到当前 `Session`。
- [ ] 如果 `FoxgloveRuntime.Session == null` 时调用上述快捷方法，抛出明确的 `InvalidOperationException`，提示先调用 `Start()`。

### C. 连接后的 advertise snapshot

- [ ] `OnClientConnected(clientId)` 保持先单发 `serverInfo`。
- [ ] `serverInfo` 之后，如果已有 channels，单发一次 `advertise` snapshot 给该 client。
- [ ] snapshot 使用 `_transport.SendText(clientId, json)`，不要使用 `BroadcastText`。
- [ ] 无 channel 时不发送空 `advertise`。
- [ ] 多个已注册 channel 应合并到一条 `advertise` 消息里。

### D. subscribe / unsubscribe 解析

- [ ] `OnClientText(clientId, json)` 解析 JSON 的 `op` 字段。
- [ ] `op == "subscribe"` 时反序列化为 `SubscribeMessage`。
- [ ] 对每个 subscription，如果 `channelId` 存在于 `ChannelRegistry`，加入 `SubscriptionRegistry`。
- [ ] 如果 `channelId` 不存在，忽略该 subscription，不要创建脏订阅。
- [ ] `op == "unsubscribe"` 时反序列化为 `UnsubscribeMessage`。
- [ ] 根据 `subscriptionIds` 移除该 client 的订阅。
- [ ] 未知 `op`、非法 JSON、字段缺失时记录 warning 并忽略，不要断开连接。
- [ ] 不解析 client binary `MessageData`，该能力留到 `clientPublish` 阶段。

### E. Publish 与 MessageData 路由

- [ ] `Publish(uint channelId, byte[] payload)` 使用 `_clock.NowNs`。
- [ ] `Publish(uint channelId, byte[] payload, ulong logTimeNs)` 使用调用方传入时间戳。
- [ ] channel 未注册时，`Publish` 不发送数据，并记录 warning 或静默忽略；本阶段不抛异常。
- [ ] 无订阅者时，`Publish` 不发送数据。
- [ ] 有订阅者时，对每个 `(clientId, subscriptionId)` 独立编码 binary frame。
- [ ] binary frame 必须使用 `BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload)`。
- [ ] 同一个 channel 被多个 client 订阅时，每个 client 收到自己的 `subscriptionId`。
- [ ] 同一个 client 对同一个 channel 建立多个 subscription 时，每个 subscription 都收到一份对应 `subscriptionId` 的数据。

### F. Debug heartbeat 手动验证

- [ ] 扩展 `Tests/Runtime/Program.cs` 的手动 server 模式，增加 `--demo` 参数。
- [ ] `--serve --port 8765` 仍只启动空 server。
- [ ] `--serve --port 8765 --demo` 注册 `/debug/heartbeat` channel。
- [ ] demo 每秒发布一次 JSON payload，例如：

```json
{"seq":1,"unixTimeNs":1710000000000000000,"message":"hello foxglove"}
```

- [ ] demo channel descriptor 使用：

```csharp
new AdvertiseChannel
{
    Id = 1,
    Topic = "/debug/heartbeat",
    Encoding = "json",
    SchemaName = "",
    Schema = ""
}
```

- [ ] 控制台打印连接 URL 和预期结果：
  Foxglove 能看到 `/debug/heartbeat` topic，订阅后持续收到 JSON。
- [ ] 按 `Ctrl+C` 时停止 heartbeat timer，并 `Dispose()` runtime。

### G. 自动化测试

- [ ] 新增 `Tests/Runtime/Phase2Validation.cs`。
- [ ] `Program.cs` 默认测试路径接入 `Phase2Validation.Validate()`。
- [ ] Fake transport 测试：`RegisterChannel` 后发生一次 `BroadcastText(advertise)`。
- [ ] Fake transport 测试：`RegisterChannel` 对无 schema JSON channel 会把 `null` schema 字段规范化为空字符串，并按官方快照序列化。
- [ ] Fake transport 测试：重复 `RegisterChannel` 保留已有订阅，重新 publish 仍会发给原 subscriptionId。
- [ ] Runtime API 测试：`FoxgloveRuntime.RegisterChannel` / `Publish` 可代理到当前 session；未启动 session 时抛明确异常。
- [ ] Fake transport 测试：新 client 连接后收到 `serverInfo` 和 channel snapshot，且 snapshot 只发给新 client。
- [ ] Fake transport 测试：`UnregisterChannel` 后发生一次 `BroadcastText(unadvertise)`。
- [ ] Fake transport 测试：`subscribe` 后 `Publish` 会 `SendBinary(clientId, frame)`。
- [ ] Fake transport 测试：`unsubscribe` 后再次 `Publish` 不再发送。
- [ ] Fake transport 测试：未知 channel 的 subscribe 不产生发送。
- [ ] Binary 测试：解码 `SendBinary` frame，断言 `subscriptionId`、`logTimeNs`、payload。
- [ ] 多 client 测试：两个 client 订阅同一 channel，收到各自 subscriptionId。
- [ ] 集成测试：`ClientWebSocket` 连接后读取 `serverInfo` 和 `advertise`。
- [ ] 集成测试：client 发送 `subscribe`，server 调用 `Publish`，client 收到 binary `MessageData`。
- [ ] 负向集成测试：client 发送 `unsubscribe` 后，短 timeout 内不应再收到 `MessageData`。
- [ ] 所有测试失败时必须抛异常，并让进程返回非 0。

### H. 文档更新

- [ ] 更新 `Documentation~/README.md`，说明 Phase 2 支持 topic advertise、subscribe、publish。
- [ ] 更新手动验证命令：

```powershell
dotnet run --project "Packages\dev.unityfoxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj" -- --serve --port 8765 --demo
```

- [ ] 更新 `Documentation~/Architecture.md`，记录 Phase 2 数据流：
  channel registry、subscription registry、publish routing。
- [ ] 明确写出 Phase 2 的 debug heartbeat 不是正式 schema 可视化能力。
- [ ] 把 `Architecture.md` 的 Phase 2 状态从 `Planned` 改为实施中或完成，取决于实现进度。

## 建议执行顺序

1. 先做 `A. 协议字段与 DTO 收口`
2. 再做 `G. 自动化测试` 的 fake transport 失败用例
3. 实现 `B. 公开 API 与 Registry`
4. 实现 `C. 连接后的 advertise snapshot`
5. 实现 `D. subscribe / unsubscribe 解析`
6. 实现 `E. Publish 与 MessageData 路由`
7. 增加 `F. Debug heartbeat 手动验证`
8. 最后补 `H. 文档更新`

## 验收矩阵

### 自动化验收

- [ ] `dotnet run --project "Packages\dev.unityfoxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj"` 返回 0。
- [ ] 输出包含 Phase 0、Phase 1、Phase 2 三组验证。
- [ ] `advertise` JSON 字段与官方 v1 快照一致。
- [ ] `subscribe` 使用 `subscriptions: [{ id, channelId }]`。
- [ ] `unsubscribe` 使用 `subscriptionIds`。
- [ ] `Publish` 发送的 binary frame 使用 `subscriptionId`，不是 `channelId`。
- [ ] unsubscribe 后消息停止。
- [ ] 多客户端订阅同一 channel 时，各自收到自己的 subscriptionId。

### 手动验收

- [ ] 启动 demo server 后，Foxglove 可连接 `ws://127.0.0.1:8765`。
- [ ] Foxglove Topics 面板能看到 `/debug/heartbeat`。
- [ ] 订阅后能持续收到 heartbeat JSON。
- [ ] 断开、重连后仍能收到 `serverInfo` + `advertise`。
- [ ] 关闭 Foxglove 不导致 server 崩溃。

## 风险检查

- [ ] 不要把 `subscriptionId` 和 `channelId` 混用。
- [ ] 不要在发送 binary frame 时持有 `SubscriptionRegistry` lock。
- [ ] 不要把 `clientPublish` capability 加进 `serverInfo`。
- [ ] 不要把 `time` capability 加进 `serverInfo`，除非真正实现并广播 time frame。
- [ ] 不要在 Phase 2 引入官方 schema 或 3D 可视化 DTO。
- [ ] 不要让 `Channels.Register(...)` 直写 registry 后绕过 advertise；文档推荐使用 `RegisterChannel(...)`。
- [ ] 不要让未知 `op` 或非法 JSON 断开 Foxglove 连接。

## 下一阶段入口

> [!success]
> Phase 2 完成后，进入 [[00_PLAN]] 里的 Phase 3：对齐官方 schema，优先支持 `foxglove.FrameTransform` 和 `foxglove.SceneUpdate`，让 Foxglove 3D 面板真正显示 Unity 数据。
