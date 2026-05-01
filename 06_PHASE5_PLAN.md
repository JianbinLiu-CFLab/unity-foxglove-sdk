---
title: Foxglove Unity SDK Phase 5 执行计划
aliases:
  - Phase 5 Plan
  - FoxgloveSDK Phase 5
tags:
  - plan
  - phase5
  - todo
  - unity
  - foxglove
  - il2cpp
status: draft
updated: 2026-05-01
---

# Foxglove Unity SDK Phase 5 执行计划

> [!summary]
> 本文是从 [[00_PLAN]] 拆出的 Phase 5 执行版，承接 [[05_PHASE4_PLAN]] 已验收的 Unity 组件、Transform、Scene cube 和 Camera 可视化闭环。目标是把 SDK 从“Editor/MVP 能演示”推进到“Windows Standalone IL2CPP 可构建、异常路径更稳、后续可发布和可扩展”的工程状态。本阶段不做新协议能力，不提前实现 MCAP、Parameters、Services。

## 对应关系

- 上位计划：[[00_PLAN]]
- 上一阶段：[[05_PHASE4_PLAN]]
- 对应阶段：`Phase 5 - 加固、IL2CPP、Native Backend 预留`
- 当前包路径：`Packages/dev.unityfoxglove.sdk`
- 目标包路径：`Packages/dev.unity2foxglove.sdk`
- Unity 验证项目：`Untiy2Foxglove`
- Unity Editor：`6000.3.14f1`
- 目标系统：Windows 10 LTSC
- 目标平台：Windows Standalone x64 + IL2CPP
- package id：`dev.unity2foxglove.sdk`
- 旧 package id：`dev.unityfoxglove.sdk`
- 根命名空间：`Unity.FoxgloveSDK`

## 本阶段目标

- 完成 Windows Standalone IL2CPP 构建验证，确认 Newtonsoft.Json、schema DTO、transport 和 Unity 组件不会被裁剪或 AOT 问题破坏。
- 增加 `link.xml`，把 Newtonsoft.Json 和 SDK runtime 类型保留下来，作为 Phase 5 的明确交付物。
- 修正 Phase 4 遗留的时间精度问题，让 `FoxgloveTimeUtil.NowUnixTimeNs()` 返回真正的 Unix epoch 纳秒级时间戳。
- 收口 `FoxgloveRuntime`、`FoxgloveSession`、`IFoxgloveTransport` 的生命周期所有权，降低 Stop/Start、Play/Stop、异常断线后的状态残留风险。
- 加固 `ManagedWsBackend` 的异常路径，覆盖断线、send 失败、非法 frame、malformed JSON、unknown op、无效 subscribe/unsubscribe。
- 将 UPM package identity 从 `dev.unityfoxglove.sdk` 迁移到 `dev.unity2foxglove.sdk`，包括 package id、包目录、Unity manifest 与文档引用。
- 对官方 `foxglove-c.h` Native Backend 做可行性评估，形成结论文档；本阶段不实现可运行 native backend。
- 保持 Phase 0-4 的 dotnet 验证链路，并新增 Phase 5 纯 C# 可测部分。

## 当前约定

> [!important]
> Phase 5 是工程加固阶段，不是新协议设计阶段。当前可运行主线继续使用纯 C# `ManagedWsBackend`。只有 IL2CPP、稳定性或性能验证出现明确阻塞，才把 Native Backend 提升为后续实施项。

- Unity 验证范围锁定当前项目：
  `Untiy2Foxglove`。
- Unity 版本锁定当前已安装版本：
  `6000.3.14f1`。
- 构建目标锁定：
  Windows Standalone x64 + IL2CPP。
- 当前 package metadata 仍声明：
  `unity = "2022.3"`，但 Phase 5 实际构建矩阵不覆盖 Unity 2022.3。
- 当前 `NativeFoxgloveBackend.cs` 只是 stub，不具备运行能力。
- 当前本地官方 C FFI 真值源为：
  `foxglove-sdk/c/include/foxglove-c/foxglove-c.h`。
