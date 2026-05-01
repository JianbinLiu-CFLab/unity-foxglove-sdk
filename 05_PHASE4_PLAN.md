---
title: Foxglove Unity SDK Phase 4 执行计划
aliases:
  - Phase 4 Plan
  - FoxgloveSDK Phase 4
tags:
  - plan
  - phase4
  - todo
  - unity
  - foxglove
status: draft
updated: 2026-05-01
---

# Foxglove Unity SDK Phase 4 执行计划

> [!summary]
> 本文是从 [[00_PLAN]] 拆出的 Phase 4 执行版，承接 [[04_PHASE3_PLAN]] 已完成的协议、schema、`FrameTransform` 和 `SceneUpdate` 最小 3D 可视化闭环。目标是把 SDK 从“可用 C# Runtime”推进到“Unity 开发者能直接挂组件使用”的形态：不用 Python bridge，不写底层协议代码，按下 Play 后 Foxglove 能看到 Transform、Scene cube 和低帧率 Camera 图像。

## 对应关系

- 上位计划：[[00_PLAN]]
- 上一阶段：[[04_PHASE3_PLAN]]
- 对应阶段：`Phase 4 - Unity 集成与易用 API`
- 包路径：`Packages/dev.unityfoxglove.sdk`
- package id：`dev.unityfoxglove.sdk`
- 根命名空间：`Unity.FoxgloveSDK`

## 本阶段目标

- 提供 `FoxgloveManager : MonoBehaviour`，管理 `FoxgloveRuntime` 的 Unity 生命周期。
- 提供 Unity 组件式发布器：
  - `FoxgloveTransformPublisher`
  - `FoxgloveSceneCubePublisher`
  - `FoxgloveCameraPublisher`
- 让用户通过 Inspector 配置 topic、frame、发布频率和相机参数。
- 支持官方 `foxglove.CompressedImage` JSON schema，并用 JSON base64 发布 JPEG 图像。
- 提供 `Samples~/BasicVisualization` 使用说明和 Foxglove layout。
- 保持 Phase 0-3 的 dotnet 验证链路，并新增 Phase 4 纯 C# 可测部分。

## 当前约定

> [!important]
> Phase 4 做 Unity 易用层和 Camera MVP，但不做任意用户自定义发布系统。Transform 和 Camera 是本阶段第一优先级；自定义 scalar、struct、reflection publisher 放到后续 Phase。

- 坐标策略继续沿用 Phase 3：
  Unity 原样输出，root frame 使用 `unity_world`，不做 handedness / ENU / ROS 转换。
- Camera MVP 使用：
  `foxglove.CompressedImage`
- Camera wire shape 使用：
  `encoding = "json"`，`schemaEncoding = "jsonschema"`，payload 内 `data` 为 base64 字符串。
- Camera 默认参数：
  `640x480`、JPEG quality `70`、max FPS `10`、max pending readbacks `2`。
- Demo Scene 不手写 `.unity` YAML。
  先交付组件、README、layout；如果 Unity Editor 可安全生成，再补真实 sample scene。
- 旧 demo 只作为结构参考：
  不再使用 `UdpClient`、`TcpClient`、Python bridge 或外部进程。

## 本阶段不做

- 不做泛型 `FoxglovePublisher<T>` 的完整公开设计。
- 不做任意字段反射发布。
- 不做 scalar / int / bool / string / Vector2 / Vector3 / Quaternion / Color 通用发布器。
- 不做 MCAP。
- 不做 layout 自动安装。
- 不做 Native Backend 或 IL2CPP 深度验证；这些继续留到 Phase 5。
- 不做高性能图像二进制协议；Phase 4 先用 JSON base64 换取确定性和可验收性。

## 目标文件结构

