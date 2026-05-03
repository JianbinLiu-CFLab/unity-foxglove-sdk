---
title: Foxglove Unity SDK Phase 8 执行计划
aliases:
  - Phase 8 Plan
  - FoxgloveSDK Phase 8
tags:
  - plan
  - phase8
  - todo
  - unity
  - foxglove
  - websocket
  - connectiongraph
  - clientpublish
status: draft
updated: 2026-05-02
---

# Foxglove Unity SDK Phase 8 执行计划

> [!summary]
> 本文是从 [[00_PLAN]] 拆出的 Phase 8 执行版，承接 [[08_PHASE7_PLAN]] 已完成的 bug 修复、DX 提升和 Time capability。Phase 8 新增两个协议能力：ConnectionGraph（连接拓扑可视化）和 ClientPublish（Foxglove 向 Unity 双向通信）。Assets + PlaybackControl 进入 [[10_PHASE9_PLAN]]，MCAP 进入 Phase 10 单独计划。

## 对应关系

- 上位计划：[[00_PLAN]]
- 上一阶段：[[08_PHASE7_PLAN]]
- 对应阶段：`Phase 8 - ConnectionGraph + ClientPublish`
- 当前包路径：`Packages/dev.unity2foxglove.sdk`
- Unity 验证项目：`Untiy2Foxglove`
- 根命名空间：`Unity.FoxgloveSDK`
- 官方协议参考：<https://github.com/foxglove/ws-protocol/blob/main/docs/spec.md>
- 官方 C FFI 参考：`foxglove-sdk/c/include/foxglove-c/foxglove-c.h`

## 本阶段目标

- 实现 `connectionGraph` capability：客户端可订阅连接拓扑，服务端推送 publisher/subscriber 关系。
- 实现 `clientPublish` capability：Foxglove 客户端可向 Unity 发布消息，实现双向通信。
- 提供 Unity 主线程安全的 client message 回调，方便业务层处理 Foxglove 发来的数据。
- ConnectionGraph 与 ClientPublish 联动：client advertise/unadvertise 时自动更新 graph。
- 保持 Core / Protocol / Transport / Schemas 层不引用 `UnityEngine`。
- 新增 Phase 8 自动化测试，保持 Phase 0-7 全部回归通过。

## Phase 7 Deferred Items

Phase 7 review 中发现 6 项可推迟，纳入 Phase 8 候选：

| 项 | 原因 | Phase 8 处理 |
|----|------|-------------|
| `FoxgloveServiceCall.InterlockedComplete/Fail` 改名 | 内部方法名，不影响功能 | 不改 |
| `FoxglovePublisher<T>.SchemaName` 每次 getter 反射 | 10Hz 下可忽略 | 不改 |
| `FoxglovePublisher<T>` 无内置 `Update()` | 设计选择，Camera async callback 不适用 | 不改 |
| `FoxgloveTransformPublisher` / `FoxgloveSceneCubePublisher` 未迁移到泛型 | Camera 不迁移，Transform/SceneCube 暂留 PublisherBase | 不改 |
| `FoxgloveParameterComponent` | Inspector 参数注册组件，nice-to-have DX | 纳入本阶段 |
| `FoxgloveManager` 连接状态事件 (OnClientConnected/OnClientDisconnected) | 高级场景再要 | 纳入本阶段 |

## 本阶段不做

- 不实现 MCAP 写入、读取、双写（进入 Phase 10）。
- 不实现 Assets / fetchAsset。
- 不实现 PlaybackControl。
- 不实现 `[FoxgloveLog]` attribute 或 source generation。
- 不切换到 Native Backend。
- 不支持 ros1 / cdr / protobuf client publish encoding；Phase 8 只支持 `json`。

## 当前约定

> [!important]
> Phase 8 是双向通信扩展阶段。ClientPublish 只在 serverInfo 声明了 `clientPublish` capability 时才接受客户端发布。ConnectionGraph 只在有订阅者时才推送更新，避免无订阅者时的无用计算。

- `serverInfo.capabilities` 在 Phase 8 完成后应包含：
  `parameters`、`parametersSubscribe`、`services`、`time`、`connectionGraph`、`clientPublish`。
- `serverInfo.supportedEncodings` 应包含：
  `json`。
- client binary MessageData 解码复用 Phase 2 已有的 `BinaryEncoding.TryDecodeClientMessageData()`。

## 目标文件结构

- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/ConnectionGraphRegistry.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase8Validation.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Protocol/JsonMessages.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveSession.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveManager.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Documentation~/README.md`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Documentation~/Architecture.md`
- 修改：
  `00_PLAN.md`

---

## Batch 5：ConnectionGraph

### A. Protocol DTO

- [ ] 新增 `SubscribeConnectionGraph` DTO（client→server）：
  `op = "subscribeConnectionGraph"`，无额外字段。
- [ ] 新增 `UnsubscribeConnectionGraph` DTO（client→server）：
  `op = "unsubscribeConnectionGraph"`，无额外字段。
- [ ] 新增 `ConnectionGraphUpdate` DTO（server→client）：
  `op = "connectionGraphUpdate"`。
