---
title: Foxglove Unity SDK Phase 3 执行计划
aliases:
  - Phase 3 Plan
  - FoxgloveSDK Phase 3
tags:
  - plan
  - phase3
  - todo
  - unity
  - foxglove
status: draft
updated: 2026-05-01
---

# Foxglove Unity SDK Phase 3 执行计划

> [!summary]
> 本文是从 [[00_PLAN]] 拆出的 Phase 3 执行版，承接 [[03_PHASE2_PLAN]] 已完成的 WebSocket、`advertise`、`subscribe` 和 `MessageData` 链路。目标是引入官方 JSON Schema，并用 `foxglove.FrameTransform` + `foxglove.SceneUpdate` 的最小 cube 场景，让 Foxglove 3D 面板真正显示 Unity 风格数据。

## 对应关系

- 上位计划：[[00_PLAN]]
- 上一阶段：[[03_PHASE2_PLAN]]
- 对应阶段：`Phase 3 - Schema 对齐与第一批可视化消息`
- 包路径：`Packages/dev.unityfoxglove.sdk`
- package id：`dev.unityfoxglove.sdk`
- 根命名空间：`Unity.FoxgloveSDK`

## 本阶段目标

- 使用本地 clone 的官方 schema 作为真值源，不手写或改造 schema 结构。
- 支持 typed JSON channel：
  `encoding = "json"`，`schemaEncoding = "jsonschema"`。
- 首批支持：
  `foxglove.FrameTransform`。
- 首批支持：
  `foxglove.SceneUpdate` 的最小 cube primitive。
- 提供最小 DTO 层，让测试和 demo 能稳定序列化出官方字段名。
- 提供 `--demo3d` 手动验证入口，在 Foxglove 3D 面板看到 cube。

## 当前约定

> [!important]
> Phase 3 只做“官方 schema + 最小 3D 可视化闭环”。不做完整 Unity 组件、不做 Inspector、不做泛型 Publisher，不把 Phase 4 的易用 API 提前搬进来。

- Schema 来源：
  `foxglove-sdk/schemas/jsonschema/`
- 本地官方 SDK 版本：
  `foxglove-sdk main@b298c3d1649e6e5dfd77a53b12ab7c27f97c7aba`
- Schema 摘要：
  `FrameTransform.json sha256=9986de138717bfaf`
- Schema 摘要：
  `SceneUpdate.json sha256=7530dfd8585239e5`
- Schema 存储策略：
  将少量官方 JSON schema 复制为 C# 内嵌常量，不在运行时读取 `foxglove-sdk` clone。
- 坐标策略：
  Unity 原样输出，root frame 使用 `unity_world`，不做 handedness / ENU / ROS 坐标转换。
- SceneUpdate 范围：
  只实现 cube primitive 的强类型 DTO；其他 primitive required arrays 序列化为空数组。
- `serverInfo.capabilities` 继续保持 `[]`，不因为 schema 支持而声明 `time` 或 `clientPublish`。

## 本阶段不做

- 不做 `foxglove.PoseInFrame`。
- 不做 `foxglove.FrameTransforms` 批量消息。
- 不做完整 `SceneUpdate` primitive 全家桶：
  arrows、spheres、cylinders、lines、triangles、texts、models 都先不实现强类型。
- 不新增 JSON Schema validator 依赖。
- 不做 Unity `MonoBehaviour` / Inspector / Demo Scene。
- 不引入 `UnityEngine` 编译依赖；Phase 3 的测试仍应能用当前 dotnet 测试工程跑通。
- 不做 `FoxglovePublisher<T>` 泛型封装。
- 不做 MCAP、Parameters、Services、Assets、PlaybackControl。

## 目标文件结构