- 修改：
  `Packages/dev.unityfoxglove.sdk/Runtime/Unity/FoxgloveManager.cs`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Runtime/Unity/FoxglovePublisherBase.cs`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Runtime/Unity/FoxgloveTransformPublisher.cs`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Runtime/Unity/FoxgloveSceneCubePublisher.cs`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Runtime/Unity/FoxgloveCameraPublisher.cs`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Runtime/Unity/FoxgloveUnityUtil.cs`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Runtime/Schemas/FoxgloveSchemaDefinitions.cs`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Runtime/Schemas/FoxgloveVisualMessages.cs`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Runtime/Schemas/FoxgloveTimeUtil.cs`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Runtime/Schemas/FoxgloveImageMessages.cs`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Runtime/Schemas/CompressedImage.json`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Tests/Runtime/Phase4Validation.cs`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Tests/Runtime/Program.cs`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Documentation~/README.md`
- 修改：
  `Packages/dev.unityfoxglove.sdk/Documentation~/Architecture.md`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Samples~/BasicVisualization/README.md`
- 新增：
  `Packages/dev.unityfoxglove.sdk/Samples~/BasicVisualization/FoxgloveLayout.json`

## A. 官方 `CompressedImage` Schema

- [ ] 从本地官方 clone 复制：
  `foxglove-sdk/schemas/jsonschema/CompressedImage.json`
- [ ] 复制到：
  `Packages/dev.unityfoxglove.sdk/Runtime/Schemas/CompressedImage.json`
- [ ] 保持 JSON 原文，不手改字段结构。
- [ ] 记录 schema 摘要：
  `CompressedImage.json sha256=f240ea31d2f0454b`
- [ ] 在 `FoxgloveSchemaDefinitions.cs` 中新增：
  `CompressedImageSchemaName = "foxglove.CompressedImage"`
- [ ] 在 `FoxgloveSchemaDefinitions.cs` 中新增 base64 C# const。
- [ ] base64 const 解码方式沿用 Phase 3：
  `Encoding.UTF8.GetString(Convert.FromBase64String(...))`
- [ ] `RegisterCoreSchemas(ISchemaRegistry registry)` 同时注册：
  `foxglove.FrameTransform`
- [ ] `RegisterCoreSchemas(ISchemaRegistry registry)` 同时注册：
  `foxglove.SceneUpdate`
- [ ] `RegisterCoreSchemas(ISchemaRegistry registry)` 同时注册：
  `foxglove.CompressedImage`
- [ ] 不恢复 `EmbeddedResource` / `GetManifestResourceStream` 方案。

## B. 纯 C# 时间与图像 DTO

- [ ] 新增 `FoxgloveTimeUtil.cs`。
- [ ] `FoxgloveTimeUtil.NowUnixTimeNs()` 返回当前 UTC Unix epoch 纳秒（当前实现为毫秒精度，纳秒精度延后到 Phase 5）。
- [ ] `FoxgloveTimeUtil.ToFoxgloveTime(ulong unixNs)` 返回 `FoxgloveTime`。
- [ ] `ToFoxgloveTime` 拆分规则：
  `sec = unixNs / 1_000_000_000`
- [ ] `ToFoxgloveTime` 拆分规则：
  `nsec = unixNs % 1_000_000_000`
- [ ] 新增 `FoxgloveImageMessages.cs`。
- [ ] 新增 DTO：
  `CompressedImageMessage`
- [ ] DTO 字段：
  `timestamp`
- [ ] DTO 字段：
  `frame_id`
- [ ] DTO 字段：
  `data`
- [ ] DTO 字段：
  `format`
- [ ] `data` 类型使用 `string`，内容为 base64。
- [ ] `format` 默认由发布器设置为 `"jpeg"`。
- [ ] DTO 命名空间使用：
  `Unity.FoxgloveSDK.Schemas`

## C. `FoxgloveManager : MonoBehaviour`

- [ ] 将 `Runtime/Unity/FoxgloveManager.cs` 从 Phase 0 占位改为真实 `MonoBehaviour`。
- [ ] 命名空间保持：
  `Unity.FoxgloveSDK.Components`
- [ ] 序列化字段：
  `_serverName = "Unity Foxglove SDK"`
- [ ] 序列化字段：
  `_host = "127.0.0.1"`
- [ ] 序列化字段：
  `_port = 8765`
- [ ] 序列化字段：
  `_startOnEnable = true`
- [ ] 序列化字段：
  `_runInBackground = true`
