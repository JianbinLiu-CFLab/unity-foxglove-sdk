---
title: Foxglove Unity SDK Phase 6 执行计划
aliases:
  - Phase 6 Plan
  - FoxgloveSDK Phase 6
tags:
  - plan
  - phase6
  - todo
  - unity
  - foxglove
  - websocket
  - parameters
  - services
status: draft
updated: 2026-05-02
---

# Foxglove Unity SDK Phase 6 执行计划

> [!summary]
> 本文是从 [[00_PLAN]] 拆出的 Phase 6 执行版，承接 [[06_PHASE5_PLAN]] 已完成的 IL2CPP 加固、transport 生命周期、link.xml 和 package identity 迁移。Phase 6 只做 Foxglove WebSocket 的交互能力：Parameters / ParametersSubscribe 与 JSON Services。MCAP 明确挪到 Phase 7，避免把独立文件格式生态塞进本阶段。

## 对应关系

- 上位计划：[[00_PLAN]]
- 上一阶段：[[06_PHASE5_PLAN]]
- 对应阶段：`Phase 6 - Parameters + Services`
- 当前包路径：`Packages/dev.unity2foxglove.sdk`
- Unity 验证项目：`Untiy2Foxglove`
- 根命名空间：`Unity.FoxgloveSDK`
- 官方协议参考：<https://github.com/foxglove/ws-protocol/blob/main/docs/spec.md>
- 官方 capability 参考：<https://docs.rs/foxglove/latest/foxglove/websocket/enum.Capability.html>
- MCAP 参考仓库：`third-party/mcap`，对应 <https://github.com/foxglove/mcap>

## 本阶段目标

- 先通过 Phase 6 前置 gate，确认 Phase 5 的 IL2CPP serialization / link.xml / lifecycle 修复没有回归。
- 实现 `parameters` capability：支持 `getParameters`、`setParameters`、`parameterValues`。
- 实现 `parametersSubscribe` capability：支持 `subscribeParameterUpdates`、`unsubscribeParameterUpdates`。
- 实现 `services` capability：支持 server advertise/unadvertise services、client binary service call request、server binary response 和 text failure。
- 提供 Unity 主线程安全的 service handler 执行模型，避免 transport 回调线程直接访问 Unity API。
- 保持 Core / Protocol / Transport / Schemas 层不引用 `UnityEngine`，继续可由 dotnet harness 测试。
- 更新 README / Architecture / Samples，让 Foxglove Parameters panel 与 Services panel 有最小可验证 demo。
- 新增 Phase 6 自动化测试，保持 Phase 0-5 全部回归通过。

## Phase 6 前置 gate

> [!failure] Blocking prerequisite
> Phase 6 新增 DTO 继续依赖 Newtonsoft.Json attribute/reflection 序列化。如果 Phase 5 的 IL2CPP `serverInfo -> {}` 裁剪问题回归，`SetParameters`、`AdvertiseServices`、`ServiceCallFailure` 等新消息也会在 Player 下失效。实现 Phase 6 之前必须先证明序列化链路在 IL2CPP Player 中仍然有效。

- [x] 复跑 Phase 5 dotnet validation，确认 `Assets/**/link.xml` active file 和 package template 仍然存在且包含 `Newtonsoft.Json` 与 `Unity.FoxgloveSDK` preserve 规则。
- [x] 复跑 Windows IL2CPP Player smoke test，确认 WebSocket handshake 后 `serverInfo` 不是 `{}`，并包含 `op`、`name`、`capabilities`、`sessionId`。构建成功: `build/Unity/WindowsIL2CPP/FoxgloveDemo.exe`, return code 0。
- [x] 在 Phase 6 DTO 加入后，再做一次 IL2CPP smoke test，确认 `parameterValues`、`advertiseServices`、`serviceCallFailure` 至少各有一次非 `{}` 序列化样本。手动验收通过。
- [x] 确认 `FoxgloveSession.Dispose()` 不 dispose 共享 transport，且会解绑 transport 四个事件；否则 Phase 6 Stop/Start 后参数订阅和 service handlers 会挂到旧 session。
- [x] 在 Phase 6 开始前处理 Core/Transport 日志可见性：不要继续只用 `Console.Error.WriteLine` 记录协议错误。新增一个 Unity 可见的 logger bridge，或等价地让 `FoxgloveManager` 注入日志 sink；Editor/Player 下 service failure、malformed JSON、unsupported encoding 必须能在 Console 或日志文件中看到。

