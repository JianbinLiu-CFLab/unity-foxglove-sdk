---
title: Phase 16 - 开源发布准备
aliases:
  - Phase 16 Plan
  - Release Readiness Plan
tags:
  - plan
  - unity
  - foxglove
  - release
  - upm
status: planned
updated: 2026-05-05
---

# 1. Phase 16 - 开源发布准备

> [!summary]
> Phase 16 的目标不是继续加功能，而是把当前 SDK 收口到可以公开发布的状态。发布范围固定为现有 JSON/WebSocket/MCAP/Replay/FoxRun 能力；Protobuf 编码支持明确放到 [[18_PHASE17_PLAN]]。

## 1.1 Context

当前项目已经完成从 Phase 0 到 Phase 15 的主要能力：

- Unity Package 基础结构：`Packages/dev.unity2foxglove.sdk`
- Foxglove WebSocket 协议主链路：handshake、serverInfo、advertise、subscribe、publish
- Unity 发布器：Transform、Scene、Camera、Parameters、Services、Assets、PlaybackControl
- MCAP：录制、读取、Replay、压缩、metadata、attachments
- DX：`FoxgloveManager`、Inspector、`[FoxRun]`、build-time generated `.g.cs`
- IL2CPP：Windows Player 构建已验证，`FoxrunBuildPreprocess` 已能为 Player 生成 source fallback
- 跨平台构建入口：`Scripts/build_unity_il2cpp.py`

但距离开源发布还有几类收口工作：

- 文档状态仍有过期内容，例如 `Documentation~/README.md` 仍写 Phase 12 complete。
- 仓库里存在本地构建输出痕迹，例如 `Tests/Runtime/bin`、`Tests/Runtime/obj`。
- Root README / LICENSE / CHANGELOG / release notes 还不完整。
- GitHub Actions CI 缺失。
- Package 发布边界需要检查，避免把本地 demo、third-party clone、build artifact 混入发布内容。

## 1.2 Goal

- [ ] 发布一个可安装、可读、可验证的 Unity UPM package。
- [ ] 明确 release 版本号、license、changelog 和 release notes。
- [ ] README 能让第一次接触项目的人完成安装、运行 Demo、连接 Foxglove。
- [ ] CI 至少能跑 dotnet runtime tests，并对 package 文件结构做基础校验。
- [ ] 仓库不包含本地构建产物、Unity generated folder、agent/tooling 状态或第三方 clone。
- [ ] 发布前通过一套固定的 Editor / IL2CPP / Foxglove 手工验收。

## 1.3 Non-goals

- 不实现 Protobuf encoding。
- 不引入 Google.Protobuf / protoc / FileDescriptorSet。
- 不扩展 ROS2、FlatBuffers、gRPC。
- 不把 Native Backend 作为发布前必需项。
- 不要求首版 CI 做 Unity IL2CPP 构建，除非后续配置 Unity license。
- 不把 `Untiy2Foxglove` demo project 伪装成 package 源码的一部分发布到 UPM。

# 2. Release Scope

## 2.1 首版能力声明

首版 release 应声明以下能力已支持：

- Foxglove WebSocket server：本地 `ws://127.0.0.1:8765`
- JSON encoding：`ServerInfo.supportedEncodings = ["json"]`
- JSON Schema channel：FrameTransform、SceneUpdate、CompressedImage 等现有 schema
- Parameters：get/set
- Services：JSON request/response/failure/timeout
- ConnectionGraph / ClientPublish
- Assets / fetchAsset
- PlaybackControl
- MCAP writer / reader / replay / compression / attachments
- Unity Editor + Standalone Player
- Windows IL2CPP 已验证
- `[FoxRun]` source generation + Player generated source fallback

## 2.2 首版限制声明

首版 release 必须明确写出以下限制：

- Protobuf encoding 暂不支持，见 [[18_PHASE17_PLAN]]。
- WebGL 暂不支持。
- Native Backend 暂不启用，当前主线为纯 C# Managed WebSocket。
- macOS/Linux Player 构建脚本已预留 target，但需要对应 Unity Build Support；实际 release 阶段只承诺已手工验收的平台。
- `FoxRun` 生成文件位于 `Assets/Scripts/Generated/`，属于 build artifact，不应提交。

# 3. Batch 16A - Package 元数据与发布边界

## 3.1 Task 16A-1：确认 package identity

目标：确认发布包身份稳定，不再在发布前改名。

检查项：