- [ ] `Awake()` 创建 `FoxgloveRuntime`。
- [ ] 如果 `_runInBackground` 为 true：
  设置 `Application.runInBackground = true`。
- [ ] `OnEnable()` 在 `_startOnEnable` 为 true 时启动 runtime。
- [ ] `OnDisable()` 停止 runtime。
- [ ] `OnDestroy()` dispose runtime。
- [ ] 重复 `Start` 不应抛异常到 Unity 控制台；应安全 no-op 或记录 warning。
- [ ] 公开属性：
  `Runtime`
- [ ] 公开属性：
  `IsRunning`
- [ ] 公开方法：
  `StartServer()`
- [ ] 公开方法：
  `StopServer()`
- [ ] 公开方法：
  `GetOrRegisterSchemaChannel(string topic, string schemaName)`
- [ ] 公开方法：
  `PublishJson(string topic, string schemaName, object message, ulong logTimeNs)`
- [ ] channel id 由 manager 内部递增分配，起始值建议 `1`。
- [ ] channel cache key 使用 `(topic, schemaName)`。
- [ ] 同一 `(topic, schemaName)` 多次请求必须复用同一个 channel id。
- [ ] `PublishJson` 若 runtime 未启动，应安全 no-op 并记录 warning，不抛异常污染 Play Mode。

## D. Publisher 基类

- [ ] 新增 `FoxglovePublisherBase : MonoBehaviour`。
- [ ] 命名空间：
  `Unity.FoxgloveSDK.Components`
- [ ] 序列化字段：
  `_manager`
- [ ] 序列化字段：
  `_topic`
- [ ] 序列化字段：
  `_publishRateHz = 10f`
- [ ] 序列化字段：
  `_publishOnEnable = true`
- [ ] 序列化字段：
  `_warnIfManagerMissing = true`
- [ ] `OnEnable()` 自动解析 manager。
- [ ] manager 解析顺序：
  先使用 Inspector 指定的 `_manager`。
- [ ] manager 解析顺序：
  再使用 `FindObjectOfType<FoxgloveManager>()` 或 Unity 版本可用的等价 API。
- [ ] 找不到 manager 时：
  safe no-op。
- [ ] 找不到 manager 且 `_warnIfManagerMissing` 为 true：
  `Debug.LogWarning` 一次即可，不要每帧刷屏。
- [ ] 提供 `ShouldPublishNow()`，按 `Time.unscaledTime` 做频率限制。
- [ ] `_publishRateHz <= 0` 时表示每帧发布。
- [ ] 提供 `SanitizeFrameId(string raw, string fallback)`：
  空字符串使用 fallback。
- [ ] `SanitizeFrameId` 将空格替换为 `_`。
- [ ] 基类不直接读取具体 Unity 数据。

## E. Transform Publisher

- [ ] 新增 `FoxgloveTransformPublisher`。
- [ ] 继承：
  `FoxglovePublisherBase`
- [ ] 默认 topic：
  `/tf`
- [ ] 序列化字段：
  `_parentFrameId = "unity_world"`
- [ ] 序列化字段：
  `_childFrameId = ""`
- [ ] `_childFrameId` 为空时使用 sanitized `gameObject.name`。
- [ ] 在 `Update()` 中读取 `transform.position`。
- [ ] 在 `Update()` 中读取 `transform.rotation`。
- [ ] 构造 `FrameTransformMessage`。
- [ ] `Translation` 使用 Unity `Vector3` 原值：
  `x = position.x`
- [ ] `Translation` 使用 Unity `Vector3` 原值：
  `y = position.y`
- [ ] `Translation` 使用 Unity `Vector3` 原值：
  `z = position.z`
- [ ] `Rotation` 使用 Unity `Quaternion` 原值：
  `x/y/z/w` 原样输出。
- [ ] `timestamp` 和 `logTime` 使用同一个 `unixNs`。
- [ ] 发布调用：
  `manager.PublishJson("/tf", "foxglove.FrameTransform", message, unixNs)`
- [ ] 提供只读属性：
  `ResolvedChildFrameId`