- 新增：
  `Packages/dev.unityfoxglove.sdk/Runtime/Schemas/FoxgloveSchemaDefinitions.cs`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Runtime/Schemas/FoxgloveVisualMessages.cs`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Tests/Runtime/Phase3Validation.cs`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Runtime/Core/FoxgloveSession.cs`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Runtime/Core/FoxgloveRuntime.cs`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Tests/Runtime/Program.cs`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Documentation~/README.md`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Documentation~/Architecture.md`

## Todo

### A. 官方 Schema 常量与注册

- [ ] 新增 `FoxgloveSchemaDefinitions`。
- [ ] 定义 schema 名称常量：
  `FrameTransformSchemaName = "foxglove.FrameTransform"`。
- [ ] 定义 schema 名称常量：
  `SceneUpdateSchemaName = "foxglove.SceneUpdate"`。
- [ ] 定义 schema encoding 常量：
  `JsonSchemaEncoding = "jsonschema"`。
- [ ] 将 `foxglove-sdk/schemas/jsonschema/FrameTransform.json` 原样复制为 C# verbatim string。
- [ ] 将 `foxglove-sdk/schemas/jsonschema/SceneUpdate.json` 原样复制为 C# verbatim string。
- [ ] 在常量旁注释来源：
  `foxglove-sdk main@b298c3d1649e6e5dfd77a53b12ab7c27f97c7aba`。
- [ ] 增加 `RegisterCoreSchemas(ISchemaRegistry registry)`。
- [ ] `RegisterCoreSchemas` 注册两条 `SchemaEntry`：
  `Name`、`Encoding = "jsonschema"`、`Content = schema json`。
- [ ] 如果 `registry == null`，抛 `ArgumentNullException`。

### B. Visual DTO 收口

- [ ] 新增 `FoxgloveTime`：
  `sec`、`nsec`。
- [ ] `FoxgloveTime` 的 C# 类型固定为：
  `ulong Sec`、`uint Nsec`。
- [ ] 新增 `FoxgloveDuration`：
  `sec`、`nsec`。
- [ ] `FoxgloveDuration` 的 C# 类型固定为：
  `long Sec`、`uint Nsec`。
- [ ] 新增 `FoxgloveVector3`：
  `x`、`y`、`z`。
- [ ] `FoxgloveVector3` 的 C# 类型固定为 `double`。
- [ ] 新增 `FoxgloveQuaternion`：
  `x`、`y`、`z`、`w`。
- [ ] `FoxgloveQuaternion` 的 C# 类型固定为 `double`。
- [ ] 新增 `FoxglovePose`：
  `position`、`orientation`。
- [ ] 新增 `FoxgloveColor`：
  `r`、`g`、`b`、`a`，取值约定为 0 到 1。
- [ ] `FoxgloveColor` 的 C# 类型固定为 `double`。
- [ ] 新增 `FoxgloveKeyValuePair`：
  `key`、`value`。
- [ ] 新增 `FrameTransformMessage`，字段必须是：
  `timestamp`、`parent_frame_id`、`child_frame_id`、`translation`、`rotation`。
- [ ] 新增 `SceneUpdateMessage`，字段必须是：
  `deletions`、`entities`。
- [ ] 新增 `SceneEntityDeletion`，字段必须是：
  `timestamp`、`type`、`id`。
- [ ] `SceneEntityDeletion.type` 使用 int-backed enum：
  `MATCHING_ID = 0`、`ALL = 1`，JSON 序列化必须输出数字。
- [ ] 新增 `SceneEntity`，字段必须是：
  `timestamp`、`frame_id`、`id`、`lifetime`、`frame_locked`、`metadata`、`arrows`、`cubes`、`spheres`、`cylinders`、`lines`、`triangles`、`texts`、`models`。
- [ ] `SceneEntity` 中 Phase 3 不支持的 primitive arrays 必须默认为空数组，不能省略字段。
- [ ] `SceneEntity` 构造函数或字段初始化器必须把所有 list 初始化为 `new List<...>()`，禁止保留 null。
- [ ] 新增 `CubePrimitive`，字段必须是：
  `pose`、`size`、`color`。
- [ ] 所有 wire 字段都用 `[JsonProperty("...")]` 明确指定官方字段名。
- [ ] 不使用 `NullValueHandling.Ignore` 省略官方 required 字段。

### C. Schema Channel API

- [ ] 在 `FoxgloveSession` 增加：
  `RegisterSchemaChannel(uint channelId, string topic, string schemaName)`。
- [ ] 该方法从 `ISchemaRegistry` 查找 schema。
- [ ] 找不到 schema 时抛 `InvalidOperationException`，错误信息包含 `schemaName`。
- [ ] 该方法内部构造 `AdvertiseChannel`：
  `Id = channelId`。
- [ ] 该方法内部构造 `AdvertiseChannel`：
  `Topic = topic`。
- [ ] 该方法内部构造 `AdvertiseChannel`：
  `Encoding = "json"`。
- [ ] 该方法内部构造 `AdvertiseChannel`：
  `SchemaName = entry.Name`。
- [ ] 该方法内部构造 `AdvertiseChannel`：
  `SchemaEncoding = entry.Encoding`。
- [ ] 该方法内部构造 `AdvertiseChannel`：
  `Schema = entry.Content`。
- [ ] 该方法最终调用现有 `RegisterChannel(AdvertiseChannel channel)`，复用 Phase 2 advertise 行为。
- [ ] 在 `FoxgloveRuntime` 增加同名代理方法。
- [ ] `FoxgloveRuntime` 未 Start 时调用代理方法，抛 `InvalidOperationException`，沿用 Phase 2 文案风格。

### D. PublishJson API

- [ ] 在 `FoxgloveSession` 增加：
  `PublishJson(uint channelId, object message)`。
- [ ] 在 `FoxgloveSession` 增加：
  `PublishJson(uint channelId, object message, ulong logTimeNs)`。
- [ ] `PublishJson` 使用 `JsonConvert.SerializeObject(message)` 序列化 payload。
- [ ] payload 使用 UTF-8 编码。
- [ ] `PublishJson(channelId, message)` 使用 `_clock.NowNs`。
- [ ] `PublishJson(channelId, message, logTimeNs)` 传入显式时间戳。
- [ ] 如果 `message == null`，抛 `ArgumentNullException`。
- [ ] 在 `FoxgloveRuntime` 增加同名代理方法。
- [ ] 不改变现有 `Publish(uint channelId, byte[] payload)` 行为。

### E. Core Schema 注册时机

- [ ] `FoxgloveRuntime` 默认构造路径创建 `DefaultSchemaRegistry` 后，需要注册 core schemas。
- [ ] 自定义 `ISchemaRegistry` 构造路径也需要注册 core schemas。
- [ ] 当前 `FoxgloveRuntime()` 默认构造会委托到三参构造；实现时只在三参构造函数里调用一次 `FoxgloveSchemaDefinitions.RegisterCoreSchemas(_schemaRegistry)`，不要在两个构造路径各写一遍。
- [ ] 注册应该是幂等的；重复注册同名 schema 覆盖即可。
- [ ] 如果调用方手动通过 `runtime.Schemas.Register(...)` 覆盖同名 schema，后续 `RegisterSchemaChannel` 使用最新 registry 内容。
- [ ] 不在 `FoxgloveSession` 构造函数里隐式注册 schema，避免重复耦合 session 生命周期。

### F. `--demo3d` 手动验证入口

- [ ] 扩展 `Tests/Runtime/Program.cs` 参数解析，新增 `--demo3d`。
- [ ] `--demo` 保持 Phase 2 heartbeat 行为不变。
- [ ] `--demo3d` 启动后注册 `/tf`：
  `schemaName = foxglove.FrameTransform`。
- [ ] `--demo3d` 启动后注册 `/scene`：
  `schemaName = foxglove.SceneUpdate`。
- [ ] `/tf` 以 10 Hz 或 1 Hz 发布 `FrameTransformMessage`，优先选择 1 Hz 保持控制台验证简单。
- [ ] `/scene` 发布一个 cube entity，`frame_id = "unity_world"`。
- [ ] cube entity 的 `id = "phase3_cube"`。
- [ ] cube size 默认使用 `{ x: 1, y: 1, z: 1 }`。
- [ ] cube color 默认使用绿色或蓝色，alpha 为 1。
- [ ] 时间字段从当前 UTC 纳秒拆成：
  `sec = unixNs / 1_000_000_000`，`nsec = unixNs % 1_000_000_000`。
- [ ] 控制台打印 Foxglove 操作说明：
  连接 URL、Topics、3D 面板选择 `/scene`。

### G. 自动化测试

- [ ] 新增 `Phase3Validation.cs`。
- [ ] `Program.cs` 默认测试路径接入 `Phase3Validation.Validate()`。
- [ ] `FoxgloveSdk.Tests.csproj` 加入 `Phase3Validation.cs`。
- [ ] 测试 `RegisterCoreSchemas` 后 registry 能找到 `foxglove.FrameTransform`。
- [ ] 测试 `RegisterCoreSchemas` 后 registry 能找到 `foxglove.SceneUpdate`。
- [ ] 测试两条 schema 的 `Encoding == "jsonschema"`。
- [ ] 测试两条 schema content 能用 `JObject.Parse` 解析。
- [ ] 测试两条 schema 的 `title` 分别等于官方 schema name。
- [ ] 测试 `RegisterSchemaChannel(10, "/tf", "foxglove.FrameTransform")` 会广播 advertise。
- [ ] 断言 advertise 中：
  `encoding == "json"`。
- [ ] 断言 advertise 中：
  `schemaName == "foxglove.FrameTransform"`。
- [ ] 断言 advertise 中：
  `schemaEncoding == "jsonschema"`。
- [ ] 断言 advertise 中：
  `schema` 非空并包含 `"title": "foxglove.FrameTransform"`。
- [ ] 测试 unknown schema 调用 `RegisterSchemaChannel` 抛 `InvalidOperationException`。
- [ ] 测试 `FrameTransformMessage` 序列化字段名是 `parent_frame_id` 和 `child_frame_id`。
- [ ] 测试 `SceneUpdateMessage` 序列化时 `deletions` 和 `entities` 都存在。
- [ ] 测试 cube entity 序列化时 required arrays 都存在，未实现 primitive arrays 为空数组。
- [ ] 测试 `PublishJson` 在 fake transport 中发送 binary frame。
- [ ] 解码 `PublishJson` 的 binary frame，确认 payload 是可 parse JSON。
- [ ] 真实 WebSocket 集成测试：
  connect -> serverInfo -> typed advertise -> subscribe -> `PublishJson(SceneUpdateMessage)` -> binary payload。
- [ ] 真实 WebSocket 集成测试断言 payload 中存在：
  `entities[0].cubes[0].size.x == 1`。
- [ ] 所有测试失败时必须抛异常，进程返回非 0。

### H. 文档更新

- [ ] 更新 `Documentation~/README.md` 状态为 Phase 3。
- [ ] 在 README 增加 typed JSON schema channel 示例。
- [ ] 在 README 增加 `--demo3d` 命令：

```powershell
dotnet run --project "Packages\dev.unityfoxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj" -- --serve --port 8765 --demo3d
```

- [ ] 在 README 增加 Foxglove Desktop 手动步骤：
  Open connection -> 3D panel -> 选择 `/scene`。
- [ ] 更新 `Documentation~/Architecture.md`，增加 Phase 3 数据流。
- [ ] 在 Architecture 记录 schema 来源 commit 与 hash。
- [ ] 在 Architecture 明确 Phase 3 坐标策略：
  Unity 原样，root frame 为 `unity_world`。
- [ ] 在 Architecture 明确 SceneUpdate 支持范围：
  cube only，其他 primitive arrays 为空。

## 建议执行顺序

1. 先做 `A. 官方 Schema 常量与注册`
2. 再做 `G. 自动化测试` 中 schema registry 失败用例
3. 实现 `B. Visual DTO 收口`
4. 补 `G. 自动化测试` 中 DTO snapshot 用例
5. 实现 `C. Schema Channel API`
6. 实现 `D. PublishJson API`
7. 补 `G. 自动化测试` 中 fake transport 与真实 WebSocket 用例
8. 实现 `F. --demo3d 手动验证入口`
9. 最后更新 `H. 文档更新`

## 验收矩阵

### 自动化验收

- [ ] `dotnet run --project "Packages\dev.unityfoxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj"` 返回 0。
- [ ] 输出包含 Phase 0、Phase 1、Phase 2、Phase 3 四组验证。
- [ ] Registry 能找到 `foxglove.FrameTransform` 与 `foxglove.SceneUpdate`。
- [ ] advertise 使用 `schemaEncoding = "jsonschema"`。
- [ ] advertise 中 schema 内容非空，且 title 与 schemaName 一致。
- [ ] `FrameTransformMessage` 使用官方字段名。
- [ ] `SceneUpdateMessage` 的 cube payload 能通过 WebSocket binary frame 到达 client。
- [ ] unknown schema 注册失败路径有测试覆盖。

### 手动验收

- [ ] 启动 demo server：

```powershell
dotnet run --project "Packages\dev.unityfoxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj" -- --serve --port 8765 --demo3d
```

- [ ] Foxglove 可连接 `ws://127.0.0.1:8765`。
- [ ] Topics 面板能看到 `/tf` 和 `/scene`。
- [ ] Raw Messages 面板能看到 `/scene` 的 JSON payload。
- [ ] 3D 面板能看到 cube。
- [ ] 断开、重连后仍能收到 `serverInfo` + typed `advertise`。
- [ ] 关闭 Foxglove 不导致 server 崩溃。

## 风险检查

- [ ] 不要把 `encoding` 改成 `jsonschema`；数据 encoding 仍然是 `json`。
- [ ] 不要把 `schemaEncoding` 留空；typed JSON channel 必须是 `jsonschema`。
- [ ] 不要省略 `SceneEntity` 的 required arrays。
- [ ] 不要引入 `UnityEngine` 到 dotnet 测试会编译的文件中。
- [ ] 不要在 Phase 3 做坐标转换；保持 Unity 原样，后续再设计转换策略。
- [ ] 不要声明 `time` capability，除非实现并广播 time frame。
- [ ] 不要把 `PoseInFrame` 或完整 SceneUpdate primitive scope 偷偷塞进 Phase 3。

## 下一阶段入口

> [!success]
> Phase 3 完成后，进入 [[00_PLAN]] 里的 Phase 4：Unity 集成与易用 API。重点是 `FoxgloveManager`、`FoxglovePublisher<T>`、`TransformPublisher`、主线程边界、Inspector 配置和 Demo Scene。