- `package.json.name = dev.unity2foxglove.sdk`
- 根命名空间：`Unity.FoxgloveSDK`
- asmdef 名称与命名空间一致
- package displayName 不误导官方归属

建议：

- `displayName` 可考虑从 `Foxglove SDK` 改为 `Unity2Foxglove SDK`，避免看起来像 Foxglove 官方 SDK。
- `author.name` 应明确项目作者或组织名。

验收：

- Unity Package Manager 能显示正确名称、版本、描述。
- 文档中 package id 全部一致。

## 3.2 Task 16A-2：版本策略

目标：确定首个公开版本号。

建议：

- 如果 API 仍可能快速变化，首版用 `0.1.0`。
- 如果希望强调当前功能已经可演示但仍非稳定 API，用 `0.1.0-preview.1`。

需要更新：

- `Packages/dev.unity2foxglove.sdk/package.json`
- `CHANGELOG.md`
- release notes
- docs 中的状态说明

验收：

- 版本号在 package、changelog、release notes 中一致。

## 3.3 Task 16A-3：Package 内容边界检查

目标：确认 UPM package 内只包含用户安装所需内容。

应包含：

- `Runtime/`
- `Editor/`
- `Plugins/` 中真正需要的 DLL
- `Documentation~/`
- `Samples~/`
- `Tests/`
- `package.json`

不应包含：

- `bin/`
- `obj/`
- `Library/`
- `build/`
- `.dotnet-home/`
- local third-party clone
- generated `Assets/Scripts/Generated/`

验收：

- `Packages/dev.unity2foxglove.sdk` 内没有可删除构建产物。
- `.gitignore` 能防止构建产物再次进入 git status。

# 4. Batch 16B - 文档收口

## 4.1 Task 16B-1：Root README

目标：仓库根目录需要一个面向 GitHub 读者的 README。

内容建议：

- 项目是什么
- 当前支持能力
- 安装方式
- Quick Start
- Demo / Samples
- 构建与测试
- Roadmap
- License

验收：

- 新读者不打开 `00_PLAN.md` 也能理解项目用途。
- README 不含本机绝对路径。

## 4.2 Task 16B-2：Package Documentation README

目标：更新 `Packages/dev.unity2foxglove.sdk/Documentation~/README.md`。

当前发现：

- Status 仍写 Phase 12 complete。
- 手工 IL2CPP 构建命令仍使用旧的直接 Unity batchmode 命令和绝对路径。
- Phase 表可能落后于 Phase 15/16。

需要更新：

- 状态改为 Phase 15 complete / Phase 16 planned。
- 构建命令改为 `python Scripts/build_unity_il2cpp.py --target win64`。
- 说明新 build artifact 布局：`build/Unity/<target>-il2cpp-<timestamp>/`。
- Protobuf 放 Roadmap，不写成支持。

验收：

- 文档中不再出现本机 `D:\...` 绝对路径作为推荐命令。
- 文档的能力列表与 `serverInfo` 实际声明一致。

## 4.3 Task 16B-3：Samples 文档

目标：确保 `Samples~/BasicVisualization` 能作为首个用户入口。

需要检查：

- README 是否说明如何导入 sample。
- 场景中需要挂哪些组件。
- Foxglove 连接地址。
- Parameters / Services / Replay / MCAP 的手工验收步骤。
- 坐标系说明：UnityRaw 与 FoxgloveStandard/FoxgloveRos 的差异。

验收：

- 用户只按 sample README 操作，能看到 3D、Camera、Parameters、Service Call、Plot。

## 4.4 Task 16B-4：Architecture 文档

目标：`Documentation~/Architecture.md` 反映当前真实架构。

重点检查：

- Runtime / Session / Transport 生命周期
- RecordingController / ReplayController
- FoxRun ISG build-time generated source
- MCAP writer / reader / replay 数据流
- 不再声明未支持的 capability
- Protobuf 标为 future work

验收：

- Architecture 中没有与 runtime 行为冲突的 capability 声明。

# 5. Batch 16C - License、第三方依赖与法律文本

## 5.1 Task 16C-1：LICENSE

目标：添加开源许可证。

待决策：

- MIT：最简单，适合 SDK。
- Apache-2.0：更强调专利授权，适合长期维护。

验收：

- 根目录存在 `LICENSE`。
- package 文档和 README 均引用 license。

## 5.2 Task 16C-2：第三方依赖声明

目标：列出发布包携带或依赖的第三方组件。

需要覆盖：