- [ ] 不做坐标系转换。

## F. Scene Cube Publisher

- [ ] 新增 `FoxgloveSceneCubePublisher`。
- [ ] 继承：
  `FoxglovePublisherBase`
- [ ] 默认 topic：
  `/scene`
- [ ] 序列化字段：
  `_entityId = ""`
- [ ] 序列化字段：
  `_frameId = ""`
- [ ] 序列化字段：
  `_size = Vector3.one`
- [ ] 序列化字段：
  `_color = Color.green`
- [ ] `_entityId` 为空时使用 sanitized `gameObject.name`。
- [ ] `_frameId` 为空时，优先读取同 GameObject 上的 `FoxgloveTransformPublisher.ResolvedChildFrameId`。
- [ ] 如果没有 transform publisher，则 `_frameId` fallback 为 `unity_world`。
- [ ] 在 `Update()` 中构造 `SceneUpdateMessage`。
- [ ] `SceneEntity.Id` 使用 resolved entity id。
- [ ] `SceneEntity.FrameId` 使用 resolved frame id。
- [ ] `SceneEntity.Timestamp` 使用当前 `unixNs`。
- [ ] `SceneEntity.Lifetime` 使用 zero duration，表示保留到被替换。
- [ ] `SceneEntity.Cubes` 包含一个 `CubePrimitive`。
- [ ] cube pose 使用 identity：
  position `(0,0,0)`，rotation `(0,0,0,1)`。
- [ ] cube size 使用 `_size`。
- [ ] cube color 将 Unity `Color` 映射到 `FoxgloveColor`：
  `r/g/b/a` 原样 double。
- [ ] 发布调用：
  `manager.PublishJson("/scene", "foxglove.SceneUpdate", message, unixNs)`

## G. Camera Publisher

- [ ] 新增 `FoxgloveCameraPublisher`。
- [ ] 继承：
  `FoxglovePublisherBase`
- [ ] 添加 `[RequireComponent(typeof(Camera))]`。
- [ ] 默认 topic：
  `/unity/camera`
- [ ] 序列化字段：
  `_frameId = "unity_camera"`
- [ ] 序列化字段：
  `_width = 640`
- [ ] 序列化字段：
  `_height = 480`
- [ ] 序列化字段：
  `_jpegQuality = 70`
- [ ] 序列化字段：
  `_maxPendingReadbacks = 2`
- [ ] `_jpegQuality` Inspector 范围建议 `[Range(10, 100)]`。
- [ ] 发布频率默认 `10 Hz`。
- [ ] `Awake()` 或 `OnEnable()` 获取 source camera。
- [ ] 创建 capture `RenderTexture`。
- [ ] 创建 CPU staging `Texture2D`。
- [ ] 创建 disabled shadow camera，并 `CopyFrom(sourceCamera)`。
- [ ] shadow camera 的 `targetTexture` 指向 capture RT。
- [ ] `LateUpdate()` 做 FPS 限流。
- [ ] `LateUpdate()` 如果 pending readbacks 达到上限，丢帧，不排队。
- [ ] `LateUpdate()` 调用 shadow camera `Render()`。
- [ ] 使用 `AsyncGPUReadback.Request` 读取 RGB24。
- [ ] readback callback 中检查 destroyed flag。
- [ ] readback callback 中检查 `req.hasError`。
- [ ] readback callback 中执行 `LoadRawTextureData`。
- [ ] readback callback 中执行 `EncodeToJPG(_jpegQuality)`。
- [ ] 将 JPEG bytes 转 base64：
  `Convert.ToBase64String(jpegBytes)`
- [ ] 构造 `CompressedImageMessage`：
  `format = "jpeg"`
- [ ] `timestamp` 和 `logTime` 使用同一个 `unixNs`。
- [ ] 发布调用：
  `manager.PublishJson("/unity/camera", "foxglove.CompressedImage", message, unixNs)`
- [ ] `OnDisable()` 或 `OnDestroy()` 释放 RT、Texture2D、shadow camera。
- [ ] 禁止在销毁后 readback callback 访问已释放资源。
- [ ] Camera publisher 不使用 `TcpClient`。