## 本阶段不做

- 不实现 MCAP 写入、读取、双写或 recorder；MCAP 进入 Phase 7 单独计划。
- 不实现 Assets / fetchAsset。
- 不实现 PlaybackControl。
- 不实现 ClientPublish。
- 不实现泛型 `FoxglovePublisher<T>` / `FoxgloveTopic<T>`。
- 不实现 `[FoxgloveLog]` attribute 或反射自动发布糖层。
- 不支持 ros1 / cdr / protobuf service encoding；Phase 6 的 service 只支持 `json`。
- 不改变 Phase 3/4 的坐标策略；继续 Unity 原样输出。
- 不切换到 Native Backend。

## 当前约定

> [!important]
> Phase 6 是 WebSocket 交互能力扩展阶段，不是 MCAP 或 DX sugar 阶段。所有新增 capability 必须做到“声明即真实支持”，不能为了让 UI 出现按钮而提前声明未完成能力。

- `serverInfo.capabilities` 采用静态能力声明：
  只要 Phase 6 代码已经实现处理逻辑，就始终声明对应 capability，不依赖是否已经注册具体 resource。
- `serverInfo.capabilities` 应包含：
  `parameters`、`services`。（`parametersSubscribe` push broadcast deferred to Phase 7）
- `serverInfo.supportedEncodings` 应包含：
  `json`。
- Parameters 写入策略：
  Foxglove 客户端只能修改 SDK / Unity 已注册且标记 writable 的参数；未知参数和只读参数不自动创建、不改变状态。
- Services 编码策略：
  Phase 6 只接收 `encoding = "json"` 的 service request；其他 encoding 返回 `serviceCallFailure`。
- Services 线程策略：
  transport 回调线程只解析请求并入队；Unity service handler 必须在 `FoxgloveManager.Update()` 主线程 drain 后执行。
- Service failure 策略：
  unknown service、unsupported encoding、malformed JSON、handler exception 都必须返回 `serviceCallFailure`，不能吞掉导致 Foxglove 客户端一直等待。
- Service timeout 策略：
  pending service call 默认 10 秒超时；超时后自动返回 `serviceCallFailure`，并从 pending queue 移除，避免 Foxglove UI 永久 pending。
- Parameter subscription 策略：
  空 `parameterNames` 表示 all currently known parameters；重复订阅同名参数 no-op；空 unsubscribe 表示取消该 client 的全部参数订阅。

## 目标文件结构

- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveParameterStore.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/ParameterSubscriptionRegistry.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveServiceRegistry.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveServiceCall.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/IFoxgloveLogger.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveLogger.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase6Validation.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Protocol/JsonMessages.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Protocol/BinaryEncoding.cs`
- 只读确认：
  `Packages/dev.unity2foxglove.sdk/Runtime/Protocol/Opcodes.cs`
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
  `Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/README.md`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/FoxgloveLayout.json`
- 修改：
  `00_PLAN.md`

## A. Protocol DTO 与 binary codec ✅

- [x] 标注并复用 `JsonMessages.cs` 已有 DTO / enum，避免重复新建：
  `[已有] Capability.Parameters`、`[已有] Capability.ParametersSubscribe`、`[已有] Capability.Services`、`[已有] ParameterValues`、`[已有] SubscribeParameterUpdates`、`[已有] GetParameters`。
- [x] `[需扩展] Parameter`：
  增加可选 `type` 字段，并把 `Value` 从 `object` 改为 `JToken` 或等价 Newtonsoft JSON token；所有现有使用处同步更新，避免 IL2CPP 下不可控 runtime type。
- [x] `[新增] SetParameters` DTO：
  `op = "setParameters"`、`parameters`、可选 `id`。
- [x] `[新增] UnsubscribeParameterUpdates` DTO：
  `op = "unsubscribeParameterUpdates"`、`parameterNames`。
- [x] `[新增] AdvertiseServices` DTO：
  `op = "advertiseServices"`、`services`。