- [ ] `ConnectionGraphUpdate` 字段：
  `publishedTopics`：`List<PublishedTopic>`，元素形状 `{ name, publisherIds: string[] }`。
  `subscribedTopics`：`List<SubscribedTopic>`，元素形状 `{ name, subscriberIds: string[] }`。
  `advertisedServices`：`List<AdvertisedService>`，元素形状 `{ name, providerIds: string[] }`。
  `removedTopics`、`removedServices`：用于增量更新。
- [ ] 新增 `PublishedTopic` / `SubscribedTopic` / `AdvertisedService` DTO，字段命名分别为 `name`、`publisherIds` / `subscriberIds` / `providerIds`。
- [ ] graph 中的 id 使用 string，不使用 `uint` client ID 直接作为 wire 类型。建议首版约定：
  server publisher id = `"unity"` 或 `"unity:<channelId>"`；
  client publisher id = `"client:<clientId>:<channelId>"`；
  subscriber id = `"client:<clientId>:<subscriptionId>"`；
  service provider id = `"unity"`。
- [ ] 字段命名严格按照官方协议 camelCase。

### B. ConnectionGraphRegistry

- [ ] 新增 `ConnectionGraphRegistry`，维护连接拓扑快照。
- [ ] 维护 per-client 的 connectionGraph 订阅状态（订阅/未订阅）。
- [ ] 当 server channel advertise/unadvertise 时更新 `publishedTopics`，覆盖 `/tf`、`/scene`、`/unity/camera` 这类 Unity 自己发布的 topic。
- [ ] 当 channel 被 subscribe/unsubscribe 时更新 graph。
- [ ] 当 client advertise/unadvertise（ClientPublish）时更新 graph。
- [ ] 当 service 被注册/反注册时更新 graph。
- [ ] 提供 `GetSnapshot()` 返回当前完整拓扑。
- [ ] 提供 `GetDelta(lastVersion)` 返回增量更新（可选优化，首版可全量推送）。

### C. FoxgloveSession 路由

- [ ] `serverInfo.capabilities` 增加 `Capability.ConnectionGraph`。
- [ ] `OnClientText` 新增 case：`subscribeConnectionGraph`。
- [ ] `OnClientText` 新增 case：`unsubscribeConnectionGraph`。
- [ ] 客户端 subscribe 后立即推送当前 graph 快照。
- [ ] server channel advertise/unadvertise、channel subscribe/unsubscribe、service register/unregister 时，向所有 graph 订阅者推送更新。
- [ ] client disconnect 时清理 graph 订阅状态。

### Batch 5 测试

- [ ] 测试：`serverInfo.capabilities` 包含 `connectionGraph`。
- [ ] 测试：`subscribeConnectionGraph` 后收到 `connectionGraphUpdate`。
- [ ] 测试：`connectionGraphUpdate` wire JSON 使用数组对象结构，包含 `name` + `publisherIds` / `subscriberIds` / `providerIds`，不是 dictionary。
- [ ] 测试：server channel advertise 后 graph 的 `publishedTopics` 包含 Unity 发布的 topic。
- [ ] 测试：channel subscribe 触发 graph update 推送给 graph 订阅者。
- [ ] 测试：`unsubscribeConnectionGraph` 后不再收到 graph update。
- [ ] 测试：client disconnect 后 graph 订阅清理。
- [ ] DTO 字段名测试。

---

## Batch 6：ClientPublish

### D. Protocol DTO

- [ ] 新增 `ClientAdvertise` DTO（client→server）：
  `op = "advertise"`（注意：client 使用与 server 相同的 op 名，但方向相反，协议通过 capability 区分）。
  字段：`channels: [{ id, topic, encoding, schemaName, schemaEncoding?, schema? }]`。
- [ ] 新增 `ClientUnadvertise` DTO（client→server）：
  `op = "unadvertise"`。
  字段：`channelIds: [uint]`。

> [!note]
> 根据官方协议，client advertise/unadvertise 使用与 server 相同的 `"advertise"` / `"unadvertise"` op 名。需要在 `OnClientText` 中根据当前是否声明了 `clientPublish` capability 来区分方向。

### E. Client Channel Registry

- [ ] `FoxgloveSession` 内部维护 `Dictionary<(uint clientId, uint channelId), ClientChannelDescriptor>` 或 per-client nested dictionary；client channelId 只在单个 client 内唯一，不能全局唯一假设。
- [ ] `ClientChannelDescriptor` 包含：`clientId`、`channelId`（client 分配）、`topic`、`encoding`、`schemaName`、可选 `schemaEncoding`、可选 `schema`。
- [ ] client advertise 时注册到 registry。
- [ ] client unadvertise 时移除。
- [ ] client disconnect 时清理该 client 的所有 channel。

### F. Client Binary MessageData 处理

- [ ] `OnClientBinary` 增加 client MessageData 处理分支：
  如果 opcode 是 `ClientOpcode.MessageData`，调用 `BinaryEncoding.TryDecodeClientMessageData(data, out channelId, out payload)`。