- `com.unity.nuget.newtonsoft-json`
- compression DLLs：LZ4 / Zstd 相关 DLL
- MCAP 格式参考来源
- Foxglove schema 来源
- Unity packages

建议文件：

- `THIRD_PARTY_NOTICES.md`

验收：

- 用户能知道哪些依赖被打包，哪些来自 Unity Package Manager。

## 5.3 Task 16C-3：压缩 DLL 发布检查

目标：避免 demo project 能编译、package 用户安装却缺 DLL。

检查项：

- `Runtime/Plugins/compression/` 内 DLL 存在。
- asmdef 引用名称与 DLL 名称一致。
- `.meta` 平台设置正确。
- package.json 描述不要承诺不存在的依赖路径。

验收：

- 新 Unity 项目只安装 package 后能解析 compression assembly 引用。

# 6. Batch 16D - 仓库清理

## 6.1 Task 16D-1：清理构建产物

目标：删除不应进入仓库的构建输出。

当前已发现：

- `Packages/dev.unity2foxglove.sdk/Tests/Runtime/bin`
- `Packages/dev.unity2foxglove.sdk/Tests/Runtime/obj`
- `build/`
- `Untiy2Foxglove/Library` 等 Unity generated folders

验收：

- `git status` 中不出现 bin/obj/build/Library 相关文件。
- `.gitignore` 覆盖这些路径。

## 6.2 Task 16D-2：清理本地参考仓库

目标：避免把 local clone 发布出去。

已知本地目录：

- `foxglove-sdk/`
- `third-party/`

处理原则：

- 保留本地参考，但必须被 `.gitignore` 排除。
- 文档可以引用它们作为开发参考，但不能要求 package 用户拥有这些目录。

验收：

- Git 不跟踪 local clone。
- 发布文档不依赖本机 `third-party` 路径。

## 6.3 Task 16D-3：清理 agent/tooling 状态

目标：本地 AI 工具状态不进入 release。

检查项：

- `.claude/`
- `.dotnet-home/`
- 临时 generated 文件
- test logs

验收：

- `.gitignore` 覆盖本地工具状态。

# 7. Batch 16E - CI 与自动化验证

## 7.1 Task 16E-1：GitHub Actions dotnet test

目标：公开仓库至少有基础 CI。

建议 workflow：

```text
.github/workflows/dotnet-tests.yml
```

执行：

```powershell
dotnet test Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
```

验收：

- PR / push 时自动跑 runtime tests。
- CI 能显示测试总数和失败日志。

## 7.2 Task 16E-2：Package 结构检查

目标：用轻量脚本防止 package 结构回归。

检查项：

- `package.json` 可解析。
- `Runtime/Unity.FoxgloveSDK.asmdef` 存在。
- `Documentation~/README.md` 存在。
- `Samples~/BasicVisualization/README.md` 存在。
- package 内没有 `bin/obj/build/Library`。

验收：

- CI 中 package structure check 通过。

## 7.3 Task 16E-3：Unity CI 暂缓策略

目标：避免发布前被 Unity license / runner 环境拖住。

决策：

- Phase 16 CI 只要求 dotnet tests + package structure check。
- Unity Editor import / IL2CPP build 作为手工 release checklist。
- 后续有 Unity license 后再加 Unity Test Runner / IL2CPP CI。

验收：

- 文档明确说明哪些验证自动化，哪些仍为手工。

# 8. Batch 16F - Release 验收矩阵

## 8.1 Task 16F-1：自动化测试

发布前必须通过：

```powershell
dotnet test Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
```

验收：

- Phase 0-15 runtime validation 全部通过。
- 测试报告中的总数记录到 release notes。

## 8.2 Task 16F-2：Unity Editor 验收

手工检查：

- Unity 打开 `Untiy2Foxglove` 无编译错误。
- Sample Scene 可 Play。
- Foxglove 连接 `ws://127.0.0.1:8765`。
- Topics 可见 `/tf`、`/scene`、`/unity/camera`、`/debug/*`。
- Parameters 可读写 `/cube/color`、`/cube/scale`。
- Service Call 可调用 `/cube/reset_pose`。
- Plot 可观察 Transform 曲线。

验收：

- Editor Play Mode 与文档描述一致。

## 8.3 Task 16F-3：IL2CPP Player 验收

构建命令：

```powershell
python Scripts\build_unity_il2cpp.py --target win64
```

验收：