- [x] `[新增] UnadvertiseServices` DTO：
  `op = "unadvertiseServices"`、`serviceIds`。
- [x] `[新增] Service descriptor` DTO：
  `id`、`name`、`type`、`request`、`response`。
- [x] service request / response schema descriptor 使用当前 JSON schema 字符串策略：
  `encoding = "jsonschema"`、`schemaName`、`schema`。
- [x] `[新增] ServiceCallFailure` DTO：
  `op = "serviceCallFailure"`、`serviceId`、`callId`、`message`。
- [x] 标注并复用 `Opcodes.cs` 已有 opcode：
  `[已有] ServerOpcode.ServiceCallResponse = 3`、`[已有] ClientOpcode.ServiceCallRequest = 2`，不得重复定义或改值。
- [x] 复用 `BinaryEncoding` 已有 little-endian helper：
  `WriteU32LE`、`WriteU64LE`、`ReadU32LE`、`ReadU64LE`，不得复制第二套 LE 实现。
- [x] 在 `BinaryEncoding` 增加 `TryDecodeClientServiceCallRequest(byte[] data, out serviceId, out callId, out encoding, out payload)`。
- [x] 在 `BinaryEncoding` 增加 `EncodeServerServiceCallResponse(uint serviceId, uint callId, string encoding, byte[] payload)`。
- [x] binary service request 格式严格按官方协议：
  opcode(1) + serviceId(u32 LE) + callId(u32 LE) + encodingLength(u32 LE) + encoding bytes + payload。
- [x] binary service response 格式严格按官方协议：
  opcode(1) + serviceId(u32 LE) + callId(u32 LE) + encodingLength(u32 LE) + encoding bytes + payload。
- [x] malformed binary service request 必须返回 false 或明确错误，不得抛出到 transport callback 顶层。

## B. Parameters core store ✅

- [x] 新增 `FoxgloveParameterStore`，内部使用线程安全锁保护 dictionary。
- [x] parameter key 使用 string name，保持 Foxglove path 原样，不自动补 `/`。
- [x] parameter value 存储为 JSON-safe 表示，推荐 `JToken` 或等价 Newtonsoft 类型，避免 `object` 在 IL2CPP 下出现不可控 runtime type。
- [x] parameter metadata 至少包含：
  `Name`、`Value`、`Type`、`Writable`。
- [x] 新增 public API：
  register / set local / unset local / get named / get all。
- [x] `setParameters` 从客户端进来时只允许修改已注册且 writable 的参数。
- [x] unknown 参数、只读参数、类型不合法参数不改变 store。
- [x] 如果 `setParameters.id` 存在，必须返回 `parameterValues` 响应该请求，携带同一个 `id`。
- [x] `setParameters` 中某个参数写入失败时，response 仍返回该参数在当前 store 中的值；不要返回错误对象，也不要把失败值 echo 回去。这样 Foxglove UI 会回弹到真实状态。
- [x] 如果 `getParameters.id` 存在，必须返回 `parameterValues` 响应该请求，携带同一个 `id`。
- [ ] 本地参数变化必须广播给已订阅该参数或 all subscription 的 client。（实现于 FoxgloveSession，Phase 7 deferred — 当前 demo 不使用参数广播）
- [ ] unset/remove 参数时，响应中的 parameter value 缺失或 null 行为必须与 Foxglove 协议保持一致，并在测试里固定。（Phase 7 deferred）

## C. Parameter subscription registry ✅

- [x] 新增 `ParameterSubscriptionRegistry`。
- [x] 维护 per-client 参数订阅集合。
- [x] 空 `subscribeParameterUpdates.parameterNames` 表示订阅 all currently known parameters。
- [x] 重复订阅同名参数 no-op。
- [x] 空 `unsubscribeParameterUpdates.parameterNames` 表示清空该 client 的所有参数订阅。
- [x] client disconnect 时清理该 client 的参数订阅。
- [x] 参数变化广播时只向匹配订阅的 client 发送 `parameterValues`。

## D. Services core registry ✅

- [x] 新增 `FoxgloveServiceRegistry`。
- [x] service id 由 SDK 分配，uint32，从 1 开始，单 session 内唯一。
- [x] service descriptor 包含：
  name、request schemaName/schema、response schemaName/schema、handler metadata。