- 当前 `SystemClock.NowNs` 已使用 `DateTime.UtcNow.Ticks * 100`，但 `FoxgloveTimeUtil.NowUnixTimeNs()` 仍是毫秒精度；Phase 5 需要统一。
- 当前 `Runtime/Unity/*.cs` 可以引用 `UnityEngine`；`Runtime/Core`、`Runtime/Protocol`、`Runtime/Transport`、`Runtime/Schemas` 必须继续保持 dotnet harness 可编译。
- `Untiy2Foxglove` 名称保持现状，不在 Phase 5 中重命名工程目录。
- package id 与包目录在 Phase 5 一起迁移为 `dev.unity2foxglove.sdk`；根命名空间 `Unity.FoxgloveSDK` 保持不变，避免 API/namespace churn。

## 本阶段不做

- 不实现 MCAP 写入。
- 不实现 Parameters / ParametersSubscribe。
- 不实现 Services。
- 不实现 Assets / fetchAsset。
- 不实现 PlaybackControl。
- 不新增通用 reflection publisher。
- 不新增 scalar / struct / generic publisher 公共 API。
- 不把 camera 从 JSON base64 改成高性能二进制图像通道。
- 不实现可运行 `NativeFoxgloveBackend`。
- 不做跨平台 Native Plugin 分发。
- 不做 Unity 2022.3 实机验证。
- 不改变 Phase 3/4 的坐标策略；继续 Unity 原样输出，不做 handedness / ENU / ROS 转换。

## 目标文件结构

- 新增：
  `Packages/dev.unity2foxglove.sdk/Runtime/link.xml`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase5Validation.cs`
- 新增：
  `Untiy2Foxglove/Assets/Editor/FoxglovePhase5Build.cs`
- 新增：
  `Packages/dev.unity2foxglove.sdk/Documentation~/NativeBackendEvaluation.md`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Schemas/FoxgloveTimeUtil.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Transport/IFoxgloveClock.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Transport/IFoxgloveTransport.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Transport/ManagedWsBackend.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs`
- 修改：
  `Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveSession.cs`
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
  `Packages/dev.unity2foxglove.sdk/package.json`
- 修改：
  `Untiy2Foxglove/Packages/manifest.json`
- 可能修改：
  `Untiy2Foxglove/Packages/packages-lock.json`
- 修改：
  `00_PLAN.md`

## A. 时间戳精度统一

- [ ] 修改 `FoxgloveTimeUtil.NowUnixTimeNs()`，从毫秒精度提升为 Unix epoch 纳秒精度。
- [ ] 实现策略使用 `Stopwatch.GetTimestamp()` 与 UTC epoch anchor：
  初始化时记录 `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` 或 ticks 作为 epoch anchor，同时记录 `Stopwatch.GetTimestamp()`。
- [ ] 后续调用使用 `Stopwatch` elapsed ticks 换算 ns，加到 anchor unix ns 上。
- [ ] 确保返回值单调不倒退；如果系统时间回拨，`Stopwatch` 路径仍保持本进程内单调。
- [ ] `FoxgloveTimeUtil.ToFoxgloveTime(ulong unixNs)` 行为不变。
- [ ] `SystemClock.NowNs` 复用 `FoxgloveTimeUtil.NowUnixTimeNs()`，避免 Core/Unity 使用两套时间源。
- [ ] 更新 XML 注释，说明该时间戳用于 Foxglove `logTime` 和 message timestamp。
- [ ] 不声明 `serverInfo.capabilities` 的 `time` capability；本阶段只修正消息时间戳，不广播 Time frame。

## B. Transport 生命周期所有权

> [!bug] 当前 Stop/Start bug chain
> 现有链路是 `FoxgloveRuntime.Stop()` → `_session.Dispose()` → `FoxgloveSession.Dispose()` → `(_transport as IDisposable)?.Dispose()`。随后 `FoxgloveRuntime.Start()` 会用同一个已经 disposed 的 `_transport` 创建新 `FoxgloveSession`，导致 Stop/Start 复用路径不稳定。