- [ ] 使用 `(clientId, channelId)` 验证 channel 是否在 client channel registry 中。
- [ ] 将 message 入队或直接通过事件分发。
- [ ] `FoxgloveSession` 新增事件 `event Action<uint clientId, uint channelId, string topic, byte[] payload> OnClientMessage`。

### G. FoxgloveManager 暴露 client message

- [ ] `FoxgloveManager` 新增 `public event Action<uint clientId, uint channelId, string topic, byte[] payload> OnClientMessage`。
- [ ] 从 session 的 `OnClientMessage` 事件转发，通过 `ConcurrentQueue` 在 `Update()` 主线程中 drain。
- [ ] 业务层可以安全地在 handler 中访问 Unity API。

### H. ServerInfo 与 capability 声明

- [ ] `serverInfo.capabilities` 增加 `Capability.ClientPublish`。
- [ ] `serverInfo.supportedEncodings` 在有 `clientPublish` 时必须声明（当前已有 `["json"]`）。
- [ ] 只有声明了 `clientPublish` 才接受 client 的 advertise/unadvertise/MessageData。

### I. ConnectionGraph 联动

- [ ] client advertise 时通知 ConnectionGraphRegistry 更新 `publishedTopics`。
- [ ] client unadvertise 时通知 ConnectionGraphRegistry 移除。
- [ ] graph update 推送给 graph 订阅者。

### Batch 6 测试

- [ ] 测试：`serverInfo.capabilities` 包含 `clientPublish`。
- [ ] 测试：`serverInfo.supportedEncodings` 包含 `json`。
- [ ] 测试：client advertise 注册 channel 到 client channel registry。
- [ ] 测试：两个不同 client 都 advertise channelId=1 时互不覆盖，MessageData 分别路由到正确 client/channel。
- [ ] 测试：client unadvertise 移除 channel。
- [ ] 测试：client binary MessageData 正确解码并触发 OnClientMessage 事件。
- [ ] 测试：未知 channelId 的 client message 被忽略。
- [ ] 测试：client disconnect 清理 client channel registry。
- [ ] 测试：client advertise 触发 connectionGraph update（如果有 graph 订阅者）。
- [ ] 保持 Phase 0-7 全部验证通过。

---

## 建议执行顺序

1. 先做 Batch 5（ConnectionGraph），跑通测试。
2. 做 Batch 6（ClientPublish），包括与 ConnectionGraph 的联动。
3. 跑完整 dotnet 验证。
4. 跑 Unity Editor 手动验收。
5. 跑 Windows IL2CPP Player 验收。
6. 更新文档和 00_PLAN.md。

## 验收矩阵

### 自动化验收

- [ ] Phase 0-8 dotnet validation 全部通过。
- [ ] `serverInfo.capabilities` 包含 `connectionGraph` 和 `clientPublish`。
- [ ] ConnectionGraph subscribe/unsubscribe/update 行为符合协议。
- [ ] ClientPublish advertise/unadvertise/MessageData 行为符合协议。
- [ ] client disconnect 清理所有相关状态。
- [ ] Phase 0-7 无回归。

### 手动验收

- [ ] Editor Play Mode 连接 Foxglove 成功。
- [ ] Foxglove Connection Graph panel 显示 Unity 节点拓扑。
- [ ] channel subscribe/unsubscribe 时 graph 实时更新。
- [ ] Foxglove 可向 Unity 发送数据（使用 Foxglove 的 publish panel 或自定义 extension）。
- [ ] Unity Console 显示收到的 client message。
- [ ] 3D / Camera / Parameters / Services / Time 不回归。
- [ ] IL2CPP Player 构建通过。
- [ ] IL2CPP Player 下重复上述验收成功。

## 风险与注意事项

- ConnectionGraph 性能风险：
  每次 subscribe/unsubscribe 都推送全量 graph 可能在 channel 多时带宽开销大。首版全量推送，后续可优化为增量。
- ClientPublish 安全风险：
  client 可以 advertise 任意 topic。不做验证可能导致 topic 冲突。首版不做限制，文档说明 clientPublish 信任模型。
- op 名冲突风险：
  client 和 server 都使用 `"advertise"` / `"unadvertise"` op 名。需要在代码中根据 capability 声明和消息方向正确区分。建议通过检查消息内容（是否有 `channels` 字段 vs `channelIds` 字段）来区分。
- client channelId 作用域风险：
  channelId 是 client 分配且只在该 client 内唯一，registry 和测试都必须使用 `(clientId, channelId)` 复合键。
- IL2CPP 风险：
  新增 DTO 继续使用 `MemberSerialization.OptIn`，link.xml 已覆盖。
- 线程风险：
  client message 必须通过 queue + drain 转发到 Unity 主线程，不能在 transport 回调线程直接触发 Unity 事件。

## 后续阶段预留

- Phase 9：[[10_PHASE9_PLAN]] — Assets + PlaybackControl。
- Phase 10：MCAP 录制 / 双写。
- Phase 11 候选：`[FoxgloveLog]` attribute + Editor-time source generation。