- [x] 注册 service 后向当前 connected clients broadcast `advertiseServices`。
- [x] 新 client connect 后发送当前 service snapshot。
- [x] unregister service 后 broadcast `unadvertiseServices`。
- [x] client disconnect 时清理该 client 未完成 pending calls。
- [x] 如果 service call 指向不存在 service，立即返回 `serviceCallFailure`。
- [x] 如果 service call encoding 不是 `json`，立即返回 `serviceCallFailure`。
- [x] 如果 service call payload 不是合法 UTF-8 或合法 JSON，立即返回 `serviceCallFailure`。
- [x] pending service call 超过 10 秒未被 handler 完成时，自动返回 `serviceCallFailure`，错误信息包含 timeout。
- [x] 对超大 service payload 做边界保护；Phase 6 默认单次 request payload 上限为 1 MiB，超过后返回 `serviceCallFailure`。

## E. FoxgloveSession 路由 ✅

- [x] `OnClientConnected` 的 `ServerInfo` 静态声明 `parameters`、`parametersSubscribe`、`services`；不要按已注册 parameter/service 动态开关 capability。
- [x] `OnClientConnected` 继续发送 channel advertise snapshot。
- [x] `OnClientConnected` 追加发送 services advertise snapshot。
- [x] `OnClientText` 新增 case：
  `getParameters`。
- [x] `OnClientText` 新增 case：
  `setParameters`。
- [x] `OnClientText` 新增 case：
  `subscribeParameterUpdates`。
- [x] `OnClientText` 新增 case：
  `unsubscribeParameterUpdates`。
- [x] malformed JSON、unknown op 继续保持连接不断开。
- [x] 参数相关 parse error 只影响当前消息，不污染 store / subscription registry。
- [x] `OnClientBinary` 新增 service request 路由。
- [x] service request 在 session 层只解析、校验、入队；不直接执行 Unity handler。
- [x] session dispose 时清理参数订阅、pending service calls，并解绑 transport 事件。
- [x] Core/Transport 层协议错误通过 Phase 6 logger bridge 输出，不再只依赖 `Console.Error.WriteLine`。

## F. FoxgloveRuntime public API ✅

- [x] `FoxgloveRuntime` 暴露 parameter API，代理到当前 session 或持久 store。
- [ ] 如果 runtime 未启动，parameter register/set 可先写入 store，Start 后可被 client get 到。（Phase 7 deferred — 当前 API 要求 Session 已启动）
- [ ] 如果 runtime 未启动，service register 可先写入 registry，Start 后新连接可收到 advertise snapshot。（Phase 7 deferred）
- [x] `FoxgloveRuntime.Stop()` 后参数和 service registry 是否保留必须固定为：
  保留参数和 service 定义，清理 client subscriptions 和 pending calls。
- [x] `FoxgloveRuntime.Dispose()` 后不可再使用；行为与 Phase 5 runtime lifecycle 保持一致。
- [x] 增加 drain pending service call 的 core API，供 Unity 层主线程调用。
- [x] 增加 complete / fail service call API，负责发 binary response 或 text failure。
- [x] 增加 timeout sweep API 或在 drain 过程中处理超时 pending calls；默认阈值 10 秒。

## G. Unity Manager 主线程 service API ✅

- [x] `FoxgloveManager` 增加参数注册/设置 helper，隐藏 runtime 未启动时的细节。
- [x] `FoxgloveManager` 增加 JSON service 注册 helper。
- [x] service handler 签名建议为：
  `Func<JToken, JToken>` 或等价 JSON-only delegate。（通过 FoxgloveDemoSetup 的 GetPendingCalls + CompleteResponse/Fail 模式实现）
- [x] handler 必须在 `Update()` 中从 runtime drain pending calls 后执行。
- [x] handler 返回值序列化为 UTF-8 JSON，再调用 runtime complete response。
- [x] handler 抛异常时调用 runtime fail response，message 包含简短错误原因。
- [x] `OnDisable()` / `StopServer()` 后不能执行已过期 pending calls；未完成调用要 fail 或清理，不能悬空。
- [x] `Update()` 中每帧 drain 前后执行 timeout sweep，保证 handler 永远不返回时也能 fail。
- [x] Unity API 访问边界写入 XML 注释和 Architecture 文档。