- [ ] 修改 `IFoxgloveTransport` 继承 `IDisposable`。
- [ ] 所有 fake transport 测试类型补 no-op `Dispose()`。
- [ ] 明确所有权：
  `FoxgloveRuntime` 拥有 transport，`FoxgloveSession` 只借用 transport。
- [ ] 修改 `FoxgloveSession.Dispose()`：
  调用 `Stop()`、解绑事件，但不 dispose transport。
- [ ] `FoxgloveSession.Dispose()` 必须显式解绑 `_transport.OnClientConnected`、`_transport.OnClientDisconnected`、`_transport.OnTextReceived`、`_transport.OnBinaryReceived` 四个事件，避免 transport 被下一个 session 复用时旧 handler 继续被调用。
- [ ] 修改 `FoxgloveRuntime.Dispose()`：
  调用 `Stop()` 后 dispose transport。
- [ ] 确认 `FoxgloveRuntime.Stop()` 后可以再次 `Start()`，复用同一个 transport 实例。
- [ ] 确认 `FoxgloveRuntime.Dispose()` 后不再支持再次 `Start()`；如果发生调用，应抛清晰异常或保持当前可理解行为。
- [ ] 在 `FoxgloveManager.StopServer()` 保持 Phase 4 已修复行为：
  清空 channel cache，重置 next channel id。

## C. Managed WebSocket 与 Unity 调用异常路径加固

- [ ] `ManagedWsBackend.Stop()` 必须取消 accept loop，断开所有 client，清空 client dictionary，并把 listener 状态置空。
- [ ] Stop 后重复 Stop 必须 no-op。
- [ ] Start 时如果正在运行，继续抛 `InvalidOperationException("Server already started")` 或等价清晰错误。
- [ ] `BroadcastText` 遍历发送前先对 `_clients.ToArray()` 做快照；发送失败时调用统一 disconnect 路径，不能吞掉失败并保留坏 client。
- [ ] `BroadcastBinary` 遍历发送前先对 `_clients.ToArray()` 做快照；发送失败时调用统一 disconnect 路径，不能吞掉失败并保留坏 client。
- [ ] `SendText` 和 `SendBinary` 遇到 `IOException`、`ObjectDisposedException`、socket 关闭类异常时移除 client。
- [ ] `HandleClient` 在 handshake 失败时必须关闭 tcp client，不触发 `OnClientConnected`。
- [ ] receive loop 收到 close frame 时回复 close 并走统一 disconnect。
- [ ] receive loop 遇到 malformed frame 或短读时走统一 disconnect，不抛出到 background task 顶层导致未观察异常。
- [ ] 对 client binary message 继续保持 no-op；不实现 clientPublish。
- [ ] 对 malformed JSON、unknown op、无效 subscribe、无效 unsubscribe 保持连接不断开，但不能污染 subscription registry。
- [ ] `FoxgloveManager.PublishJson()` 在 server 未启动时改成 warn-once 或静默 no-op；推荐 warn-once，并在 `StartServer()` 成功后重置 warning flag，避免三个 publisher 每秒刷几十条 Console warning。

## D. IL2CPP 裁剪保护

- [ ] 新增 `Packages/dev.unity2foxglove.sdk/Runtime/link.xml`，作为包内模板供消费者项目复制到 `Assets/`。
- [ ] link.xml 至少保留：
  `Newtonsoft.Json`。
- [ ] link.xml 至少保留：
  `Unity.FoxgloveSDK` runtime assembly。
- [ ] 保留范围优先选择保守策略：
  `preserve="all"`，先换取 IL2CPP 验证确定性。