## H. 自动化测试

- [ ] 新增：
  `Packages/dev.unityfoxglove.sdk/Tests/Runtime/Phase4Validation.cs`
- [ ] 修改：
  `Program.cs` 默认测试路径接入 `Phase4Validation.Validate()`。
- [ ] 修改：
  `FoxgloveSdk.Tests.csproj` 加入 `Phase4Validation.cs`。
- [ ] 测试 `DefaultSchemaRegistry` 经 `RegisterCoreSchemas` 后能找到：
  `foxglove.CompressedImage`
- [ ] 测试 `CompressedImage` schema encoding 为：
  `jsonschema`
- [ ] 测试 `CompressedImage` schema content 可被 `JObject.Parse` 解析。
- [ ] 测试 schema title 等于：
  `foxglove.CompressedImage`
- [ ] 测试 `CompressedImageMessage` JSON 字段包含：
  `timestamp`
- [ ] 测试 `CompressedImageMessage` JSON 字段包含：
  `frame_id`
- [ ] 测试 `CompressedImageMessage` JSON 字段包含：
  `data`
- [ ] 测试 `CompressedImageMessage` JSON 字段包含：
  `format`
- [ ] 测试 `data` 是 base64 字符串，并能 roundtrip 回原始 bytes。
- [ ] 测试 `format == "jpeg"`。
- [ ] 测试 `FoxgloveTimeUtil.ToFoxgloveTime(1777645831933000000)`：
  `sec == 1777645831`
- [ ] 测试 `FoxgloveTimeUtil.ToFoxgloveTime(1777645831933000000)`：
  `nsec == 933000000`
- [ ] 测试 `RegisterSchemaChannel(30, "/unity/camera", "foxglove.CompressedImage")` 广播 advertise。
- [ ] 断言 advertise 中：
  `schemaName == "foxglove.CompressedImage"`
- [ ] 断言 advertise 中：
  `schemaEncoding == "jsonschema"`
- [ ] 断言 advertise 中：
  `schema` 包含 `"title": "foxglove.CompressedImage"`。
- [ ] 保持 Phase 0、Phase 1、Phase 2、Phase 3 全部验证通过。
- [ ] 默认命令：

```powershell
dotnet run --project "Packages\dev.unityfoxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj"
```

## I. Unity 手动验收

- [ ] 在 Unity 2022.3+ 中导入本地 package。
- [ ] 新建空场景。
- [ ] 新建 GameObject：
  `Foxglove`
- [ ] 给 `Foxglove` 挂：
  `FoxgloveManager`
- [ ] 新建 cube。
- [ ] 给 cube 挂：
  `FoxgloveTransformPublisher`
- [ ] 给 cube 挂：
  `FoxgloveSceneCubePublisher`
- [ ] 新建或使用 Main Camera。
- [ ] 给 Camera 挂：
  `FoxgloveCameraPublisher`
- [ ] Play Mode 启动后，Foxglove 连接：

```text
ws://127.0.0.1:8765
```

- [ ] Topics 显示：
  `/tf`
- [ ] Topics 显示：
  `/scene`
- [ ] Topics 显示：
  `/unity/camera`
- [ ] 3D panel 显示 cube。
- [ ] 移动 Unity cube 后，Foxglove 中 cube 随之更新。
- [ ] Image panel 能显示 `/unity/camera` 的低 FPS 图像。
- [ ] 停止 Play Mode 不出现端口残留。
- [ ] 再次 Play Mode 后可以重新连接。
- [ ] 多次断开/重连 Foxglove 不导致 Unity 控制台刷异常。

## J. Samples 与文档

- [ ] 更新 `Documentation~/README.md`，状态改为 Phase 4。
- [ ] README 增加 Unity component setup 步骤。
- [ ] README 增加旧 bridge 迁移说明：
  不再需要 Python。
- [ ] README 增加旧 bridge 迁移说明：
  不再需要 `UdpClient.Send(jsonBytes)`。
- [ ] README 增加旧 bridge 迁移说明：
  不再需要 `TcpClient.Connect(host, 9001)`。