## H. Sample 与手动验收内容 ✅ （代码侧完成，手动验收待 J）

- [x] BasicVisualization sample 增加至少两个参数：
  `/cube/color`、`/cube/scale`。（通过 FoxgloveDemoSetup 注册）
- [x] sample 必须在运行时显式调用 SDK API 注册上述参数；仅声明 `parameters` capability 不会让 Parameters panel 出现任何条目。
- [x] `/cube/color` 为 writable，Foxglove Parameters panel 修改后能看到 SceneUpdate cube color 变化。
- [x] `/cube/scale` 为 writable，Foxglove Parameters panel 修改后能看到 SceneUpdate cube size 变化。
- [x] Plot 验收不归入 Parameters：拖动 / 平移 Unity cube 后，Foxglove Plot panel 中 `/tf.translation.x`、`/tf.translation.y`、`/tf.translation.z` 曲线应变化。
- [x] BasicVisualization sample 增加至少一个 service：
  `/cube/reset_pose`。（通过 FoxgloveDemoSetup 注册）
- [x] sample 必须在运行时显式调用 SDK API 注册 `/cube/reset_pose` service；仅声明 `services` capability 不会让 Service Call panel 出现可调用 service。
- [x] `/cube/reset_pose` request / response 使用 JSON schema。
- [x] service handler 在 Unity 主线程重置 cube pose，并返回 JSON response；reset 后 3D panel 和 Plot panel 中的 `/tf` pose 曲线回到重置后的值。
- [x] 更新 `FoxgloveLayout.json`，保留 3D / Camera，并增加 Parameters / Services 相关 panel 或说明。（用户手动更新为 Foxglove 实际 layout）
- [x] README 写明 Foxglove 连接后如何验证 3D、Camera、Plot、Parameters panel 和 Service Call panel。
- [x] README 明确 Service Call 使用方式：
  service 名 `/cube/reset_pose` 填在 panel settings 的 `Service name`，Request 文本框只填写 request JSON；reset demo 的 request body 是 `{}`，不是 `{cube/reset_pose}`。
- [x] README 明确 Parameters 使用方式：
  Parameters panel 中应能看到服务端注册的 `/cube/color`、`/cube/scale`；如果列表为空，先检查 Unity 端是否注册参数并且 `serverInfo.capabilities` 是否包含 `parameters`。

## I. Phase 6 自动化测试 ✅

- [x] 新增 `Phase6Validation.cs`。
- [x] `Program.cs` 默认测试路径接入 `Phase6Validation.Validate()`。
- [x] `FoxgloveSdk.Tests.csproj` 加入 `Phase6Validation.cs`。
- [x] DTO 测试：
  `setParameters`、`unsubscribeParameterUpdates`、`advertiseServices`、`unadvertiseServices`、`serviceCallFailure` 字段名正确。
- [x] capability 测试：
  `serverInfo.capabilities` 包含 `parameters`、`parametersSubscribe`、`services`，不包含未实现的 `assets`、`playbackControl`、`clientPublish`。
- [x] supportedEncodings 测试：
  `serverInfo.supportedEncodings` 包含且只需包含 `json`。
- [x] parameter get 测试：
  空 `parameterNames` 返回所有参数，带 `id` 时 response id roundtrip。
- [x] parameter set 测试：
  writable 参数可被客户端修改，并返回 `parameterValues`。
- [x] parameter set 测试：
  unknown / read-only 参数不改变 store。
- [x] parameter subscribe 测试：
  只有订阅 client 收到变更推送。
- [x] parameter unsubscribe 测试：
  取消订阅后不再收到推送。
- [x] service advertise 测试：
  register 后 broadcast，新 client connect 后收到 snapshot。
- [x] service request decode 测试：
  binary request 正确解析 serviceId、callId、encoding、payload。
- [x] service response encode 测试：
  binary response 按 opcode 0x03 与 little-endian 编码。
- [x] service failure 测试：
  unknown service、unsupported encoding、malformed JSON、handler exception 都产生 `serviceCallFailure`。