- [ ] **重要发现 (2026-05-01):** Unity 不支持将 `link.xml` 放在 package 的 `Runtime/` 下自动生效；`link.xml` 必须放在消费者项目的 `Assets/` 目录或其子目录下，详见 [Unity Manual - Managed code stripping](https://docs.unity.cn/Manual/ManagedCodeStripping.html)。Package 内的 link.xml 可以作为模板，但构建脚本或消费者需将内容复制到 `Assets/link.xml`。
- [ ] 新增 `Untiy2Foxglove/Assets/link.xml`，内容与包内模板一致，作为 IL2CPP 构建的实际 preserve root。
- [ ] 在 `Documentation~/Architecture.md` 记录 Newtonsoft.Json + IL2CPP 裁剪策略，明确 link.xml 必须放在 Assets。
- [ ] 在 `Documentation~/README.md` 记录 Phase 5 构建验证已覆盖 link.xml（Assets 位置）。

## E. Phase 5 自动化测试

- [ ] 新增 `Phase5Validation.cs`。
- [ ] `Program.cs` 默认测试路径接入 `Phase5Validation.Validate()`。
- [ ] `FoxgloveSdk.Tests.csproj` 加入 `Phase5Validation.cs`。
- [ ] 时间测试：
  连续调用 `FoxgloveTimeUtil.NowUnixTimeNs()`，断言后一个值大于等于前一个值。
- [ ] 时间测试：
  `FoxgloveTimeUtil.NowUnixTimeNs()` 与 `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000` 的差距在合理窗口内。
- [ ] 时间测试：
  `SystemClock.NowNs` 与 `FoxgloveTimeUtil.NowUnixTimeNs()` 差距在合理窗口内。
- [ ] 生命周期测试：
  使用 fake transport 创建 `FoxgloveRuntime`，`Start()` 后 `Stop()` 再 `Start()`，确认不会因为 session dispose 误 dispose transport。
- [ ] 生命周期测试：
  `FoxgloveRuntime.Dispose()` 后 fake transport 的 `Dispose()` 被调用一次。
- [ ] session 测试：
  `FoxgloveSession.Dispose()` 解绑事件后，再触发 fake transport 事件不会调用已释放 session 的 handler。
- [ ] 异常路径测试：
  fake transport 模拟 send failure 时，验证 publish 不抛出到调用方。
- [ ] 异常路径测试：
  fake / test backend 模拟 `BroadcastText`、`BroadcastBinary` 发送失败时，验证遍历使用快照且坏 client 会走统一 disconnect 路径。
- [ ] Unity 调用测试：
  `FoxgloveManager.PublishJson()` 在 server 未启动时不会每帧重复 `Debug.LogWarning`；如采用 warn-once，验证 `StartServer()` 成功后 warning flag 可重置。
- [ ] subscription 测试：
  malformed subscribe JSON 不增加 subscription。
- [ ] subscription 测试：
  unknown op 不改变 channel 或 subscription 状态。
- [ ] link.xml 测试（两层验收）：
  ① **包内模板**: 检查 `Packages/dev.unity2foxglove.sdk/Runtime/link.xml` 存在且包含 `Newtonsoft.Json` 与 `Unity.FoxgloveSDK`。这是 SDK 分发模板，不作为 IL2CPP 生效文件。
  ② **Assets 生效文件**: 搜索 `Untiy2Foxglove/Assets/**/link.xml`，断言至少一个存在且包含上述两个 assembly 的 preserve 规则。Unity 只接受 `Assets/` 下的 link.xml 作为 linker preserve root。如果找不到 valid candidate，测试直接失败。
- [ ] 保持 Phase 0-4 全部验证通过。
- [ ] 默认命令：

```powershell
dotnet run --project "Packages\dev.unity2foxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj"
```

## F. Unity IL2CPP 构建验证入口

- [ ] 新增 `Untiy2Foxglove/Assets/Editor/FoxglovePhase5Build.cs`。
- [ ] 该 editor script 提供静态方法：
  `FoxglovePhase5Build.BuildWindowsIl2Cpp()`。
- [ ] 构建目标固定：
  `BuildTarget.StandaloneWindows64`。
- [ ] 构建输出目录固定：
  `build/Unity/Phase5WindowsIL2CPP/`。
- [ ] 构建包含当前项目的 enabled scenes；若没有 enabled scene，则使用：
  `Assets/Scenes/SampleScene.unity`。
- [ ] 构建前设置 scripting backend：
  Windows Standalone 使用 IL2CPP。
- [ ] 构建前设置 architecture：
  x64。
- [ ] 构建前设置 managed stripping level：
  建议 `Medium`，用于验证 link.xml 是否足够。
- [ ] 构建失败时抛异常，让 Unity batchmode 返回非 0。
- [ ] 构建成功时在日志中输出构建路径。
- [ ] 默认验证命令（IL2CPP 需 10-30 分钟，`-logFile -` 将日志实时输出到终端，`Tee-Object` 同时保存到文件）：

```powershell
Remove-Item -Recurse -Force "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\Untiy2Foxglove\Library" -ErrorAction SilentlyContinue; & "C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe" -batchmode -quit -projectPath "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\Untiy2Foxglove" -executeMethod FoxglovePhase5Build.BuildWindowsIl2Cpp -logFile - 2>&1 | Tee-Object -FilePath "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\Unity\phase5-il2cpp.log"
```

## G. Native Backend 可行性评估

- [ ] 新增 `Documentation~/NativeBackendEvaluation.md`。
- [ ] 记录本地 C FFI 入口：
  `foxglove-sdk/c/include/foxglove-c/foxglove-c.h`。
- [ ] 记录可用于 server 的关键 API：
  `foxglove_server_start`、`foxglove_server_stop`、`foxglove_server_clear_session`、`foxglove_server_get_port`、`foxglove_server_get_client_count`。
- [ ] 记录可用于 typed channel 的关键 API：
  `foxglove_channel_create_*`、`foxglove_channel_log_*`、对应 schema/encode 函数。
- [ ] 记录 Phase 5 不实现 native backend 的原因：
  当前纯 C# 路径已完成 Phase 0-4 验收，Native 切换会引入 DLL 构建、P/Invoke、生命周期和平台分发成本。
- [ ] 记录触发 Native Backend 实施的条件：
  IL2CPP 构建失败且无法通过 link.xml 修复、ManagedWsBackend 稳定性无法达标、性能无法满足 camera/高频消息需求、或需要官方 SDK 高级能力。
- [ ] 记录 Windows native plugin 预期产物：
  `foxglove_c.dll` 或等价动态库，放入 `Plugins/native/x86_64` 或 Unity 推荐平台目录。
- [ ] 记录 P/Invoke 风险：
  string lifetime、callback threading、opaque pointer ownership、error code mapping、shutdown ordering。
- [ ] 不新增任何 `[DllImport]` 调用。
- [ ] 不修改 `NativeFoxgloveBackend.cs` 为可运行实现；最多更新注释指向评估文档。

## H. 文档更新

- [ ] 更新 `Documentation~/README.md` 状态为 Phase 5。
- [ ] README 增加 Windows IL2CPP 构建验证命令。
- [ ] README 记录 Phase 5 仍继续使用纯 C# Managed backend。
- [ ] README 记录 Native Backend 评估结果位置：
  `Documentation~/NativeBackendEvaluation.md`。
- [ ] 更新 `Documentation~/Architecture.md` 的 Phase 表：
  Phase 5 标记为 Done 或 In Progress，按实际完成状态填写。
- [ ] Architecture 增加 runtime/session/transport lifecycle 说明。
- [ ] Architecture 增加 IL2CPP/link.xml 裁剪策略。
- [ ] Architecture 增加时间戳策略：
  `FoxgloveTimeUtil` + `SystemClock` 统一生成 Unix epoch ns。
- [ ] README / Architecture 记录 package identity 已迁移为 `dev.unity2foxglove.sdk`，并避免继续把旧 id 写成安装入口。
- [ ] 更新 [[00_PLAN]] 中 Phase 5 状态摘要，保持上位计划和执行计划一致。

## I. Package Identity 迁移

- [ ] 将包目录从 `Packages/dev.unityfoxglove.sdk` 迁移为 `Packages/dev.unity2foxglove.sdk`，并确认 Unity `.meta` 与 samples/docs 路径没有遗留旧目录引用。
- [ ] 修改 `Packages/dev.unity2foxglove.sdk/package.json`：
  `"name"` 从 `dev.unityfoxglove.sdk` 改为 `dev.unity2foxglove.sdk`。
- [ ] 修改 `Untiy2Foxglove/Packages/manifest.json`：
  dependency key 从 `dev.unityfoxglove.sdk` 改为 `dev.unity2foxglove.sdk`；`file:` 路径同步指向 `Packages/dev.unity2foxglove.sdk`。
- [ ] 运行 `rg "dev\.unityfoxglove\.sdk"`，更新 `00_PLAN.md`、`06_PHASE5_PLAN.md`、README、Architecture、Samples 或其他文档中的 package id 引用。
- [ ] 不修改根命名空间 `Unity.FoxgloveSDK`，不修改 asmdef 名称，除非后续明确要做 API/namespace 级 rename。
- [ ] 在 Unity Editor 打开 `Untiy2Foxglove` 后确认 package 解析正常，无 duplicate package 或 missing dependency。
- [ ] 如 Unity 生成 `Packages/packages-lock.json` 或等价 lock 文件，确认旧 id 已被替换且没有 stale entry。

## 建议执行顺序

1. 先做 `I. Package Identity 迁移`，让后续新增/修改文件都落在目标包路径下。
2. 做 `A. 时间戳精度统一`。
3. 补 `E. Phase 5 自动化测试` 中时间相关用例，确认当前毫秒实现会暴露差异。
4. 做 `B. Transport 生命周期所有权`。
5. 补 `E. Phase 5 自动化测试` 中 lifecycle 用例。
6. 做 `C. Managed WebSocket 与 Unity 调用异常路径加固`。
7. 补 `E. Phase 5 自动化测试` 中异常路径与 warn-once 用例。
8. 做 `D. IL2CPP 裁剪保护`。
9. 补 `E. Phase 5 自动化测试` 中 link.xml 路径策略用例。
10. 做 `F. Unity IL2CPP 构建验证入口`。
11. 运行完整 dotnet 验证。
12. 运行 Unity batchmode IL2CPP 构建验证。
13. 做 `G. Native Backend 可行性评估`。
14. 做 `H. 文档更新`。
15. 最后复跑 dotnet 验证，并保存 Unity 构建日志路径。

## 验收矩阵

### 自动化验收

- [ ] 输出包含 Phase 0、Phase 1、Phase 2、Phase 3、Phase 4、Phase 5 六组验证。
- [ ] 所有测试失败时必须抛异常，进程返回非 0。
- [ ] `FoxgloveTimeUtil.NowUnixTimeNs()` 单调不倒退。
- [ ] `SystemClock.NowNs` 与 `FoxgloveTimeUtil.NowUnixTimeNs()` 使用同一策略。
- [ ] `FoxgloveRuntime.Stop()` 后可再次 `Start()`。
- [ ] `FoxgloveSession.Dispose()` 不 dispose transport。
- [ ] `FoxgloveSession.Dispose()` 解绑 transport 四个事件，释放后 fake transport 再触发事件不会调用旧 session handler。
- [ ] `FoxgloveRuntime.Dispose()` dispose transport。
- [ ] `BroadcastText`、`BroadcastBinary` 使用 `_clients.ToArray()` 快照后遍历，send failure 会移除坏 client。
- [ ] `FoxgloveManager.PublishJson()` 在 server 未启动时不会每帧刷 warning。
- [ ] malformed JSON、unknown op、无效 subscribe/unsubscribe 不污染状态。
- [ ] `Packages/dev.unity2foxglove.sdk/Runtime/link.xml` 存在并包含 Newtonsoft.Json 与 Unity.FoxgloveSDK，测试不依赖 `bin/Debug/net9.0` 当前工作目录。
- [ ] `package.json`、包目录与 Unity manifest 使用新 package identity：`dev.unity2foxglove.sdk`。
- [ ] Phase 0-4 无回归。

### Unity 构建验收

- [ ] Unity batchmode 命令返回 0。
- [ ] `build/Unity/phase5-il2cpp.log` 中无 C# 编译错误。
- [ ] `build/Unity/phase5-il2cpp.log` 中无 IL2CPP conversion failure。
- [ ] `build/Unity/phase5-il2cpp.log` 中无 Newtonsoft.Json 裁剪导致的运行时代码生成错误。
- [ ] `build/Unity/Phase5WindowsIL2CPP/` 生成 Windows Player。

### 手动验收

- [ ] 在 Unity Editor 中打开 `Untiy2Foxglove`，无 package 编译错误。
- [ ] Unity Package Manager 识别 `dev.unity2foxglove.sdk`，没有残留的旧 id dependency。
- [ ] Play Mode 后 Foxglove 能连接 `ws://127.0.0.1:8765`。
- [ ] `/tf`、`/scene`、`/unity/camera` 都能看到。
- [ ] 停止 Play Mode 后端口无残留。
- [ ] 重复 Play/Stop 后仍可连接。
- [ ] Windows IL2CPP Player 启动后，Foxglove 可连接并看到同样 topics。
- [ ] 关闭 Player 后端口无残留。

### Native 评估验收

- [ ] `NativeBackendEvaluation.md` 记录 `foxglove-c.h` 关键 server API。
- [ ] `NativeBackendEvaluation.md` 记录 P/Invoke 风险和资源所有权风险。
- [ ] `NativeBackendEvaluation.md` 给出 Phase 5 结论：
  继续纯 C#，Native Backend 暂不实施。
- [ ] `NativeBackendEvaluation.md` 给出未来切换触发条件。

## 风险与注意事项

- IL2CPP 裁剪风险：
  Newtonsoft.Json 大量依赖反射，Phase 5 必须交付 `link.xml`，不要只写文档风险。
- 时间戳风险：
  不要声明 Foxglove `time` capability；修正消息 timestamp 和 logTime 不等于实现 Time frame。
- transport 生命周期风险：
  不要让 `FoxgloveSession.Dispose()` dispose 共享 transport，否则 runtime stop/start 会回归。
- WebSocket 异常路径风险：
  broadcast failure 不能静默吞掉坏 client，否则后续 publish 会持续命中失效连接。
- Unity Console 风险：
  server 未启动时的 publish no-op 不能每帧刷 warning；否则 Camera、Transform、Scene 三个 publisher 会快速淹没真实错误。
- link.xml 测试路径风险：
  dotnet harness 的当前工作目录通常是 `bin/Debug/net9.0`，测试不能用 `Runtime/link.xml` 这种相对路径。
- package id 迁移风险：
  迁移 package id 与包目录，但不顺手改 namespace/asmdef；实施时必须同步 Unity manifest、sample path、`.meta` 和文档引用，避免出现旧 id 与新 id 混用。
- Unity 构建脚本风险：
  构建脚本只放在 `Untiy2Foxglove/Assets/Editor`，不要放进 package runtime assembly。
- Native Backend 风险：
  本阶段只评估，不写 `[DllImport]`，避免把动态库构建和平台分发问题提前带进 MVP 主线。
- 工作区风险：
  当前仓库包含大量 Unity `.meta` 和 `Untiy2Foxglove` 项目文件，实施时只修改 Phase 5 明确列出的路径，避免无关资源 churn。

## 后续阶段预留

- [[00_PLAN]] Phase 6：MCAP 写入。
- [[00_PLAN]] Phase 6：Parameters / ParametersSubscribe。
- [[00_PLAN]] Phase 6：Services。
- [[00_PLAN]] Phase 6：Assets / fetchAsset。
- [[00_PLAN]] Phase 6：PlaybackControl。
- 高性能 camera 二进制路径。
- 真正的 `NativeFoxgloveBackend` P/Invoke 实现与 Windows native plugin 分发。

## 下一阶段入口

> [!success]
> Phase 5 完成后，SDK 应具备 Windows Standalone IL2CPP 构建证据、异常路径回归测试、Newtonsoft 裁剪保护和 Native Backend 评估结论。之后再进入 [[00_PLAN]] 的 Phase 6，按实际需求选择 MCAP、Parameters、Services 等可选能力。