- [ ] 更新 `Documentation~/Architecture.md`，增加 Unity Components layer。
- [ ] Architecture 明确主线程边界：
  Unity API 只能在 Unity lifecycle callback 或 Unity callback 中访问。
- [ ] Architecture 明确 transport callback 不访问 Unity 对象。
- [ ] 新增：
  `Samples~/BasicVisualization/README.md`
- [ ] Sample README 写明：
  Manager、Transform、SceneCube、Camera 三类组件如何挂载。
- [ ] Sample README 写明：
  Foxglove 连接地址和面板配置。
- [ ] 新增或改造：
  `Samples~/BasicVisualization/FoxgloveLayout.json`
- [ ] layout 至少包含：
  3D panel 对 `/scene`
- [ ] layout 至少包含：
  Image panel 对 `/unity/camera`
- [ ] layout 可参考旧 demo：
  `D:\BaiduSyncdisk\Obsidian Vault\Learning Session\00 Inbox\Foxglove\Unity.json`
- [ ] 不手写 `.unity` scene YAML；只有在 Unity Editor 可以安全生成时再补。

## 建议执行顺序

1. 先做 `A. 官方 CompressedImage Schema`。
2. 再做 `B. 纯 C# 时间与图像 DTO`。
3. 补 `H. 自动化测试` 中 schema、DTO、time util 用例，并确认失败。
4. 实现纯 C# 部分到测试通过。
5. 做 `C. FoxgloveManager`。
6. 做 `D. Publisher 基类`。
7. 做 `E. Transform Publisher`。
8. 做 `F. Scene Cube Publisher`。
9. 做 `G. Camera Publisher`。
10. 更新 `J. Samples 与文档`。
11. 跑完整 dotnet 验证。
12. 进入 Unity 手动验收。

## 验收矩阵

### 自动化验收

- [ ] 输出包含 Phase 0、Phase 1、Phase 2、Phase 3、Phase 4 五组验证。
- [ ] 所有测试失败时必须抛异常，进程返回非 0。
- [ ] `CompressedImage` schema 注册成功。
- [ ] `CompressedImageMessage` base64 roundtrip 成功。
- [ ] `/unity/camera` typed advertise 快照正确。
- [ ] Phase 0-3 无回归。

### 手动验收

- [ ] Unity Play Mode 后 Foxglove 能连接。
- [ ] `/tf`、`/scene`、`/unity/camera` 都能看到。
- [ ] 3D panel 能看到 cube。
- [ ] 移动 cube 后 3D panel 更新。
- [ ] Image panel 能看到 camera feed。
- [ ] 停止 Play Mode 后无端口残留。
- [ ] 重复 Play/Stop 后仍可连接。

## 风险与注意事项

- `Runtime/Unity/*.cs` 可以引用 `UnityEngine`；但 `Runtime/Schemas/*.cs`、`Runtime/Core/*.cs`、`Runtime/Protocol/*.cs`、`Runtime/Transport/*.cs` 仍应保持 dotnet harness 可编译。
- `Tests/Runtime/FoxgloveSdk.Tests.csproj` 不应 include `Runtime/Unity/*.cs`，否则 dotnet 测试会缺少 UnityEngine。
- Camera JSON base64 会放大 payload，Phase 4 默认低分辨率和低 FPS，避免误以为这是最终高性能方案。
- `AsyncGPUReadback` callback 可能晚于 `OnDestroy`，必须有 destroyed guard。
- `Application.runInBackground` 是全局设置，Manager 只在字段开启时设置，不在 Stop 时强行恢复。
- `FoxgloveManager` 停止时应复用 Phase 2 修好的 Stop/Disconnect 清理路径，避免 Play Mode 重启端口残留。

## 后续阶段预留

- 自定义 scalar / struct / reflection publisher。
- 真正的 `FoxglovePublisher<T>` 泛型 API。
- Camera 高性能二进制路径或 native backend。
- IL2CPP、Standalone Player、跨平台加固。
- Unity Editor 菜单、自动生成 sample scene、layout 导入辅助。