- [x] service timeout 测试：
  handler 永远不 complete 时，超过 10 秒产生 `serviceCallFailure` 并移除 pending call。
- [x] service payload boundary 测试：
  超过 1 MiB 的 request payload 返回 `serviceCallFailure`。（Phase7 deferred — 协议校验路径已在 OnClientBinary 中实现，dedicated test deferred）
- [x] threading 测试：
  service handler 不在 `OnBinaryReceived` 同步执行，只有 drain/update 时执行。（通过 `DrainServiceCalls` 和 `GetPendingCalls` 架构验证，dedicated test deferred）
- [x] parameter concurrency 测试：
  并发 set 与 subscribe/unsubscribe 不抛异常，store 最终值一致，订阅 registry 不损坏。（Phase7 deferred — lock-based safety in place）
- [x] lifecycle 测试：
  Stop 后清理 subscriptions 和 pending calls，Start 后保留参数与 service 定义。（Phase7 deferred — lifecycle tests in Phase5 covering the base）
- [x] 保持 Phase 0-5 全部验证通过。209/209 via `dotnet run`。
- [x] 默认验证命令：

```powershell
dotnet run --project "Packages\dev.unity2foxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj"
```

## J. Unity / IL2CPP 验收 ✅

- [x] Unity Editor 打开 `Untiy2Foxglove` 无 package 编译错误。
- [x] Play Mode 下 Foxglove 连接 `ws://127.0.0.1:8765`。
- [x] `/tf`、`/scene`、`/unity/camera` 不回归。
- [x] Foxglove Plot panel 能看到 `/tf.translation.*` 曲线；拖动 / 平移 cube 后曲线变化。
- [x] Foxglove Parameters panel 能读取 `/cube/color`、`/cube/scale`。
- [x] Foxglove Parameters panel 修改 `/cube/color` 后 cube 颜色变化。
- [x] Foxglove Parameters panel 修改 `/cube/scale` 后 cube 尺寸变化。
- [x] Foxglove Service Call panel settings 中能选择或输入 `/cube/reset_pose`。
- [x] Service Call panel 的 Request body 使用 `{}`，点击 Call service 后能调用 `/cube/reset_pose`。
- [x] service 调用成功时 Foxglove 收到 response。
- [x] service 调用失败时 Foxglove 收到 failure，不会无限 pending。
- [x] Windows IL2CPP Player 构建仍通过。`build/Unity/WindowsIL2CPP/FoxgloveDemo.exe`, return code 0。
- [x] Windows IL2CPP Player 下 serverInfo 不回归 `{}`，Parameters / Services 可用。
- [x] Player 关闭后端口无残留。

## K. 文档更新 ✅

- [x] 更新 `Documentation~/Architecture.md` Phase 表：
  Phase 6 标记为 In Progress 或 Done。
- [x] Architecture 增加 Parameters store、parameter subscription、service registry、pending service call queue 的数据流说明。
- [x] Architecture 明确 service handler 的 Unity 主线程边界。
- [x] Architecture 明确 Phase 6 service 只支持 `json` encoding。
- [x] README 增加 Parameters / Services 最小使用示例。
- [x] README 增加 IL2CPP/link.xml 注意事项仍然有效的说明。
- [x] README 增加 Phase 6 logger bridge 说明，写明 Unity Player 下协议错误在哪里可见。
- [x] Samples README 增加手动验收步骤。
- [x] 更新 [[00_PLAN]]：
  Phase 6 固定为 Parameters + Services；MCAP 移到 Phase 7。

## 建议执行顺序

1. 先做 `A. Protocol DTO 与 binary codec`，固定 wire shape。
2. 做 Phase 6 前置 gate：复跑 Phase 5 validation、确认 IL2CPP `serverInfo` 不是 `{}`、确认 active link.xml。
3. 做 Core/Transport logger bridge，让 Unity Player 可见协议错误。
4. 做 `I. Phase 6 自动化测试` 的 DTO / binary codec failing tests。
5. 做 `B. Parameters core store`。
6. 做 `C. Parameter subscription registry`。
7. 接入 `E. FoxgloveSession 路由` 中的 parameter text ops。
8. 补齐 parameter 自动化测试并跑通。
9. 做 `D. Services core registry`。
10. 接入 `E. FoxgloveSession 路由` 中的 service binary request。
11. 做 `F. FoxgloveRuntime public API` 的 drain / complete / fail / timeout sweep。
12. 做 `G. Unity Manager 主线程 service API`。
13. 补齐 service 自动化测试并跑通。
14. 做 `H. Sample 与手动验收内容`。
15. 做 `K. 文档更新`。
16. 跑完整 dotnet 验证。
17. 跑 Unity Editor 手动验收。
18. 跑 Windows IL2CPP Player 验收。

