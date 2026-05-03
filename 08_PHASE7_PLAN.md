---
title: Foxglove Unity SDK Phase 7 执行计划
aliases:
  - Phase 7 Plan
  - FoxgloveSDK Phase 7
tags:
  - plan
  - phase7
  - todo
  - unity
  - foxglove
  - websocket
  - bugfix
  - dx
  - time
status: draft
updated: 2026-05-02
---

# Foxglove Unity SDK Phase 7 执行计划

> [!summary]
> 本文是从 [[00_PLAN]] 拆出的 Phase 7 执行版，承接 [[07_PHASE6_PLAN]] 已完成的 Parameters + Services。Phase 7 有三重目标：(1) 修复 Phase 6 遗留的 10 个 bug 和 plan 偏差；(2) 提升开发者体验（DX），降低接入门槛和样板代码；(3) 新增 Time capability。ConnectionGraph 和 ClientPublish 进入 [[09_PHASE8_PLAN]]，Assets + PlaybackControl 进入 [[10_PHASE9_PLAN]]，MCAP 顺延到 Phase 10。

## 对应关系

- 上位计划：[[00_PLAN]]
- 上一阶段：[[07_PHASE6_PLAN]]
- 对应阶段：`Phase 7 - Bug 修复 + DX 提升 + Time`
- 当前包路径：`Packages/dev.unity2foxglove.sdk`
- Unity 验证项目：`Untiy2Foxglove`
- 根命名空间：`Unity.FoxgloveSDK`
- 官方协议参考：<https://github.com/foxglove/ws-protocol/blob/main/docs/spec.md>

## 本阶段目标

- 修复 Phase 6 code review 发现的全部 bug 和 plan 偏差（10 项）。
- 将 `IFoxgloveLogger` 从 Session 层扩展到 Runtime 和 Transport 全链路，替换所有 `Console.Error.WriteLine`。
- 重构参数和 service 的生命周期：定义由 Runtime 持有，Stop/Start 后保留，支持提前注册。
- 实现 service handler delegate 注册机制，替代手动 `GetPendingCalls()` 轮询。
- 引入泛型 `FoxglovePublisher<TMessage>` 基类，减少 publisher 样板代码。
- 新增 Inspector 参数注册组件 `FoxgloveParameterComponent`。
- 新增 `FoxgloveManager` 连接状态事件，方便业务层响应。
- 新增 `Time` capability，周期性广播 Time frame。
- 保持 Core / Protocol / Transport / Schemas 层不引用 `UnityEngine`，继续可由 dotnet harness 测试。
- 新增 Phase 7 自动化测试，保持 Phase 0-6 全部回归通过。

## 本阶段不做

- 不实现 ConnectionGraph（进入 Phase 8）。
- 不实现 ClientPublish（进入 Phase 8）。
- 不实现 MCAP 写入、读取、双写（进入 Phase 10）。
- 不实现 Assets / fetchAsset。
- 不实现 PlaybackControl。
- 不实现 `[FoxgloveLog]` attribute 或 Editor-time source generation。
- 不切换到 Native Backend。

## 当前约定

> [!important]
> Phase 7 是修复+DX+Time 阶段。所有新增 capability 必须做到"声明即真实支持"。生命周期重构必须确保 Stop/Start 不回归。

- 所有新 DTO 继续使用 `[JsonObject(MemberSerialization.OptIn)]` + `[JsonProperty]` 模式。
- `Assets/link.xml` 保持现有 `preserve="all"` 策略。
- `Runtime/Unity/*.cs` 可以引用 `UnityEngine`；`Runtime/Core`、`Runtime/Protocol`、`Runtime/Transport`、`Runtime/Schemas` 继续保持 dotnet harness 可编译。

## 目标文件结构

- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxglovePublisher.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveParameterComponent.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase7Validation.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/IFoxgloveLogger.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveSession.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveServiceRegistry.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveServiceCall.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/ParameterSubscriptionRegistry.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Transport/ManagedWsBackend.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveManager.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveTransformPublisher.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Unity/FoxgloveSceneCubePublisher.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase6Validation.cs`
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

## Batch 1：基础设施修复

### A. IFoxgloveLogger 全链路连通

- [ ] `FoxgloveRuntime` 构造函数增加可选 `IFoxgloveLogger logger = null` 参数。
- [ ] `FoxgloveRuntime.Start()` 将 logger 传递给 `FoxgloveSession`。
- [ ] `ManagedWsBackend` 构造函数增加可选 `IFoxgloveLogger logger = null` 参数。
- [ ] `FoxgloveRuntime` 将 logger 传递给 `ManagedWsBackend`（通过构造函数或 property setter）。
- [ ] `ManagedWsBackend.cs` 第 68 行 `Console.Error.WriteLine` → `_logger.LogError`。
- [ ] `ManagedWsBackend.cs` 第 76 行 `Console.Error.WriteLine` → `_logger.LogError`。
- [ ] `ManagedWsBackend.cs` 第 119 行 `Console.Error.WriteLine` → `_logger.LogError`。
- [ ] `ManagedWsBackend.cs` 第 151 行 `Console.Error.WriteLine` → `_logger.LogError`。
- [ ] `ManagedWsBackend.cs` 第 294 行 `Console.Error.WriteLine` → `_logger.LogError`。
- [ ] 在 `IFoxgloveLogger.cs` 中新增 `UnityFoxgloveLogger` 实现，使用 `UnityEngine.Debug.LogWarning`/`Debug.LogError`。该类放在 `Runtime/Unity/` 下（可引用 UnityEngine）。
- [ ] `FoxgloveManager.Awake()` 创建 `UnityFoxgloveLogger` 并传入 `FoxgloveRuntime`。
- [ ] dotnet harness 默认使用 `ConsoleLogger`（现有行为不变）。

### B. FoxgloveServiceCall 字段风格统一

- [ ] `FoxgloveServiceCall.Completed` 从 `public int` field 改为 `public bool IsCompleted { get; private set; }`。
- [ ] `FoxgloveServiceCall.ResponsePayload` 改为 property。
- [ ] `FoxgloveServiceCall.ResponseEncoding` 改为 property。
- [ ] `FoxgloveServiceCall.FailureMessage` 改为 property。
- [ ] 移除 `Interlocked.Exchange` 调用，改用调用方已有的 lock 保护。
- [ ] 在 `FoxgloveServiceCall` 上提供 `internal void Complete(string encoding, byte[] payload)` 和 `internal void Fail(string message)` 方法，封装状态变更。
- [ ] `FoxgloveServiceRegistry.CompleteResponse()` 和 `Fail()` 改为调用上述方法。

### C. RemoveClientCalls 直接移除

- [ ] `FoxgloveServiceRegistry.RemoveClientCalls(uint clientId)`：直接从 `_pending` 字典中 Remove 匹配的 entry，不再标记 completed。
- [ ] 断开 client 的 pending call 不走 drain 流程，不尝试向已断开 client 发送 failure。

### D. ParametersSubscribe capability 声明

- [ ] `FoxgloveSession.OnClientConnected()` 的 Capabilities 列表增加 `Capability.ParametersSubscribe`。
- [ ] `Phase6Validation.TestServerInfoCapabilities()` 中 `parametersSubscribe` 断言从 excludes 改为 includes。

### E. ParameterSubscriptionRegistry 空列表语义修正

- [ ] `ParameterSubscriptionRegistry.Subscribe()`：当 `parameterNames` 是非 null 但 `Count == 0` 的空集合时，行为等同于 null（订阅全部）。
- [ ] 当 client 已经有显式订阅时，再发空列表 subscribe，切换为"全部订阅"。
- [ ] `ParameterSubscriptionRegistry.Unsubscribe()`：空 `parameterNames`（`Count == 0`）等同于 null（清空全部订阅）。
- [ ] 调用处 `FoxgloveSession.HandleSubscribeParameterUpdates()` 和 `HandleUnsubscribeParameterUpdates()` 简化：不再判断 `Count > 0`，直接传原始列表给 registry，由 registry 内部处理语义。

### Batch 1 测试

- [ ] 新增 `Phase7Validation.cs`。
- [ ] `Program.cs` 接入 `Phase7Validation.Validate()`。
- [ ] `FoxgloveSdk.Tests.csproj` 加入 `Phase7Validation.cs`。
- [ ] 测试：`serverInfo.capabilities` 包含 `parametersSubscribe`。
- [ ] 测试：logger 注入后 FoxgloveSession 使用注入的 logger 而非默认 ConsoleLogger。
- [ ] 测试：client disconnect 后 pending calls 直接移除，不出现在 DrainCompleted() 中。
- [ ] 测试：FoxgloveServiceCall.Complete()/Fail() 封装正确。
- [ ] 测试：空 parameterNames subscribe 等同于订阅全部。
- [ ] 保持 Phase 0-6 全部验证通过。

---

## Batch 2：生命周期重构

### F. 参数/service 定义从 Session 提升到 Runtime

- [ ] `FoxgloveRuntime` 持有 `FoxgloveParameterStore _parameters` 和 `FoxgloveServiceRegistry _services` 实例（构造时创建）。
- [ ] `FoxgloveRuntime.Parameters` 直接返回 Runtime 持有的参数 store，不再依赖 session。
- [ ] `FoxgloveRuntime.Services` 不再暴露可变 registry 的 `Register()` 入口；public API 必须通过 `FoxgloveRuntime.RegisterService()` / `UnregisterService()`，避免绕过 `advertiseServices` broadcast。
- [ ] 如需查询 service 定义，提供只读视图（例如 `IReadOnlyCollection<ServiceDescriptor>` 或 `GetServicesSnapshot()`），不要让外部调用方拿到可变 `FoxgloveServiceRegistry`。
- [ ] `FoxgloveSession.Services` 同样改为 internal/private 或只读视图；Session 内部可持有 registry 引用，但外部不能直接 `Services.Register()`。
- [ ] `FoxgloveSession` 构造函数接受外部 `FoxgloveParameterStore` 和 `FoxgloveServiceRegistry` 引用，不再内部 new。
- [ ] `FoxgloveRuntime.Start()` 创建 session 时传入 Runtime 持有的 store 和 registry。
- [ ] `FoxgloveRuntime.Stop()` 销毁 session，但 store 和 registry 保留。
- [ ] `FoxgloveSession.ClearSession()` 只清理 channels、subscriptions、paramSubs、pending calls，不清理参数定义和 service 定义。
- [ ] `FoxgloveRuntime.Dispose()` 清理 store 和 registry。

### G. Runtime 未启动时允许提前注册

- [ ] `FoxgloveRuntime.RegisterParameter()` 不再检查 `_session == null`，直接写入 Runtime 的 `_parameters`。
- [ ] `FoxgloveRuntime.RegisterService()` 不再检查 `_session == null`。如果 session 存在则同时 broadcast；如果不存在则只写入 registry。
- [ ] `FoxgloveSession.OnClientConnected()` 发送 service advertise snapshot 时从 Runtime 的 registry 读取。

### H. setParameters 后广播变更给订阅 client

- [ ] `FoxgloveSession.HandleSetParameters()` 中，成功修改参数后，遍历 `_paramSubs` 找到订阅了变更参数（或订阅全部）的 client。
- [ ] 向每个匹配的订阅 client 发送 `parameterValues`，包含变更的参数当前值。
- [ ] 排除请求者自身（请求者已通过 response 收到了当前值）。
- [ ] 如果参数未被成功修改（readonly/unknown），不触发广播。

### I. RegisterService 改为增量 broadcast

- [ ] `FoxgloveSession.RegisterService()` broadcast 时只包含新注册的 service descriptor，不包含全量列表。
- [ ] `AdvertiseServices.Services` 只放单个新增的 descriptor。
- [ ] `OnClientConnected()` 发送 snapshot 时仍为全量。

### Batch 2 测试

- [ ] 测试：Stop/Start 后 `Parameters.GetAllWireParameters()` 保留之前注册的参数。
- [ ] 测试：Stop/Start 后 `Services.GetAll()` 保留之前注册的 service。
- [ ] 测试：外部无法通过 public `Services.Register()` 绕过 `RegisterService()`；已连接 client 的新增 service 必须触发 `advertiseServices`。
- [ ] 测试：Runtime 未启动时 `RegisterParameter()` 不抛异常。
- [ ] 测试：Runtime 未启动时 `RegisterService()` 不抛异常；Start 后新 client 收到 advertise snapshot。
- [ ] 测试：setParameters 后，另一个订阅 client 收到 parameterValues 推送。
- [ ] 测试：setParameters 的请求者不收到重复推送。
- [ ] 测试：未订阅的 client 不收到推送。
- [ ] 测试：RegisterService 后 broadcast 只包含新增 service（增量）。

---

## Batch 3：DX 提升

### J. Service handler delegate 注册

- [ ] `FoxgloveServiceRegistry` 增加 `Dictionary<uint, Func<JToken, JToken>> _handlers`。
- [ ] `FoxgloveServiceRegistry.Register(ServiceDescriptor, Func<JToken, JToken>)` 重载：注册 descriptor 的同时关联 handler。
- [ ] `FoxgloveRuntime.RegisterService(ServiceDescriptor, Func<JToken, JToken>)` 重载，代理到 registry。
- [ ] `FoxgloveSession.DrainServiceCalls()` 改为：
  1. `SweepTimeouts()`
  2. 对每个 pending call：如果有对应 handler，在当前线程（Update 主线程）执行 handler
  3. handler 返回值序列化为 UTF-8 JSON，调用 `CompleteResponse()`
  4. handler 抛异常时调用 `Fail()`，message 包含异常信息
  5. 最后 `DrainCompleted()` 发送所有 response/failure
- [ ] `FoxgloveDemoSetup.cs` 迁移为使用 delegate API：
  ```csharp
  rt.RegisterService(descriptor, req => {
      // reset pose logic
      return JToken.Parse("{\"status\":\"ok\"}");
  });
  ```
- [ ] 移除 `FoxgloveDemoSetup.Update()` 中的手动 `GetPendingCalls()` 轮询。
- [ ] `GetPendingCalls()` 保留为 public API，标注 XML 注释为 advanced usage。

### K. 统一 drain 模型 + ExecutionOrder

- [ ] `FoxgloveManager.Update()` 只承诺在 Unity 主线程 drain service handlers 和发送 response，不承诺先于或晚于所有业务 MonoBehaviour 的 `Update()`。
- [ ] 不把 `[DefaultExecutionOrder(-100)]` 作为语义依赖；如果保留 execution order attribute，只能用于默认体验，不能作为 handler 时序正确性的前提。
- [ ] 对需要严格时序的项目，文档说明两种方案：在 Unity Script Execution Order 中显式排序，或由业务层在自己的 coordinator 中手动调用 `FoxgloveRuntime.Tick()`。
- [ ] `FoxgloveRuntime.Tick()` / `FoxgloveManager.Update()` 的内部顺序写清楚：sweep timeout → execute pending service handlers on current main thread → drain completed responses/failures → broadcast time。
- [ ] 文档说明：handler 中可以安全访问 Unity API，因为它在主线程执行；但它看到的是当前脚本执行顺序下的业务状态，不保证是本帧所有业务 Update 之后的最终状态。

### L. 泛型 FoxglovePublisher<TMessage>

- [ ] 新增 `Runtime/Unity/FoxglovePublisher.cs`。
- [ ] `FoxglovePublisher<TMessage>` 继承 `FoxglovePublisherBase`，约束 `where TMessage : class, new()`。
- [ ] 新增 `[FoxgloveSchema("foxglove.FrameTransform")]` attribute，标注在 message DTO 类上，用于自动推断 schemaName。
- [ ] `FoxglovePublisher<TMessage>` 中 `SchemaName` 自动从 `TMessage` 类型的 `[FoxgloveSchema]` attribute 读取。
- [ ] 提供 `protected abstract TMessage CreateMessage()`，子类实现消息构建。
- [ ] `Update()` 内部处理：`ShouldPublishNow()` → `CreateMessage()` → `Publish(msg, logTimeNs)`。
- [ ] 迁移 `FoxgloveTransformPublisher`：继承 `FoxglovePublisher<FrameTransformMessage>`，`CreateMessage()` 返回构建好的 FrameTransformMessage。
- [ ] 迁移 `FoxgloveSceneCubePublisher`：继承 `FoxglovePublisher<SceneUpdateMessage>`。
- [ ] `FoxgloveCameraPublisher` 保持现有实现（AsyncGPUReadback 回调模式不适合泛型 CreateMessage 模型），但可在基类中复用 rate limiting 等。
- [ ] 在对应 message DTO 类上添加 `[FoxgloveSchema]` attribute：
  `FrameTransformMessage` → `[FoxgloveSchema("foxglove.FrameTransform")]`
  `SceneUpdateMessage` → `[FoxgloveSchema("foxglove.SceneUpdate")]`
  `CompressedImageMessage` → `[FoxgloveSchema("foxglove.CompressedImage")]`

### M. Inspector 参数注册组件

- [ ] 新增 `Runtime/Unity/FoxgloveParameterComponent.cs`。
- [ ] MonoBehaviour，Inspector 中提供一个 `List<ParameterDefinition>` 配置列表。
- [ ] `ParameterDefinition` 是 `[Serializable]` struct，包含 `string name`、`string type`（dropdown: number/string/boolean/number[]）、`string defaultValue`（JSON 字符串）、`bool writable`。
- [ ] `OnEnable()` 时自动查找 `FoxgloveManager`，将列表中的参数注册到 `Runtime.Parameters`。
- [ ] `OnDisable()` 时可选反注册（unregister）。
- [ ] 不强制使用——高级用户仍可代码注册。

### N. FoxgloveManager 连接状态事件

- [ ] `FoxgloveManager` 新增 `public event Action<uint> OnClientConnected`。
- [ ] `FoxgloveManager` 新增 `public event Action<uint> OnClientDisconnected`。
- [ ] 实现方式：transport 的事件在后台线程触发，FoxgloveManager 内部使用 `ConcurrentQueue<(bool isConnect, uint clientId)>` 缓冲，在 `Update()` 中 drain 并触发 Unity 事件。
- [ ] 这样业务层的事件 handler 在主线程执行，可以安全调用 Unity API。
- [ ] `FoxgloveSession` 需暴露或转发 transport 的连接/断开事件（或 FoxgloveManager 直接从 transport 读取）。

### Batch 3 测试

- [ ] 测试：RegisterService with handler delegate → pending call 被 handler 自动处理。
- [ ] 测试：handler 返回值正确序列化为 service response。
- [ ] 测试：handler 抛异常产生 serviceCallFailure。
- [ ] 测试：泛型 `FoxglovePublisher<T>` 从 `[FoxgloveSchema]` attribute 正确读取 schemaName。
- [ ] 测试：`FoxglovePublisher<T>` 调用 `CreateMessage()` 并 publish。
- [ ] 测试：连接状态事件在 fake transport 模拟 connect/disconnect 后触发。

---

## Batch 4：Time capability

### O. Time frame 广播

- [ ] `FoxgloveSession.OnClientConnected()` 的 Capabilities 列表增加 `Capability.Time`。
- [ ] `FoxgloveSession` 新增 `BroadcastTime()` 方法：
  调用 `BinaryEncoding.EncodeTime(_clock.NowNs)` 生成 Time frame，`_transport.BroadcastBinary(frame)` 广播。
- [ ] `FoxgloveSession` 内部维护 Time 广播的频率控制（类似 `ShouldPublishNow()` 的时间间隔检查）。
- [ ] `FoxgloveRuntime` 新增 `Tick()` 或扩展 `DrainServiceCalls()` 为 `Tick()`，内部依次执行：drain service calls → broadcast time。
- [ ] `FoxgloveManager.Update()` 调用 `_runtime.Tick()` 替代单独的 `DrainServiceCalls()`。
- [ ] Time 广播频率可配置，默认 10Hz。
- [ ] `FoxgloveManager` 新增 `[SerializeField] private float _timeFrameRateHz = 10f`，Inspector 可调。
- [ ] 频率传递方式：`FoxgloveRuntime` 暴露 `TimeFrameRateHz` 属性，FoxgloveManager 在 Start/Update 时同步。

### Batch 4 测试

- [ ] 测试：`serverInfo.capabilities` 包含 `time`。
- [ ] 测试：`BroadcastTime()` 发送正确格式的 Time frame（opcode=2, 8 bytes LE nanoseconds）。
- [ ] 测试：Time frame 频率控制：短时间内连续调用只发出预期数量的 frame。
- [ ] 保持 Phase 0-6 全部验证通过。

---

## 建议执行顺序

1. 先做 Batch 1（基础设施修复），跑通全部测试。
2. 做 Batch 2（生命周期重构），确保 Stop/Start 语义正确。
3. 做 Batch 3（DX 提升），迁移现有代码到新 API。
4. 做 Batch 4（Time capability）。
5. 跑完整 dotnet 验证。
6. 跑 Unity Editor 手动验收。
7. 跑 Windows IL2CPP Player 验收。
8. 更新文档和 00_PLAN.md。

## 验收矩阵

### 自动化验收

- [ ] Phase 0-7 dotnet validation 全部通过。
- [ ] `serverInfo.capabilities` 包含 `parameters`、`parametersSubscribe`、`services`、`time`。
- [ ] `serverInfo.capabilities` 不包含未实现的 `assets`、`playbackControl`、`clientPublish`、`connectionGraph`。
- [ ] setParameters 变更广播测试覆盖：订阅者收到、请求者不重复、未订阅者不收到。
- [ ] Stop/Start 后参数和 service 定义保留。
- [ ] Runtime 未启动时参数和 service 注册不抛异常。
- [ ] service handler delegate 执行和异常处理测试覆盖。
- [ ] Time frame 格式和频率控制测试覆盖。
- [ ] 泛型 Publisher schema 自动绑定测试覆盖。
- [ ] Phase 0-6 无回归。

### 手动验收

- [ ] Editor Play Mode 连接 Foxglove 成功。
- [ ] 3D / Camera / Parameters / Services 面板不回归。
- [ ] Foxglove 时间轴与 Unity 时间同步。
- [ ] 修改参数后其他连接的 Foxglove 实例收到变更推送。
- [ ] `/cube/reset_pose` service 通过 delegate handler 执行成功。
- [ ] Unity Console 可见 transport 层错误日志（不再只到 Console.Error）。
- [ ] IL2CPP Player 构建通过。
- [ ] IL2CPP Player 下重复上述验收成功。

## 风险与注意事项

- 生命周期重构风险：
  参数/service 从 Session 提升到 Runtime 是核心架构变更，需确保 Session 对 store/registry 的引用在 Stop/Start 后不悬空。
- handler 线程风险：
  service handler delegate 必须在 `FoxgloveManager.Update()` 主线程中执行。`DrainServiceCalls()` 不能在 transport 回调线程调用。
- 泛型 Publisher IL2CPP 风险：
  `FoxglovePublisher<T>` 使用泛型 + attribute 反射。`[FoxgloveSchema]` attribute 在 IL2CPP 下需确认 `typeof(TMessage).GetCustomAttributes()` 正常工作。link.xml 的 `preserve="all"` 应已覆盖。
- ExecutionOrder 风险：
  `[DefaultExecutionOrder(-100)]` 只影响同一 MonoBehaviour 组的 Update 顺序。如果业务逻辑在 LateUpdate/FixedUpdate 中操作参数，需文档说明。
- Time 广播频率风险：
  过高频率会增加 WebSocket 带宽。默认 10Hz 是合理起点，文档建议不超过 60Hz。
- 增量 advertiseServices 风险：
  Foxglove 客户端是否正确处理多次增量 advertiseServices 需要手动验证。如果有问题，回退到全量。

## 后续阶段预留

- Phase 8：[[09_PHASE8_PLAN]] — ConnectionGraph + ClientPublish。
- Phase 9：[[10_PHASE9_PLAN]] — Assets + PlaybackControl。
- Phase 10：MCAP 录制 / 双写。
- Phase 11 候选：`[FoxgloveLog]` attribute + Editor-time source generation。