- 构建产物位于 `build/Unity/<target>-il2cpp-<timestamp>/`。
- `build.log` 和 Player 产物在同一个 run 目录。
- log 中出现 `[FoxrunBuildPreprocess]`。
- Player 启动后 Foxglove 能连接。
- `/debug/*` topic 在 Player 中可见。

## 8.4 Task 16F-4：Package 安装验收

目标：从“开发项目能跑”提升到“新项目安装 package 能跑”。

建议步骤：

1. 创建一个临时 Unity 空项目。
2. 通过 local package path 安装 `Packages/dev.unity2foxglove.sdk`。
3. 导入 `Samples~/BasicVisualization`。
4. 打开 sample scene。
5. 运行 Editor Play Mode 验收。

验收：

- 新项目安装没有缺 asmdef / DLL / meta 文件。

# 9. Batch 16G - Release Notes 与发布动作

## 9.1 Task 16G-1：CHANGELOG

目标：记录首版发布内容。

建议结构：

```text
## 0.1.0 - 2026-05-xx

### Added
### Changed
### Fixed
### Known Limitations
```

Known Limitations 必须包含：

- Protobuf unsupported
- WebGL unsupported
- Unity CI / IL2CPP CI not yet automated

验收：

- CHANGELOG 与 README 的能力描述一致。

## 9.2 Task 16G-2：Release notes

目标：面向 GitHub Release 的短说明。

内容建议：

- 这是什么
- 如何安装
- 如何运行 sample
- 已验证平台
- 已知限制
- 下一步 Roadmap：Protobuf

验收：

- release notes 不依赖读者先看 `00_PLAN.md`。

## 9.3 Task 16G-3：Tag 与发布包

目标：形成可追溯发布点。

建议：

- tag：`v0.1.0` 或 `v0.1.0-preview.1`
- release artifact：可选 `.tgz` 或直接依赖 UPM git URL

验收：

- tag 对应 commit 的 tests / docs / package metadata 一致。

# 10. 发布前 Checklist

## 10.1 必须完成

- [ ] `17_PHASE16_PLAN.md` 已链接到 [[00_PLAN]]
- [ ] Root README 存在且不过期
- [ ] Package Documentation README 更新到 Phase 15/16 状态
- [ ] LICENSE 存在
- [ ] CHANGELOG 存在
- [ ] THIRD_PARTY_NOTICES 存在或 README 中有第三方依赖说明
- [ ] `package.json` 元数据完整
- [ ] `Samples~/BasicVisualization` 可导入可运行
- [ ] `dotnet test` 通过
- [ ] Unity Editor Play Mode 验收通过
- [ ] Windows IL2CPP Player 验收通过
- [ ] 新 Unity 项目 local package install 验收通过
- [ ] git status 无构建产物和本地工具状态

## 10.2 可以发布后再做

- [ ] Protobuf encoding
- [ ] Unity GitHub Actions IL2CPP build
- [ ] Native Backend
- [ ] WebGL
- [ ] 更完整的跨平台 Player 构建矩阵

# 11. Risk Register

## 11.1 README 过期风险

风险：用户按旧命令操作，看到绝对路径或旧 Phase 状态。

对策：

- Phase 16 强制更新 Root README 与 Package README。
- 所有命令使用相对路径。
- IL2CPP 构建统一使用 `Scripts/build_unity_il2cpp.py`。

## 11.2 Package 安装失败风险

风险：demo 项目能跑，但新 Unity 项目缺 DLL、meta 或 asmdef 引用。

对策：

- 做一次新项目 local package install 验收。
- 检查 compression DLL 与 asmdef 引用。

## 11.3 仓库污染风险

风险：`bin/obj/build/Library` 或 generated 文件进入 release。

对策：

- Phase 16 清理仓库。
- CI 增加 package structure check。
- `.gitignore` 覆盖构建产物。

## 11.4 发布范围膨胀风险

风险：发布前继续加入 Protobuf 或 Native Backend，导致新依赖链拖慢 release。

对策：

- Phase 16 只做 release readiness。
- Protobuf 只在 [[18_PHASE17_PLAN]] 推进。

# 12. Definition of Done

Phase 16 完成时应满足：

- 仓库可公开浏览，README 能独立解释项目。
- Package 可被新 Unity 项目安装。
- Sample 可运行并连接 Foxglove。
- dotnet tests 通过。
- Windows IL2CPP Player 验收通过。
- release notes 和 changelog 可直接用于 GitHub Release。
- Protobuf 被明确标为下一阶段，而不是半支持状态。