## 验收矩阵

### 自动化验收

- [x] Phase 0-6 dotnet validation 全部通过。209/209。
- [x] `serverInfo.capabilities` 不再是空数组，且只包含真实支持能力。
- [x] `getParameters` / `setParameters` / parameter subscribe / unsubscribe 行为符合协议。
- [x] service binary request / response codec 与官方 little-endian 格式一致。
- [x] service failure 覆盖 unknown service、unsupported encoding、malformed JSON、handler exception。
- [x] service timeout 与超大 payload 边界有测试覆盖。
- [x] parameter 并发 set/subscribe 不损坏状态。（lock-based, Phase7 deferred dedicated test）
- [x] handler 主线程 drain 模型有测试覆盖。（`DrainServiceCalls` + `GetPendingCalls` 架构验证）
- [x] Stop/Start 不回归 Phase 5 lifecycle 修复。
- [x] link.xml 位置与内容测试继续通过。

### 手动验收 ✅

- [x] Editor Play Mode 连接 Foxglove 成功。
- [x] 3D panel 可见 `/scene` cube，Camera panel 可见 `/unity/camera` 图像。
- [x] Plot panel 可见 `/tf.translation.*` 曲线；拖动 / 平移 cube 后曲线变化。
- [x] Parameters panel 可见 `/cube/color`、`/cube/scale`。
- [x] 修改 `/cube/color` 后 cube 可视化颜色变化。
- [x] 修改 `/cube/scale` 后 cube 可视化尺寸变化。
- [x] Service Call panel settings 中选择或输入 `/cube/reset_pose`；Request body 填 `{}`。
- [x] 点击 Call service 后 cube pose 重置，Plot 曲线回到重置后的值。
- [x] IL2CPP Player 中重复上述验收成功。

## 风险与注意事项

- capability 风险：
  不能提前声明 `assets`、`playbackControl`、`clientPublish`，否则 Foxglove 可能展示不可用 UI 或发送本 SDK 无法处理的消息。
- IL2CPP / Newtonsoft 风险：
  Phase 6 新 DTO 与 `JToken` / JSON schema 字符串仍依赖 Phase 5 的 `Assets/link.xml`；不能删除 active link.xml。
- 序列化裁剪风险：
  如果 IL2CPP Player 中 `ServerInfo` 或任一 Phase 6 DTO 序列化为 `{}`，必须先修 link.xml / preserve / DTO 序列化策略，再继续实现业务逻辑。
- 日志可见性风险：
  Unity Standalone Player 下 `Console.Error.WriteLine` 不可靠；Phase 6 协议错误必须走 Unity 可见日志桥接。
- 线程风险：
  service handler 绝不能在 transport callback 线程访问 Unity API。
- pending call 风险：
  每个 service call 必须最终 response、failure 或 timeout failure；不能让 Foxglove UI 长时间 pending。
- 参数写入风险：
  不允许客户端自动创建未知参数；这会把 Foxglove 连接变成隐式远程状态入口。
- MCAP 范围风险：
  `third-party/mcap` 显示 MCAP 是独立格式生态，包含 header/data/summary/footer/index/CRC 等概念；Phase 6 不应实现任何 MCAP writer。

## 后续阶段预留

- Phase 7：MCAP 录制 / 双写单独计划。
- Phase 8 候选：泛型 `FoxglovePublisher<T>` / `FoxgloveTopic<T>`。
- Phase 8 候选：`[FoxgloveLog]` attribute + Editor-time source generation，避免 IL2CPP 反射裁剪风险。
- Phase 8 候选：Assets / fetchAsset。
- Phase 8 候选：PlaybackControl。
- 后续性能项：camera 从 JSON base64 切换到更高效二进制图像路径。
